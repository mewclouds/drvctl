[CmdletBinding()]
param(
    [string] $BaselineWim = (Join-Path $PSScriptRoot '..\..\bin\Release\net10.0-windows\win-x64\publish\install-original.wim'),
    [string] $StudyRoot = 'C:\DrvCtlPublicationStudy',
    [string] $DriversRoot = 'C:\Drivers',
    [string] $DrvCtlAssembly = (Join-Path $PSScriptRoot '..\..\bin\Release\net10.0-windows\win-x64\drvctl.dll'),
    [switch] $Resume
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$KnownPackages = [ordered]@{
    ACPIVPC = Join-Path $DriversRoot 'acpivpc.inf_amd64_fd0a5766a43dadc1'
    RzS4LWI = Join-Path $DriversRoot 'rzs4lwi_0a58.inf_amd64_aecac1c0c5a62538'
    RzS4Ext = Join-Path $DriversRoot 'rzs4ext_0a58.inf_amd64_dc5be97a64b0151a'
}

$AdditionalPackageNames = @(
    'tbthostcontrollerextension.inf_amd64_d5a2de10b318e2b2',
    'realtekhsa.inf_amd64_665d8dafdeb163db',
    'tbthostcontrollerhsacomponent.inf_amd64_7ccbff661cca2f37',
    'raptorlakesystem.inf_amd64_53911bfa89a28e84'
)

function Resolve-SafePath([string] $Path) {
    return [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
}

function Assert-StudySafety {
    param([string] $Root, [string] $Baseline, [Collections.IDictionary] $Packages)
    $resolvedRoot = Resolve-SafePath $Root
    $windows = Resolve-SafePath $env:SystemRoot
    if ([string]::IsNullOrWhiteSpace($resolvedRoot) -or $resolvedRoot -eq [IO.Path]::GetPathRoot($resolvedRoot)) {
        throw "Study root must be a dedicated non-root directory: $resolvedRoot"
    }
    if ($resolvedRoot.Equals($windows, 'OrdinalIgnoreCase') -or $resolvedRoot.StartsWith($windows + '\', 'OrdinalIgnoreCase')) {
        throw "Study root must not be Windows or a child of Windows: $resolvedRoot"
    }
    $baselineDirectory = Resolve-SafePath (Split-Path -Parent $Baseline)
    if ($resolvedRoot.Equals($baselineDirectory, 'OrdinalIgnoreCase')) {
        throw "Study root must not be the baseline WIM directory: $resolvedRoot"
    }
    foreach ($package in $Packages.Values) {
        $resolvedPackage = Resolve-SafePath $package
        if ($resolvedRoot.Equals($resolvedPackage, 'OrdinalIgnoreCase') -or $resolvedRoot.StartsWith($resolvedPackage + '\', 'OrdinalIgnoreCase')) {
            throw "Study root must not be a package directory or its child: $resolvedRoot"
        }
    }
}

function Format-NativeCommand([string] $FilePath, [string[]] $Arguments) {
    return (($FilePath, $Arguments | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }) -join ' ')
}

function Invoke-NativeCapture {
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter(Mandatory)] [string[]] $Arguments,
        [string] $LogPath,
        [int[]] $AllowedExitCodes = @(0)
    )
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    foreach ($argument in $Arguments) { [void] $startInfo.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) { throw "Could not start native process: $FilePath" }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    $command = Format-NativeCommand $FilePath $Arguments
    if ($LogPath) {
        $parent = Split-Path -Parent $LogPath
        if ($parent) { [IO.Directory]::CreateDirectory($parent) | Out-Null }
        [IO.File]::WriteAllText($LogPath, "Command: $command`r`nExit code: $($process.ExitCode)`r`n`r`nSTDOUT`r`n$stdout`r`nSTDERR`r`n$stderr", [Text.UTF8Encoding]::new($false))
    }
    if ($AllowedExitCodes -notcontains $process.ExitCode) {
        throw "Native command failed with exit code $($process.ExitCode): $command`n$stderr`n$stdout"
    }
    return [pscustomobject]@{ Command = $command; ExitCode = $process.ExitCode; StdOut = $stdout; StdErr = $stderr }
}

function Invoke-Native {
    param([string] $FilePath, [string[]] $Arguments, [string] $LogPath, [int[]] $AllowedExitCodes = @(0))
    [void] (Invoke-NativeCapture -FilePath $FilePath -Arguments $Arguments -LogPath $LogPath -AllowedExitCodes $AllowedExitCodes)
}

function Assert-BaselineUnchanged {
    $currentHash = (Get-FileHash -LiteralPath $script:Baseline -Algorithm SHA256).Hash
    if ($currentHash -ne $script:BaselineHash) {
        throw "Immutable baseline hash changed. Expected $script:BaselineHash, observed $currentHash."
    }
}

function Copy-Baseline([string] $Destination) {
    if (Test-Path -LiteralPath $Destination) {
        if ($Resume) { return }
        throw "Destination WIM already exists: $Destination. Use -Resume or a new study root."
    }
    Copy-Item -LiteralPath $script:Baseline -Destination $Destination
    Assert-BaselineUnchanged
}

function Invoke-MountedImageOperation {
    param(
        [string] $ExperimentDirectory,
        [string] $WimPath,
        [scriptblock] $Operation
    )
    $mount = Join-Path $ExperimentDirectory 'mount'
    [IO.Directory]::CreateDirectory($mount) | Out-Null
    $mounted = $false
    try {
        Invoke-Native dism.exe @('/English', '/Mount-Image', "/ImageFile:$WimPath", '/Index:1', "/MountDir:$mount") (Join-Path $ExperimentDirectory 'dism-mount.log')
        $mounted = $true
        & $Operation $mount
        $offlineLog = Join-Path $mount 'Windows\INF\setupapi.offline.log'
        if (Test-Path -LiteralPath $offlineLog) { Copy-Item -LiteralPath $offlineLog -Destination (Join-Path $ExperimentDirectory 'setupapi.offline.log') }
        Invoke-Native dism.exe @('/English', '/Unmount-Image', "/MountDir:$mount", '/Commit') (Join-Path $ExperimentDirectory 'dism-unmount-commit.log')
        $mounted = $false
    }
    finally {
        if ($mounted) {
            [void] (Invoke-NativeCapture dism.exe @('/English', '/Unmount-Image', "/MountDir:$mount", '/Discard') (Join-Path $ExperimentDirectory 'dism-unmount-discard.log'))
        }
    }
    Assert-BaselineUnchanged
}

function Add-DriverPackage([string] $Mount, [string] $Package, [string] $LogPath) {
    Invoke-Native dism.exe @('/English', "/Image:$Mount", '/Add-Driver', "/Driver:$Package") $LogPath
}

function Remove-PublishedDriver([string] $Mount, [string] $PublishedInf, [string] $LogPath) {
    Invoke-Native dism.exe @('/English', "/Image:$Mount", '/Remove-Driver', "/Driver:$PublishedInf") $LogPath
}

function New-ServicedFixture {
    param([string] $Name, [string] $SourceWim, [object[]] $Operations)
    $directory = Join-Path $script:StudyRootResolved $Name
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $wim = Join-Path $directory 'fixture.wim'
    if ((Test-Path -LiteralPath $wim) -and $Resume) { return $wim }
    if (Test-Path -LiteralPath $wim) { throw "Fixture already exists: $wim" }
    Copy-Item -LiteralPath $SourceWim -Destination $wim
    Invoke-MountedImageOperation $directory $wim {
        param($mount)
        $sequence = 0
        foreach ($operation in $Operations) {
            $sequence++
            if ($operation.Action -eq 'Add') {
                Add-DriverPackage $mount $operation.Package (Join-Path $directory ("dism-{0:D2}-add.log" -f $sequence))
            } elseif ($operation.Action -eq 'Remove') {
                Remove-PublishedDriver $mount $operation.PublishedInf (Join-Path $directory ("dism-{0:D2}-remove.log" -f $sequence))
            } else {
                throw "Unknown research operation: $($operation.Action)"
            }
        }
    }
    return $wim
}

function Invoke-PublicationAnalysis {
    param([string] $Name, [string] $BeforeWim, [string] $AfterWim, [string] $Package)
    $directory = Join-Path $script:StudyRootResolved "reports\$Name"
    $report = Join-Path $directory 'publication-analysis.json'
    if ((Test-Path -LiteralPath $report) -and $Resume) { return $report }
    if (Test-Path -LiteralPath $directory) { throw "Analysis directory already exists: $directory" }
    [IO.Directory]::CreateDirectory((Split-Path -Parent $directory)) | Out-Null
    $result = Invoke-NativeCapture dotnet @($DrvCtlAssembly, 'analyze-publication', $BeforeWim, $AfterWim, $Package, '--index', '1', '--workspace', $directory) (Join-Path $script:StudyRootResolved "logs\analyze-$Name.log")
    $servicedLog = Join-Path $directory 'serviced\Windows\INF\setupapi.offline.log'
    if (Test-Path -LiteralPath $servicedLog) { Copy-Item -LiteralPath $servicedLog -Destination (Join-Path $directory 'setupapi.offline.log') }
    foreach ($scratch in @((Join-Path $directory 'baseline'), (Join-Path $directory 'serviced'))) {
        if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
    }
    if (-not (Test-Path -LiteralPath $report)) { throw "drvctl did not create the expected report: $report" }
    return $report
}

function Get-PackageInventory([string] $Name, [string] $Package) {
    $files = @(Get-ChildItem -LiteralPath $Package -File -Recurse)
    $inf = @($files | Where-Object Extension -eq '.inf')
    if ($inf.Count -ne 1) { throw "Research package must contain exactly one INF: $Package" }
    $inspection = Invoke-NativeCapture dotnet @($DrvCtlAssembly, 'inspect-inf', $inf[0].FullName)
    $plan = Invoke-NativeCapture dotnet @($DrvCtlAssembly, 'plan-driver', $Package)
    return [ordered]@{
        Name = $Name
        Directory = $Package
        Inf = $inf[0].Name
        FileCount = $files.Count
        TotalSize = [long](($files | Measure-Object Length -Sum).Sum)
        Inspection = ($inspection.StdOut -split "`r?`n" | Where-Object { $_ })
        Plan = ($plan.StdOut -split "`r?`n" | Where-Object { $_ })
    }
}

$Baseline = Resolve-SafePath $BaselineWim
$StudyRootResolved = Resolve-SafePath $StudyRoot
if (-not (Test-Path -LiteralPath $Baseline -PathType Leaf)) { throw "Baseline WIM not found: $Baseline" }
if (-not (Test-Path -LiteralPath $DrvCtlAssembly -PathType Leaf)) { throw "Managed drvctl assembly not found: $DrvCtlAssembly" }
foreach ($package in $KnownPackages.Values) { if (-not (Test-Path -LiteralPath $package -PathType Container)) { throw "Package not found: $package" } }
Assert-StudySafety $StudyRootResolved $Baseline $KnownPackages
$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'The research harness requires an elevated PowerShell process for DISM mount servicing.' }
if ((Test-Path -LiteralPath $StudyRootResolved) -and -not $Resume) { throw "Study root already exists: $StudyRootResolved. Use -Resume or choose a new directory." }
[IO.Directory]::CreateDirectory($StudyRootResolved) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $StudyRootResolved 'logs')) | Out-Null
$BaselineHash = (Get-FileHash -LiteralPath $Baseline -Algorithm SHA256).Hash

$packages = [ordered]@{}
foreach ($entry in $KnownPackages.GetEnumerator()) { $packages[$entry.Key] = Get-PackageInventory $entry.Key $entry.Value }
foreach ($name in $AdditionalPackageNames) {
    $path = Join-Path $DriversRoot $name
    if (Test-Path -LiteralPath $path -PathType Container) { $packages[$name] = Get-PackageInventory $name $path }
}
$fourth = $packages[$AdditionalPackageNames[0]]

$experiments = [Collections.Generic.List[object]]::new()
function Record-Experiment([string] $Name, [string] $Kind, [string] $Before, [string] $After, [string] $Package, [string] $Report) {
    $experiments.Add([ordered]@{ Name=$Name; Kind=$Kind; BeforeWim=$Before; AfterWim=$After; Package=$Package; Report=$Report })
}

try {
    $repeatA = New-ServicedFixture '01-repeat-acpivpc-a' $Baseline @(@{Action='Add'; Package=$KnownPackages.ACPIVPC})
    $repeatB = New-ServicedFixture '02-repeat-acpivpc-b' $Baseline @(@{Action='Add'; Package=$KnownPackages.ACPIVPC})
    Record-Experiment 'repeat-baseline-a' 'Repeatability' $Baseline $repeatA $KnownPackages.ACPIVPC (Invoke-PublicationAnalysis 'repeat-baseline-a' $Baseline $repeatA $KnownPackages.ACPIVPC)
    Record-Experiment 'repeat-baseline-b' 'Repeatability' $Baseline $repeatB $KnownPackages.ACPIVPC (Invoke-PublicationAnalysis 'repeat-baseline-b' $Baseline $repeatB $KnownPackages.ACPIVPC)
    Record-Experiment 'repeat-a-b' 'Repeatability' $repeatA $repeatB $KnownPackages.ACPIVPC (Invoke-PublicationAnalysis 'repeat-a-b' $repeatA $repeatB $KnownPackages.ACPIVPC)

    $once = New-ServicedFixture '03-duplicate-once' $Baseline @(@{Action='Add'; Package=$KnownPackages.ACPIVPC})
    $twice = New-ServicedFixture '04-duplicate-twice' $once @(@{Action='Add'; Package=$KnownPackages.ACPIVPC})
    Record-Experiment 'duplicate-once-twice' 'Duplicate' $once $twice $KnownPackages.ACPIVPC (Invoke-PublicationAnalysis 'duplicate-once-twice' $once $twice $KnownPackages.ACPIVPC)

    $forwardFirst = New-ServicedFixture '05-order-acpi-first' $Baseline @(@{Action='Add'; Package=$KnownPackages.ACPIVPC})
    $forwardFinal = New-ServicedFixture '06-order-acpi-then-lwi' $forwardFirst @(@{Action='Add'; Package=$KnownPackages.RzS4LWI})
    $reverseFirst = New-ServicedFixture '07-order-lwi-first' $Baseline @(@{Action='Add'; Package=$KnownPackages.RzS4LWI})
    $reverseFinal = New-ServicedFixture '08-order-lwi-then-acpi' $reverseFirst @(@{Action='Add'; Package=$KnownPackages.ACPIVPC})
    Record-Experiment 'order-forward-first' 'Order' $Baseline $forwardFirst $KnownPackages.ACPIVPC (Invoke-PublicationAnalysis 'order-forward-first' $Baseline $forwardFirst $KnownPackages.ACPIVPC)
    Record-Experiment 'order-forward-second' 'Order' $forwardFirst $forwardFinal $KnownPackages.RzS4LWI (Invoke-PublicationAnalysis 'order-forward-second' $forwardFirst $forwardFinal $KnownPackages.RzS4LWI)
    Record-Experiment 'order-reverse-first' 'Order' $Baseline $reverseFirst $KnownPackages.RzS4LWI (Invoke-PublicationAnalysis 'order-reverse-first' $Baseline $reverseFirst $KnownPackages.RzS4LWI)
    Record-Experiment 'order-reverse-second' 'Order' $reverseFirst $reverseFinal $KnownPackages.ACPIVPC (Invoke-PublicationAnalysis 'order-reverse-second' $reverseFirst $reverseFinal $KnownPackages.ACPIVPC)

    $pairForwardFirst = New-ServicedFixture '09-pair-ext-first' $Baseline @(@{Action='Add'; Package=$KnownPackages.RzS4Ext})
    $pairForwardFinal = New-ServicedFixture '10-pair-ext-then-lwi' $pairForwardFirst @(@{Action='Add'; Package=$KnownPackages.RzS4LWI})
    $pairReverseFirst = New-ServicedFixture '11-pair-lwi-first' $Baseline @(@{Action='Add'; Package=$KnownPackages.RzS4LWI})
    $pairReverseFinal = New-ServicedFixture '12-pair-lwi-then-ext' $pairReverseFirst @(@{Action='Add'; Package=$KnownPackages.RzS4Ext})
    Record-Experiment 'pair-forward-first' 'PairOrder' $Baseline $pairForwardFirst $KnownPackages.RzS4Ext (Invoke-PublicationAnalysis 'pair-forward-first' $Baseline $pairForwardFirst $KnownPackages.RzS4Ext)
    Record-Experiment 'pair-forward-second' 'PairOrder' $pairForwardFirst $pairForwardFinal $KnownPackages.RzS4LWI (Invoke-PublicationAnalysis 'pair-forward-second' $pairForwardFirst $pairForwardFinal $KnownPackages.RzS4LWI)
    Record-Experiment 'pair-reverse-first' 'PairOrder' $Baseline $pairReverseFirst $KnownPackages.RzS4LWI (Invoke-PublicationAnalysis 'pair-reverse-first' $Baseline $pairReverseFirst $KnownPackages.RzS4LWI)
    Record-Experiment 'pair-reverse-second' 'PairOrder' $pairReverseFirst $pairReverseFinal $KnownPackages.RzS4Ext (Invoke-PublicationAnalysis 'pair-reverse-second' $pairReverseFirst $pairReverseFinal $KnownPackages.RzS4Ext)

    $sessionForward = New-ServicedFixture '13-session-acpi-lwi-ext' $Baseline @(@{Action='Add'; Package=$KnownPackages.ACPIVPC}, @{Action='Add'; Package=$KnownPackages.RzS4LWI}, @{Action='Add'; Package=$KnownPackages.RzS4Ext})
    $sessionReverse = New-ServicedFixture '14-session-ext-lwi-acpi' $Baseline @(@{Action='Add'; Package=$KnownPackages.RzS4Ext}, @{Action='Add'; Package=$KnownPackages.RzS4LWI}, @{Action='Add'; Package=$KnownPackages.ACPIVPC})
    Record-Experiment 'session-forward-reverse' 'SameSession' $sessionForward $sessionReverse $KnownPackages.ACPIVPC (Invoke-PublicationAnalysis 'session-forward-reverse' $sessionForward $sessionReverse $KnownPackages.ACPIVPC)

    if ($null -ne $fourth) {
        $gapPopulated = New-ServicedFixture '15-gap-populated' $Baseline @(@{Action='Add'; Package=$KnownPackages.ACPIVPC}, @{Action='Add'; Package=$KnownPackages.RzS4LWI}, @{Action='Add'; Package=$KnownPackages.RzS4Ext})
        Record-Experiment 'gap-baseline-populated' 'GapPopulation' $Baseline $gapPopulated $KnownPackages.RzS4LWI (Invoke-PublicationAnalysis 'gap-baseline-populated' $Baseline $gapPopulated $KnownPackages.RzS4LWI)
        $gapRemoved = New-ServicedFixture '16-gap-removed-oem1' $gapPopulated @(@{Action='Remove'; PublishedInf='oem1.inf'})
        $gapRefilled = New-ServicedFixture '17-gap-add-fourth' $gapRemoved @(@{Action='Add'; Package=$fourth.Directory})
        Record-Experiment 'gap-populated-removed' 'GapRemoval' $gapPopulated $gapRemoved $KnownPackages.RzS4LWI (Invoke-PublicationAnalysis 'gap-populated-removed' $gapPopulated $gapRemoved $KnownPackages.RzS4LWI)
        Record-Experiment 'gap-removed-refilled' 'GapReuse' $gapRemoved $gapRefilled $fourth.Directory (Invoke-PublicationAnalysis 'gap-removed-refilled' $gapRemoved $gapRefilled $fourth.Directory)
    }

    $sampleNumber = 0
    foreach ($name in $AdditionalPackageNames) {
        if (-not $packages.Contains($name)) { continue }
        $sampleNumber++
        $sample = $packages[$name]
        $fixtureName = '18-sample-{0:D2}-{1}' -f $sampleNumber, (($name -split '\.')[0])
        $sampleWim = New-ServicedFixture $fixtureName $Baseline @(@{Action='Add'; Package=$sample.Directory})
        $reportName = 'sample-{0:D2}' -f $sampleNumber
        Record-Experiment $reportName 'DatabaseCorrelation' $Baseline $sampleWim $sample.Directory (Invoke-PublicationAnalysis $reportName $Baseline $sampleWim $sample.Directory)
    }

    $manifest = [ordered]@{
        Study = 'Task 6 controlled publication rule-discovery'
        StartedUtc = (Get-Date).ToUniversalTime().ToString('O')
        BaselineWim = $Baseline
        BaselineSha256Before = $BaselineHash
        BaselineSha256After = (Get-FileHash -LiteralPath $Baseline -Algorithm SHA256).Hash
        Environment = [ordered]@{ OSVersion=[Environment]::OSVersion.VersionString; Dism='dism.exe'; DrvCtlAssembly=$DrvCtlAssembly; ImageIndex=1 }
        Packages = @($packages.Values)
        Experiments = @($experiments)
    }
    $manifestPath = Join-Path $StudyRootResolved 'study-manifest.json'
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
    Invoke-Native pwsh.exe @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'Summarize-PublicationRuleStudy.ps1'), '-StudyRoot', $StudyRootResolved) (Join-Path $StudyRootResolved 'logs\summarize.log')
}
finally {
    Assert-BaselineUnchanged
}

Write-Host "Study complete: $(Join-Path $StudyRootResolved 'publication-rule-study.json')"
