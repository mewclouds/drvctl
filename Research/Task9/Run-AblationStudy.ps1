[CmdletBinding()]
param(
    [string] $KnownGoodWim = (Join-Path $PSScriptRoot '..\..\bin\Release\net10.0-windows\win-x64\publish\install-acpivpc.wim'),
    [string] $OriginalWim = (Join-Path $PSScriptRoot '..\..\bin\Release\net10.0-windows\win-x64\publish\install-original.wim'),
    [string] $AllDriversWim = (Join-Path $PSScriptRoot '..\..\bin\Release\net10.0-windows\win-x64\publish\install-dism.wim'),
    [string] $Package = 'C:\Drivers\acpivpc.inf_amd64_fd0a5766a43dadc1',
    [string] $StudyRoot = 'C:\DrvCtlAblationStudy',
    [string] $DrvCtlAssembly = (Join-Path $PSScriptRoot '..\..\bin\Release\net10.0-windows\win-x64\drvctl.dll'),
    [string] $HiveEditorAssembly = (Join-Path $PSScriptRoot 'Task9HiveEditor\bin\Release\net10.0-windows\win-x64\Task9HiveEditor.dll'),
    [string[]] $ExperimentIds,
    [switch] $Resume
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$PackageIdentity = 'acpivpc.inf_amd64_fd0a5766a43dadc1'
$PackageKey = "DriverDatabase\DriverPackages\$PackageIdentity"
$ConfigurationKey = "$PackageKey\Configurations\ACPIVPC_Inst.NTamd64"
$DeviceIdKey = 'DriverDatabase\DeviceIds\ACPI\VEN_VPC&DEV_2004'
$PnpKey = 'Microsoft\Windows\CurrentVersion\Setup\PnpLockdownFiles\%SystemRoot%/System32/drivers/AcpiVpc.sys'
$Dism = Join-Path $env:SystemRoot 'System32\dism.exe'

function Resolve-FullPath([string] $Path) {
    return [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
}

function Assert-SafeRoot([string] $Path) {
    $resolved = Resolve-FullPath $Path
    $windows = Resolve-FullPath $env:SystemRoot
    if ($resolved -eq [IO.Path]::GetPathRoot($resolved)) { throw "Study root cannot be a filesystem root: $resolved" }
    if ($resolved.Equals($windows, 'OrdinalIgnoreCase') -or $resolved.StartsWith($windows + '\', 'OrdinalIgnoreCase')) { throw "Study root cannot be Windows or a child of Windows: $resolved" }
    foreach ($forbidden in @($KnownGoodWim, $OriginalWim, $AllDriversWim, $Package)) {
        $candidate = Resolve-FullPath $forbidden
        if ($resolved.Equals($candidate, 'OrdinalIgnoreCase') -or $resolved.StartsWith($candidate + '\', 'OrdinalIgnoreCase')) { throw "Study root overlaps protected input: $candidate" }
    }
}

function Format-Command([string] $FilePath, [string[]] $Arguments) {
    return (($FilePath, $Arguments | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }) -join ' ')
}

function Invoke-NativeCapture {
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter(Mandatory)] [string[]] $Arguments,
        [Parameter(Mandatory)] [string] $LogBase,
        [int[]] $AllowedExitCodes = @(0)
    )
    $start = (Get-Date).ToUniversalTime()
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    foreach ($argument in $Arguments) { [void] $startInfo.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) { throw "Could not start process: $FilePath" }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    $end = (Get-Date).ToUniversalTime()
    $record = [ordered]@{
        FilePath = $FilePath
        Arguments = $Arguments
        Command = Format-Command $FilePath $Arguments
        ExitCode = $process.ExitCode
        StartUtc = $start.ToString('O')
        EndUtc = $end.ToString('O')
        StdOut = $stdout
        StdErr = $stderr
    }
    $parent = Split-Path -Parent $LogBase
    if ($parent) { [IO.Directory]::CreateDirectory($parent) | Out-Null }
    [IO.File]::WriteAllText($LogBase + '.json', ($record | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($LogBase + '.log', "Command: $($record.Command)`r`nExit code: $($record.ExitCode)`r`nStart UTC: $($record.StartUtc)`r`nEnd UTC: $($record.EndUtc)`r`n`r`nSTDOUT`r`n$stdout`r`nSTDERR`r`n$stderr", [Text.UTF8Encoding]::new($false))
    if ($AllowedExitCodes -notcontains $process.ExitCode) { throw "Process exited $($process.ExitCode): $($record.Command)`n$stderr`n$stdout" }
    return [pscustomobject]$record
}

function Assert-OriginalHashes {
    foreach ($entry in $script:ProtectedHashes.GetEnumerator()) {
        $actual = (Get-FileHash -LiteralPath $entry.Key -Algorithm SHA256).Hash
        if ($actual -ne $entry.Value) { throw "Protected input changed: $($entry.Key). Expected $($entry.Value), found $actual." }
    }
}

function Assert-NoMountedImages([string] $LogBase) {
    $result = Invoke-NativeCapture $Dism @('/English', '/Get-MountedWimInfo') $LogBase
    if ($result.StdOut -notmatch 'No mounted images found') { throw "A WIM is already mounted. Clean it up before running Task 9.`n$($result.StdOut)" }
}

function Remove-VerifiedDirectory([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $target = Resolve-FullPath $Path
    if (-not $target.StartsWith($script:StudyRootResolved + '\', 'OrdinalIgnoreCase')) { throw "Refusing to remove directory outside study root: $target" }
    if ($target.Equals($script:StudyRootResolved, 'OrdinalIgnoreCase')) { throw "Refusing to remove study root." }
    Remove-Item -LiteralPath $target -Recurse -Force
}

function Invoke-WithMountedImage {
    param(
        [string] $ExperimentDirectory,
        [string] $WimPath,
        [bool] $ReadOnly,
        [string] $Phase,
        [scriptblock] $Operation
    )
    $mount = Join-Path $ExperimentDirectory "mount-$Phase"
    [IO.Directory]::CreateDirectory($mount) | Out-Null
    $mounted = $false
    try {
        $arguments = @('/English', '/Mount-Image', "/ImageFile:$WimPath", '/Index:1', "/MountDir:$mount")
        if ($ReadOnly) { $arguments += '/ReadOnly' }
        [void](Invoke-NativeCapture $Dism $arguments (Join-Path $ExperimentDirectory "logs\$Phase-mount"))
        $mounted = $true
        & $Operation $mount
        $offlineLog = Join-Path $mount 'Windows\INF\setupapi.offline.log'
        if (Test-Path -LiteralPath $offlineLog) { Copy-Item -LiteralPath $offlineLog -Destination (Join-Path $ExperimentDirectory "$Phase-setupapi.offline.log") -Force }
        $unmountMode = if ($ReadOnly) { '/Discard' } else { '/Commit' }
        [void](Invoke-NativeCapture $Dism @('/English', '/Unmount-Image', "/MountDir:$mount", $unmountMode) (Join-Path $ExperimentDirectory "logs\$Phase-unmount"))
        $mounted = $false
    }
    finally {
        if ($mounted) {
            [void](Invoke-NativeCapture $Dism @('/English', '/Unmount-Image', "/MountDir:$mount", '/Discard') (Join-Path $ExperimentDirectory "logs\$Phase-cleanup") @(0, 2))
        }
    }
}

function Copy-Fixture([string] $Source, [string] $Destination) {
    if (Test-Path -LiteralPath $Destination) {
        if ($Resume) { return }
        throw "Destination already exists: $Destination"
    }
    Copy-Item -LiteralPath $Source -Destination $Destination
}

function Invoke-HiveMutation([object] $Definition, [string] $ExperimentDirectory, [string] $Mount, [string] $SourceWimHash) {
    $mountedHive = Join-Path $Mount "Windows\System32\config\$($Definition.Hive)"
    $editDirectory = Join-Path $ExperimentDirectory 'hive-edit'
    [IO.Directory]::CreateDirectory($editDirectory) | Out-Null
    $input = Join-Path $editDirectory "$($Definition.Hive)-input"
    $output = Join-Path $editDirectory "$($Definition.Hive)-output"
    foreach ($scratch in @($input, $output)) {
        if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Force }
    }
    Copy-Item -LiteralPath $mountedHive -Destination $input
    $arguments = @($HiveEditorAssembly, $input, $output, (Join-Path $ExperimentDirectory 'mutation.json'), $Definition.Id, $SourceWimHash, $Definition.Hive, $Definition.Operation, $Definition.Key)
    if ($Definition.Operation -ne 'delete-tree' -and $null -ne $Definition.Argument) { $arguments += [string]$Definition.Argument }
    [void](Invoke-NativeCapture 'dotnet' $arguments (Join-Path $ExperimentDirectory 'logs\hive-mutation'))
    [IO.File]::Copy($output, $mountedHive, $true)
}

function Invoke-FileMutation([object] $Definition, [string] $ExperimentDirectory, [string] $Mount, [string] $SourceWimHash) {
    $target = Join-Path $Mount $Definition.RelativePath
    $state = Get-Item -LiteralPath $target
    $manifest = [ordered]@{
        ExperimentId = $Definition.Id
        SourceWimHash = $SourceWimHash
        TargetHive = $null
        RegistryPath = $null
        ValueName = $null
        Operation = 'delete-file'
        RelativePath = $Definition.RelativePath
        BeforeSize = $state.Length
        BeforeSha256 = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
        AfterSize = $null
        AfterSha256 = $null
        ExpectedSingleMutation = $true
    }
    Remove-Item -LiteralPath $target -Force
    [IO.File]::WriteAllText((Join-Path $ExperimentDirectory 'mutation.json'), ($manifest | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
}

function Invoke-Inspection([object] $Definition, [string] $ExperimentDirectory, [string] $WimPath) {
    $inspection = [ordered]@{}
    Invoke-WithMountedImage $ExperimentDirectory $WimPath $true 'inspect' {
        param($mount)
        $drivers = Invoke-NativeCapture $Dism @('/English', "/Image:$mount", '/Get-Drivers') (Join-Path $ExperimentDirectory 'logs\inspect-get-drivers')
        $info = Invoke-NativeCapture $Dism @('/English', "/Image:$mount", '/Get-DriverInfo', "/Driver:$($Definition.OemInf)") (Join-Path $ExperimentDirectory 'logs\inspect-get-driver-info') @(0, 2, 87, 1168)
        $inspection.GetDriversExitCode = $drivers.ExitCode
        $inspection.GetDriverInfoExitCode = $info.ExitCode
        $inspection.PackageListed = $drivers.StdOut -match [regex]::Escape($Definition.OemInf)
        $inspection.PackageRecognized = $info.ExitCode -eq 0
        $inspection.GetDriversOutput = $drivers.StdOut
        $inspection.GetDriverInfoOutput = $info.StdOut
    }
    [IO.File]::WriteAllText((Join-Path $ExperimentDirectory 'inspection.json'), ($inspection | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    return [pscustomobject]$inspection
}

function Invoke-Repair([object] $Definition, [string] $ExperimentDirectory, [string] $WimPath) {
    $repair = [ordered]@{}
    Invoke-WithMountedImage $ExperimentDirectory $WimPath $false 'repair' {
        param($mount)
        $result = Invoke-NativeCapture $Dism @('/English', "/Image:$mount", '/Add-Driver', "/Driver:$($Definition.Inf)") (Join-Path $ExperimentDirectory 'logs\repair-add-driver')
        $repair.ExitCode = $result.ExitCode
        $repair.StdOut = $result.StdOut
        $repair.StdErr = $result.StdErr
    }
    [IO.File]::WriteAllText((Join-Path $ExperimentDirectory 'repair.json'), ($repair | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    return [pscustomobject]$repair
}

function Invoke-Analysis([string] $Name, [string] $Before, [string] $After, [string] $PackagePath, [string] $ExperimentDirectory) {
    $experimentId = Split-Path -Leaf $ExperimentDirectory
    $workspace = Join-Path $script:StudyRootResolved "reports\$experimentId\$Name"
    $report = Join-Path $workspace 'publication-analysis.json'
    if ((Test-Path -LiteralPath $report) -and $Resume) { return $report }
    if (Test-Path -LiteralPath $workspace) { Remove-VerifiedDirectory $workspace }
    [IO.Directory]::CreateDirectory((Split-Path -Parent $workspace)) | Out-Null
    [void](Invoke-NativeCapture 'dotnet' @($DrvCtlAssembly, 'analyze-publication', $Before, $After, $PackagePath, '--index', '1', '--workspace', $workspace) (Join-Path $ExperimentDirectory "logs\analyze-$Name"))
    foreach ($scratchName in @('baseline', 'serviced')) { Remove-VerifiedDirectory (Join-Path $workspace $scratchName) }
    if (-not (Test-Path -LiteralPath $report)) { throw "Expected analysis report was not created: $report" }
    return $report
}

function New-Definition {
    param([string]$Id,[string]$Field,[string]$Hive,[string]$Operation,[string]$Key,[AllowNull()][string]$Argument,[string]$SourceWim,[string]$PackagePath,[string]$OemInf,[string]$Inf,[string]$RelativePath)
    return [pscustomobject]@{Id=$Id;Field=$Field;Hive=$Hive;Operation=$Operation;Key=$Key;Argument=$Argument;SourceWim=$SourceWim;Package=$PackagePath;OemInf=$OemInf;Inf=$Inf;RelativePath=$RelativePath}
}

$KnownGoodWim = Resolve-FullPath $KnownGoodWim
$OriginalWim = Resolve-FullPath $OriginalWim
$AllDriversWim = Resolve-FullPath $AllDriversWim
$Package = Resolve-FullPath $Package
$DrvCtlAssembly = Resolve-FullPath $DrvCtlAssembly
$HiveEditorAssembly = Resolve-FullPath $HiveEditorAssembly
$StudyRootResolved = Resolve-FullPath $StudyRoot
Assert-SafeRoot $StudyRootResolved
foreach ($required in @($KnownGoodWim,$OriginalWim,$AllDriversWim,$DrvCtlAssembly,$HiveEditorAssembly)) { if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required file not found: $required" } }
if (-not (Test-Path -LiteralPath $Package -PathType Container)) { throw "Package not found: $Package" }
$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Task 9 requires an elevated PowerShell process for disposable DISM mounts.' }
if ((Test-Path -LiteralPath $StudyRootResolved) -and -not $Resume) { throw "Study root already exists: $StudyRootResolved. Use -Resume or choose a new root." }
[IO.Directory]::CreateDirectory($StudyRootResolved) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $StudyRootResolved 'logs')) | Out-Null

$ProtectedHashes = [ordered]@{
    $OriginalWim = (Get-FileHash -LiteralPath $OriginalWim -Algorithm SHA256).Hash
    $KnownGoodWim = (Get-FileHash -LiteralPath $KnownGoodWim -Algorithm SHA256).Hash
}
$packageHashes = @(Get-ChildItem -LiteralPath $Package -File | Sort-Object Name | ForEach-Object { [ordered]@{Name=$_.Name;Size=$_.Length;Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} })
Assert-NoMountedImages (Join-Path $StudyRootResolved 'logs\mounted-before')

$acpiInf = (Get-ChildItem -LiteralPath $Package -Filter '*.inf' -File | Select-Object -First 1).FullName
$tailPackage = Resolve-FullPath 'C:\Drivers\a-volutenhapo4swc.inf_amd64_b6d9d50049bf9522'
$tailInf = (Get-ChildItem -LiteralPath $tailPackage -Filter '*.inf' -File | Select-Object -First 1).FullName
$tailKey = 'DriverDatabase\DriverPackages\a-volutenhapo4swc.inf_amd64_b6d9d50049bf9522'

$definitions = @(
    (New-Definition '00-control' 'Control duplicate add' $null 'control' $null $null $KnownGoodWim $Package 'oem0.inf' $acpiInf $null),
    (New-Definition '01-statusflags-delete' 'StatusFlags' 'SYSTEM' 'delete-value' $PackageKey 'StatusFlags' $KnownGoodWim $Package 'oem0.inf' $acpiInf $null),
    (New-Definition '02-configflags-delete' 'ConfigFlags' 'SYSTEM' 'delete-value' $ConfigurationKey 'ConfigFlags' $KnownGoodWim $Package 'oem0.inf' $acpiInf $null),
    (New-Definition '03-configscope-delete' 'ConfigScope' 'SYSTEM' 'delete-value' $ConfigurationKey 'ConfigScope' $KnownGoodWim $Package 'oem0.inf' $acpiInf $null),
    (New-Definition '04-custom-property-delete' 'Custom property 0xFFFF0012' 'SYSTEM' 'delete-value' "$PackageKey\Properties\{4da162c1-5eb1-4140-a444-5064c9814e76}\0009" '@default' $KnownGoodWim $Package 'oem0.inf' $acpiInf $null),
    (New-Definition '05-version-tail-zero' 'Version tail (non-ACPIVPC specimen)' 'DRIVERS' 'zero-tail-8' $tailKey 'Version' $AllDriversWim $tailPackage 'oem4.inf' $tailInf $null),
    (New-Definition '06-version-delete' 'Version' 'SYSTEM' 'delete-value' $PackageKey 'Version' $KnownGoodWim $Package 'oem0.inf' $acpiInf $null),
    (New-Definition '07-deviceid-delete' 'DeviceIds mapping' 'SYSTEM' 'delete-value' $DeviceIdKey 'oem0.inf' $KnownGoodWim $Package 'oem0.inf' $acpiInf $null),
    (New-Definition '08-deviceid-zero' 'DeviceIds blob corruption' 'SYSTEM' 'set-binary-zero' $DeviceIdKey 'oem0.inf' $KnownGoodWim $Package 'oem0.inf' $acpiInf $null),
    (New-Definition '09-descriptors-delete' 'Descriptors subtree' 'SYSTEM' 'delete-tree' "$PackageKey\Descriptors\ACPI\VEN_VPC&DEV_2004" $null $KnownGoodWim $Package 'oem0.inf' $acpiInf $null),
    (New-Definition '10-strings-delete' 'Strings subtree' 'SYSTEM' 'delete-tree' "$PackageKey\Strings" $null $KnownGoodWim $Package 'oem0.inf' $acpiInf $null),
    (New-Definition '11-service-owners-delete' 'Service Owners' 'SYSTEM' 'delete-value' 'ControlSet001\Services\ACPIVPC' 'Owners' $KnownGoodWim $Package 'oem0.inf' $acpiInf $null),
    (New-Definition '12-service-displayname-delete' 'Service DisplayName' 'SYSTEM' 'delete-value' 'ControlSet001\Services\ACPIVPC' 'DisplayName' $KnownGoodWim $Package 'oem0.inf' $acpiInf $null),
    (New-Definition '13-pnplockdown-source-delete' 'PnpLockdown Source' 'SOFTWARE' 'delete-value' $PnpKey 'Source' $KnownGoodWim $Package 'oem0.inf' $acpiInf $null),
    (New-Definition '14-pnplockdown-owners-delete' 'PnpLockdown Owners' 'SOFTWARE' 'delete-value' $PnpKey 'Owners' $KnownGoodWim $Package 'oem0.inf' $acpiInf $null),
    (New-Definition '15-pnplockdown-class-delete' 'PnpLockdown Class' 'SOFTWARE' 'delete-value' $PnpKey 'Class' $KnownGoodWim $Package 'oem0.inf' $acpiInf $null),
    (New-Definition '16-pnplockdown-record-delete' 'PnpLockdown record' 'SOFTWARE' 'delete-tree' $PnpKey $null $KnownGoodWim $Package 'oem0.inf' $acpiInf $null),
    (New-Definition '17-reflected-sys-delete' 'Reflected AcpiVpc.sys' $null 'delete-file' $null $null $KnownGoodWim $Package 'oem0.inf' $acpiInf 'Windows\System32\drivers\AcpiVpc.sys')
)

if ($ExperimentIds) { $definitions = @($definitions | Where-Object Id -in $ExperimentIds) }
if ($definitions.Count -eq 0) { throw 'No matching experiments were selected.' }

$results = [Collections.Generic.List[object]]::new()
foreach ($definition in $definitions) {
    $experimentDirectory = Join-Path $StudyRootResolved $definition.Id
    [IO.Directory]::CreateDirectory($experimentDirectory) | Out-Null
    $resultPath = Join-Path $experimentDirectory 'experiment-run.json'
    if ((Test-Path -LiteralPath $resultPath) -and $Resume) {
        $existingRun = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json -Depth 30
        if ($existingRun.Status -eq 'Completed') {
            $results.Add($existingRun)
            continue
        }
        $repairLogPath = Join-Path $experimentDirectory 'logs\repair-add-driver.json'
        if ($existingRun.Status -eq 'Failed') {
            if ($definition.Id -eq '05-version-tail-zero') {
                $results.Add($existingRun)
                continue
            }
            if (Test-Path -LiteralPath $repairLogPath) {
                $failedRepair = Get-Content -LiteralPath $repairLogPath -Raw | ConvertFrom-Json -Depth 10
                if ($failedRepair.ExitCode -ne 0) {
                    $results.Add($existingRun)
                    continue
                }
            }
        }
    }
    $run = [ordered]@{Id=$definition.Id;Field=$definition.Field;Status='Running';Error=$null;SourceWim=$definition.SourceWim;SourceWimHash=(Get-FileHash -LiteralPath $definition.SourceWim -Algorithm SHA256).Hash}
    try {
        $sourceCopy = Join-Path $experimentDirectory 'source-copy.wim'
        Copy-Fixture $definition.SourceWim $sourceCopy
        $ablated = if ($definition.Operation -eq 'control') { $sourceCopy } else { Join-Path $experimentDirectory 'ablated.wim' }
        if ($definition.Operation -ne 'control') {
            Copy-Fixture $sourceCopy $ablated
            $mutationPath = Join-Path $experimentDirectory 'mutation.json'
            if (-not ($Resume -and (Test-Path -LiteralPath $mutationPath))) {
                Invoke-WithMountedImage $experimentDirectory $ablated $false 'ablate' {
                    param($mount)
                    if ($definition.Operation -eq 'delete-file') { Invoke-FileMutation $definition $experimentDirectory $mount $run.SourceWimHash }
                    else { Invoke-HiveMutation $definition $experimentDirectory $mount $run.SourceWimHash }
                }
            }
        }
        $inspectionPath = Join-Path $experimentDirectory 'inspection.json'
        $inspection = if ($Resume -and (Test-Path -LiteralPath $inspectionPath)) { Get-Content -LiteralPath $inspectionPath -Raw | ConvertFrom-Json -Depth 10 } else { Invoke-Inspection $definition $experimentDirectory $ablated }
        $repaired = Join-Path $experimentDirectory 'repaired.wim'
        Copy-Fixture $ablated $repaired
        $repairPath = Join-Path $experimentDirectory 'repair.json'
        $repair = if ($Resume -and (Test-Path -LiteralPath $repairPath)) { Get-Content -LiteralPath $repairPath -Raw | ConvertFrom-Json -Depth 10 } else { Invoke-Repair $definition $experimentDirectory $repaired }
        $reports = [ordered]@{}
        $analysisPackage = if ($definition.Id -eq '05-version-tail-zero') { $Package } else { $definition.Package }
        if ($definition.Operation -ne 'control') { $reports.GoodToAblated = Invoke-Analysis 'good-to-ablated' $definition.SourceWim $ablated $analysisPackage $experimentDirectory }
        $reports.AblatedToRepaired = Invoke-Analysis 'ablated-to-repaired' $ablated $repaired $analysisPackage $experimentDirectory
        $reports.GoodToRepaired = Invoke-Analysis 'good-to-repaired' $definition.SourceWim $repaired $analysisPackage $experimentDirectory
        $run.Status = 'Completed'
        $run.Inspection = $inspection
        $run.Repair = $repair
        $run.Reports = $reports
        $run.SourceCopyWim = $sourceCopy
        $run.AblatedWim = $ablated
        $run.RepairedWim = $repaired
    }
    catch {
        $run.Status = 'Failed'
        $run.Error = $_.Exception.ToString()
        if ($definition.Id -eq '00-control') {
            [IO.File]::WriteAllText($resultPath, ($run | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
            throw
        }
    }
    [IO.File]::WriteAllText($resultPath, ($run | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
    $results.Add([pscustomobject]$run)
}

$studyManifest = [ordered]@{
    Study = 'Task 9 ablation and Windows self-repair study'
    StudyRoot = $StudyRootResolved
    ImageIndex = 1
    ProtectedWims = @($ProtectedHashes.GetEnumerator() | ForEach-Object { [ordered]@{Path=$_.Key;Sha256Before=$_.Value;Sha256After=(Get-FileHash -LiteralPath $_.Key -Algorithm SHA256).Hash} })
    Package = $Package
    PackageHashes = $packageHashes
    Experiments = @($results)
}
[IO.File]::WriteAllText((Join-Path $StudyRootResolved 'study-manifest.json'), ($studyManifest | ConvertTo-Json -Depth 30), [Text.UTF8Encoding]::new($false))
Assert-OriginalHashes
Assert-NoMountedImages (Join-Path $StudyRootResolved 'logs\mounted-after')
Write-Output (Join-Path $StudyRootResolved 'study-manifest.json')
