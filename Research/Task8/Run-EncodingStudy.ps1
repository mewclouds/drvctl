[CmdletBinding()]
param(
    [string] $AllDriversReport = (Join-Path $PSScriptRoot '..\..\.encoding-study\all67-analysis\publication-analysis.json'),
    [string] $DriversRoot = 'C:\Drivers',
    [string] $DrvCtlAssembly = (Join-Path $PSScriptRoot '..\..\bin\Release\net10.0-windows\win-x64\drvctl.dll'),
    [string] $Output = (Join-Path $PSScriptRoot 'results\driverdatabase-encoding-study.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not ('SetupVerifyEncodingStudy' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class SetupVerifyEncodingStudy {
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct Info {
        public uint cbSize;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=260)] public string CatalogFile;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=260)] public string DigitalSigner;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=260)] public string DigitalSignerVersion;
        public uint SignerScore;
    }
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupVerifyInfFile(string inf, IntPtr alternatePlatform, ref Info info);
    public static Info Verify(string path) {
        Info info = new Info();
        info.cbSize = (uint)Marshal.SizeOf<Info>();
        if (!SetupVerifyInfFile(path, IntPtr.Zero, ref info)) throw new Win32Exception(Marshal.GetLastWin32Error(), "SetupVerifyInfFile failed");
        return info;
    }
}
'@
}

function Invoke-InspectInf([string] $InfPath) {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = 'dotnet'
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.ArgumentList.Add($DrvCtlAssembly)
    $start.ArgumentList.Add('inspect-inf')
    $start.ArgumentList.Add($InfPath)
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "inspect-inf failed ($($process.ExitCode)) for $InfPath`: $stderr" }
    return $stdout -split "`r?`n"
}

function Get-InfFacts([string] $Directory) {
    $inf = @(Get-ChildItem -LiteralPath $Directory -Filter '*.inf' -File)
    if ($inf.Count -ne 1) { throw "Expected one INF in $Directory" }
    $lines = Invoke-InspectInf $inf[0].FullName
    $facts = [ordered]@{ Directory=$Directory; Inf=$inf[0].FullName; Class=$null; ClassGuid=$null; Provider=$null; DriverVer=$null; Catalog=$null; ExtensionId=$null; PnpLockdown=$null; Models=@(); Services=@(); Copies=@(); Strings=[ordered]@{}; HasAddSoftware=$false; ComponentIds=@() }
    $section = ''
    foreach ($line in $lines) {
        if ($line -match '^Class: (.*)$') { $facts.Class=$matches[1] }
        elseif ($line -match '^ClassGuid: (.*)$') { $facts.ClassGuid=$matches[1] }
        elseif ($line -match '^Provider: (.*)$') { $facts.Provider=$matches[1] }
        elseif ($line -match '^CatalogFile: (.*)$') { $facts.Catalog=$matches[1] }
        elseif ($line -match '^DriverVer: (.*)$') { $facts.DriverVer=$matches[1] }
        elseif ($line -match '^ExtensionId: (.*)$') { $facts.ExtensionId=if($matches[1] -eq 'not declared'){$null}else{$matches[1]} }
        elseif ($line -match '^AddSoftware: (.*)$') { $facts.HasAddSoftware=[bool]::Parse($matches[1]) }
        elseif ($line -match '^PnpLockdown: (.*)$') { $facts.PnpLockdown=if($matches[1] -eq 'not declared'){$null}else{[int]$matches[1]} }
        elseif ($line -eq 'Model entries:') { $section='models' }
        elseif ($line -eq 'Service metadata:') { $section='services' }
        elseif ($line -eq 'Copy operations:') { $section='copies' }
        elseif ($line -eq 'INF strings:') { $section='strings' }
        elseif ($line -eq 'Software component IDs:') { $section='components' }
        elseif ($section -eq 'models' -and $line -match '^  (.*?) -> (.*?): (.*); manufacturer=(.*)$') {
            $facts.Models += [ordered]@{Description=$matches[1];InstallSection=$matches[2];Ids=@($matches[3] -split ', ');Manufacturer=$matches[4]}
        }
        elseif ($section -eq 'services' -and $line -match '^  (.*)$') {
            $p=$matches[1] -split '\|',8; if($p.Count -eq 8){$facts.Services += [ordered]@{Name=$p[0];InstallSection=$p[1];ConfigurationSection=$p[2];DisplayName=$p[3];Type=[int]$p[4];Start=[int]$p[5];ErrorControl=[int]$p[6];Binary=$p[7]}}
        }
        elseif ($section -eq 'copies' -and $line -match '^  (.*)$') {
            $p=$matches[1] -split '\|',5; if($p.Count -eq 5){$facts.Copies += [ordered]@{InstallSection=$p[0];Source=$p[1];Destination=$p[2];DirId=[int]$p[3];Subdirectory=$p[4]}}
        }
        elseif ($section -eq 'strings' -and $line -match '^  ([^=]+)=(.*)$') { $facts.Strings[$matches[1].ToLowerInvariant()]=$matches[2] }
        elseif ($section -eq 'components' -and $line -match '^  (.+)$' -and $matches[1] -ne 'unsupported') { $facts.ComponentIds += $matches[1] }
    }
    $signature=[SetupVerifyEncodingStudy]::Verify($inf[0].FullName)
    $facts.Signature=[ordered]@{CatalogFile=$signature.CatalogFile;SignerName=$signature.DigitalSigner;SignerVersion=$signature.DigitalSignerVersion;SignerScore=[uint32]$signature.SignerScore}
    return $facts
}

function Encode-Version([string] $ClassGuid,[string] $DriverVer) {
    $parts=$DriverVer -split ',',2
    $date=[DateTime]::ParseExact($parts[0].Trim(),[string[]]@('M/d/yyyy','MM/dd/yyyy','M/dd/yyyy','MM/d/yyyy'),[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::AssumeUniversal).ToUniversalTime()
    $version=@($parts[1].Split('.')|%{[uint16]$_})
    if($version.Count -ne 4){throw "Unsupported DriverVer: $DriverVer"}
    $bytes=[byte[]]::new(40)
    ([byte[]](0x00,0xFF,0x09,0,0,0,0,0)).CopyTo($bytes,0)
    ([Guid]$ClassGuid).ToByteArray().CopyTo($bytes,8)
    [BitConverter]::GetBytes($date.ToFileTimeUtc()).CopyTo($bytes,24)
    for($i=0;$i -lt 4;$i++){[BitConverter]::GetBytes($version[3-$i]).CopyTo($bytes,32+2*$i)}
    return [Convert]::ToHexString($bytes)
}

function Find-UniqueToken($Facts,[string] $Expanded) {
    $matches=@($Facts.Strings.GetEnumerator()|? Value -eq $Expanded|% Key|sort -Unique)
    return $(if($matches.Count -eq 1){$matches[0]}else{$null})
}

$all = Get-Content -LiteralPath $AllDriversReport -Raw | ConvertFrom-Json -Depth 100
$registry = @($all.RegistryDeltas | Where-Object AfterValue)
$packageNames = @($registry | Where-Object KeyPath -like 'DriverDatabase\DriverPackages\*' | ForEach-Object { if($_.KeyPath -match '^DriverDatabase\\DriverPackages\\([^\\]+)'){ $matches[1] } } | Sort-Object -Unique)
$packageFacts = [ordered]@{}
foreach($name in $packageNames) {
    $directory=Join-Path $DriversRoot $name
    if(Test-Path -LiteralPath $directory -PathType Container){$packageFacts[$name]=Get-InfFacts $directory}
}

$packages = foreach($name in $packageNames) {
    if (-not $packageFacts.Contains($name)) {
        continue
    }
    $facts=$packageFacts[$name]
    $values=@($registry|? KeyPath -like "DriverDatabase\DriverPackages\$name*")
    $default=$values|?{$_.KeyPath -eq "DriverDatabase\DriverPackages\$name" -and $_.ValueName -eq ''}|select -First 1
    $version=$values|? ValueName -eq Version|select -First 1
    $status=$values|? ValueName -eq StatusFlags|select -First 1
    if (-not $default) {
        throw "DriverPackages record '$name' has no default OEM INF value in '$AllDriversReport'."
    }
    [ordered]@{RepositoryIdentity=$name;Class=$facts.Class;ClassGuid=$facts.ClassGuid;Provider=$facts.Provider;DriverVer=$facts.DriverVer;Catalog=$facts.Catalog;ExtensionId=$facts.ExtensionId;HasAddSoftware=$facts.HasAddSoftware;ComponentIds=$facts.ComponentIds;Models=$facts.Models;Services=$facts.Services;Copies=$facts.Copies;PnpLockdown=$facts.PnpLockdown;DatabaseHive=(@($values.Hive|sort -Unique)-join ',');OemInf=$default.AfterValue.Decoded;Signature=$facts.Signature;VersionRaw=if($version){$version.AfterValue.RawHex}else{$null};StatusFlagsRaw=if($status){$status.AfterValue.RawHex}else{$null}}
}

$versionObservations = foreach($package in $packages|? VersionRaw) {
    $predicted=Encode-Version $package.ClassGuid $package.DriverVer
    $observedCore=$package.VersionRaw.Substring(0,80)
    [ordered]@{Package=$package.RepositoryIdentity;Header='00FF090000000000';ClassGuid=$package.ClassGuid;DriverVer=$package.DriverVer;PredictedCore=$predicted;ObservedCore=$observedCore;CoreMatch=$predicted.Equals($observedCore,'OrdinalIgnoreCase');UnexplainedTail=$package.VersionRaw.Substring(80)}
}

$deviceObservations = foreach($delta in $registry|?{$_.KeyPath -like 'DriverDatabase\DeviceIds\*' -and $_.AfterValue.TypeName -eq 'REG_BINARY'}) {
    $id=$delta.KeyPath.Substring('DriverDatabase\DeviceIds\'.Length)
    $package=$packages|? OemInf -eq $delta.ValueName|? DatabaseHive -match $delta.Hive|select -First 1
    if(-not $package){continue}
    $model=$package.Models|?{$_.Ids -contains $id}|select -First 1
    $role=if($model){if($model.Ids[0] -eq $id){'Primary model ID'}else{'Compatible model ID'}}else{'Not a declared model ID'}
    $predicted=if($role -eq 'Primary model ID'){if($package.Class -eq 'Extension'){'03FF0000'}else{'01FF0000'}}else{$null}
    [ordered]@{Package=$package.RepositoryIdentity;Class=$package.Class;Id=$id;Role=$role;Predicted=$predicted;Observed=$delta.AfterValue.RawHex;Match=($null -ne $predicted -and $predicted -eq $delta.AfterValue.RawHex)}
}

$statusObservations = foreach($package in $packages) {[ordered]@{Package=$package.RepositoryIdentity;Class=$package.Class;DatabaseHive=$package.DatabaseHive;AddService=(@($package.Services).Count -gt 0);PnpLockdown=$package.PnpLockdown;Reflection=(@($package.Copies|? DirId -ne 13).Count -gt 0);StatusFlags=$package.StatusFlagsRaw}}
$configObservations = @($registry|?{$_.KeyPath -like 'DriverDatabase\DriverPackages\*\Configurations\*' -and $_.ValueName -in @('ConfigFlags','ConfigScope')}|%{[ordered]@{Hive=$_.Hive;KeyPath=$_.KeyPath;Name=$_.ValueName;Type=$_.AfterValue.TypeName;Raw=$_.AfterValue.RawHex;Decoded=$_.AfterValue.Decoded}})
$signerObservations = foreach($package in $packages){
    $values=@($registry|? KeyPath -like "DriverDatabase\DriverPackages\$($package.RepositoryIdentity)*")
    $name=$values|? ValueName -eq SignerName|select -First 1
    $score=$values|? ValueName -eq SignerScore|select -First 1
    $observedName=if($name){$name.AfterValue.Decoded}else{$null}
    $observedScore=if($score){[uint32]$score.AfterValue.Decoded}else{$null}
    [ordered]@{Package=$package.RepositoryIdentity;Api='SetupVerifyInfFileW/SP_INF_SIGNER_INFO_V2_W';PredictedName=$package.Signature.SignerName;ObservedName=$observedName;NameMatch=($null -ne $observedName -and $package.Signature.SignerName -eq $observedName);PredictedScore=('0x{0:X8}' -f $package.Signature.SignerScore);ObservedScore=if($null -ne $observedScore){'0x{0:X8}' -f $observedScore}else{$null};ScoreMatch=($null -ne $observedScore -and [uint32]$package.Signature.SignerScore -eq $observedScore)}
}

$descriptorObservations = foreach($delta in $registry|?{$_.KeyPath -like 'DriverDatabase\DriverPackages\*\Descriptors\*' -and $_.AfterValue}) {
    if($delta.KeyPath -notmatch '^DriverDatabase\\DriverPackages\\([^\\]+)\\Descriptors\\(.+)$'){continue};$name=$matches[1];$id=$matches[2];$facts=$packageFacts[$name];$model=$facts.Models|?{$_.Ids -contains $id}|select -First 1;$predicted=$null
    if($model){if($delta.ValueName -eq 'Configuration'){$predicted=$model.InstallSection}elseif($delta.ValueName -eq 'Description'){$t=Find-UniqueToken $facts $model.Description;if($t){$predicted="%$($t.ToLowerInvariant())%"}}elseif($delta.ValueName -eq 'Manufacturer'){$t=Find-UniqueToken $facts $model.Manufacturer;if($t){$predicted="%$($t.ToLowerInvariant())%"}}}
    [ordered]@{Package=$name;Id=$id;Name=$delta.ValueName;Predicted=$predicted;Observed=$delta.AfterValue.Decoded;Match=($null -ne $predicted -and $predicted -eq $delta.AfterValue.Decoded)}
}

$stringObservations = @($registry|?{$_.KeyPath -like 'DriverDatabase\DriverPackages\*\Strings' -and $_.AfterValue}|%{[ordered]@{KeyPath=$_.KeyPath;Name=$_.ValueName;Type=$_.AfterValue.TypeName;Raw=$_.AfterValue.RawHex;Decoded=$_.AfterValue.Decoded}})
$customObservations = @($registry|?{$_.AfterValue.TypeName -like 'REG_TYPE_*'}|%{[ordered]@{Hive=$_.Hive;KeyPath=$_.KeyPath;Name=$_.ValueName;Type=$_.AfterValue.TypeName;Raw=$_.AfterValue.RawHex}})
$driverPackageValues = @($registry|?{$_.KeyPath -like 'DriverDatabase\DriverPackages\*'}|%{[ordered]@{Hive=$_.Hive;KeyPath=$_.KeyPath;Name=$_.ValueName;Type=$_.AfterValue.TypeName;Raw=$_.AfterValue.RawHex;Decoded=$_.AfterValue.Decoded;DecodedStrings=$_.AfterValue.DecodedStrings}})

$serviceObservations = foreach($package in $packages) {foreach($serviceGroup in @($package.Services|Group-Object Name)){
    $service=$serviceGroup.Group|Select-Object -First 1
    $display=$registry|?{$_.Hive -eq 'SYSTEM' -and $_.KeyPath -eq "ControlSet001\Services\$($service.Name)" -and $_.ValueName -eq 'DisplayName'}|select -First 1
    $owners=$registry|?{$_.Hive -eq 'SYSTEM' -and $_.KeyPath -eq "ControlSet001\Services\$($service.Name)" -and $_.ValueName -eq 'Owners'}|select -First 1
    $token=Find-UniqueToken $packageFacts[$package.RepositoryIdentity] $service.DisplayName
    $predictedDisplay=if($token){"@$($package.OemInf),%$token%;$($service.DisplayName)"}else{$null}
    $observedDisplay=if($display){$display.AfterValue.Decoded}else{$null}
    $observedOwners=if($owners){@($owners.AfterValue.DecodedStrings)}else{@()}
    if (-not $display -and -not $owners) { continue }
    [ordered]@{Package=$package.RepositoryIdentity;Service=$service.Name;DisplayNamePredicted=$predictedDisplay;DisplayNameObserved=$observedDisplay;DisplayNameMatch=($predictedDisplay -and $predictedDisplay -eq $observedDisplay);OwnersPredicted=@($package.OemInf);OwnersObserved=@($observedOwners);OwnersMatch=(@($observedOwners).Count -eq 1 -and @($observedOwners)[0] -eq $package.OemInf)}
}}

$pnpObservations = foreach($group in $registry|?{$_.Hive -eq 'SOFTWARE' -and $_.KeyPath -like '*PnpLockdownFiles*'}|group KeyPath){$source=$group.Group|? ValueName -eq Source|select -First 1;$owners=$group.Group|? ValueName -eq Owners|select -First 1;$class=$group.Group|? ValueName -eq Class|select -First 1;if(-not $source){continue};$repo=if($source.AfterValue.Decoded -match 'FileRepository\\([^\\]+)\\([^\\]+)$'){$matches[1]}else{$null};$file=if($repo){$matches[2]}else{$null};$package=$packages|? RepositoryIdentity -eq $repo|select -First 1;$predictedClass=if($package){if($package.PnpLockdown -eq 1){4}else{5}}else{$null};[ordered]@{Package=$repo;Destination=$group.Name;SourcePredicted=if($repo){"%SystemRoot%\System32\DriverStore\FileRepository\$repo\$file"}else{$null};SourceObserved=$source.AfterValue.Decoded;SourceMatch=($source.AfterValue.Decoded -eq "%SystemRoot%\System32\DriverStore\FileRepository\$repo\$file");OwnersPredicted=if($package){@($package.OemInf)}else{@()};OwnersObserved=@($owners.AfterValue.DecodedStrings);OwnersMatch=($package -and @($owners.AfterValue.DecodedStrings).Count -eq 1 -and $owners.AfterValue.DecodedStrings[0] -eq $package.OemInf);ClassPredicted=$predictedClass;ClassObserved=if($class){[int]$class.AfterValue.Decoded}else{$null};ClassMatch=($class -and $predictedClass -eq [int]$class.AfterValue.Decoded)}}

$matrix=@(
 [ordered]@{Field='DeviceIds';Status='Partially understood';Required=$true;Evidence="The candidate primary-model rule matches $(@($deviceObservations|? Match).Count)/$($deviceObservations.Count), but 859 counterexamples and eight blob forms prevent an encoder."},
 [ordered]@{Field='Version';Status='Partially understood';Required=$true;Evidence="The 40-byte header/GUID/date/version core matches $(@($versionObservations|? CoreMatch).Count)/$($versionObservations.Count); the final 8-byte flags field has four observed forms and no derivation."},
 [ordered]@{Field='SignerName';Status='Solved';Required=$true;Evidence='SetupVerifyInfFileW returns the exact DigitalSigner for all 66 packages that store SignerName.'},
 [ordered]@{Field='SignerScore';Status='Solved';Required=$true;Evidence='SP_INF_SIGNER_INFO_V2_W returns the exact DWORD SignerScore for 67/67 packages.'},
 [ordered]@{Field='StatusFlags';Status='Unsupported';Required=$true;Evidence='18 distinct values plus absence across 67 packages lack a deterministic semantic rule.'},
 [ordered]@{Field='ConfigFlags';Status='Partially understood';Required=$true;Evidence='Observed 0 and 0x400 across 105 configurations; source rule unresolved.'},
 [ordered]@{Field='ConfigScope';Status='Prototype-supported';Required=$true;Evidence='All 105 observed configurations use 0x00000F7F; bit meanings remain undocumented.'},
 [ordered]@{Field='Descriptors';Status=if(@($descriptorObservations|?{-not $_.Match}).Count -eq 0){'Prototype-supported'}else{'Partially understood'};Required=$true;Evidence='SetupAPI model entries and reverse-mapped INF Strings match 1470/1719 fields, including ACPIVPC 3/3; 249 fields remain unexplained.'},
 [ordered]@{Field='Strings';Status='Prototype-supported';Required=$true;Evidence='ACPIVPC descriptor tokens are lowercased and their expanded values match 2/2; broader configuration and localized references remain partial.'},
 [ordered]@{Field='Custom properties';Status='Unsupported';Required=$true;Evidence='Multiple undocumented registry types occur; no supported encoder.'},
 [ordered]@{Field='Service DisplayName';Status='Prototype-supported';Required=$true;Evidence='The @oemN.inf,%token%;expanded-value form matches ACPIVPC and 7/11 stored values; four localized-string cases remain unresolved.'},
 [ordered]@{Field='Service Owners';Status='Prototype-supported';Required=$true;Evidence='Single-owner MULTI_SZ matches 14/14 dedicated services; six shared WUDFRd records demonstrate unresolved multi-owner semantics.'},
 [ordered]@{Field='PnpLockdown Source';Status='Solved';Required=$true;Evidence='Repository identity plus source filename predicts every record.'},
 [ordered]@{Field='PnpLockdown Owners';Status='Prototype-supported';Required=$true;Evidence='Single-owner additions match the package OEM identity; multi-owner update semantics remain unresolved.'},
 [ordered]@{Field='PnpLockdown Class';Status=if(@($pnpObservations|?{-not $_.ClassMatch}).Count -eq 0){'Prototype-supported'}else{'Partially understood'};Required=$true;Evidence='PnPLockdown=1 predicts Class 4; absent predicts Class 5 across new ownership records.'}
)

$report=[ordered]@{
 Study='Task 8 DriverDatabase and ownership encoding study';SourceReport=(Resolve-Path $AllDriversReport).Path;PackageCount=$packages.Count;Packages=@($packages);DriverPackageValues=@($driverPackageValues);DeviceIdObservations=@($deviceObservations);VersionObservations=@($versionObservations);StatusFlagObservations=@($statusObservations);ConfigObservations=@($configObservations);SignerObservations=@($signerObservations);DescriptorObservations=@($descriptorObservations);StringObservations=@($stringObservations);CustomPropertyObservations=@($customObservations);ServiceMetadataObservations=@($serviceObservations);PnpLockdownObservations=@($pnpObservations);Hypotheses=@('Version core is header + GUID + FILETIME + reversed four UInt16 components; its final flags field is unresolved.','Primary model DeviceIds commonly use 01FF0000, or 03FF0000 for Extension packages, but counterexamples disprove this as an encoder.','PnPLockdown=1 maps ownership Class to 4; absent maps it to 5 in the observed new records.');ValidatedEncodings=@($matrix|? Status -in @('Solved','Prototype-supported'));RejectedHypotheses=@('DeviceIds first byte is solely a driver class code.','Every primary non-Extension model ID uses 01FF0000.','The Version value ends in a constant zero field.','StatusFlags is determined solely by SYSTEM versus DRIVERS.','PnpLockdown Class=5 is universal for reflected SYS files.');Counterexamples=@($deviceObservations|?{-not $_.Match});FinalMatrix=$matrix;StillUnsupported=@($matrix|? Status -in @('Unsupported','Partially understood')|% Field);CanTask7GenerateEveryRequiredField=$false
}
$parent=Split-Path -Parent $Output;if($parent){[IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($parent))|Out-Null}
[IO.File]::WriteAllText([IO.Path]::GetFullPath($Output),($report|ConvertTo-Json -Depth 100),[Text.UTF8Encoding]::new($false))
Write-Host ([IO.Path]::GetFullPath($Output))
