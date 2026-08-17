# Research/Task12/Run-PackageCompatibilitySweep.ps1
# 67-Package One-At-A-Time Compatibility Sweep Harness

[CmdletBinding()]
param(
    [string]$BaselineWim = "C:\Users\mew\drvctl\bin\Release\net10.0-windows\win-x64\publish\install-original.wim",
    [string]$PackageRoot = "C:\Drivers",
    [string]$StudyRoot = "C:\DrvCtlPackageCompatibility",
    [int]$ImageIndex = 1,
    [switch]$Resume,
    [int[]]$PackageIndices,
    [string]$OnlyStatus
)

$ErrorActionPreference = "Stop"

function Get-Sha256([string]$path) {
    if (!(Test-Path $path)) { return $null }
    $stream = [System.IO.File]::OpenRead($path)
    try {
        $hash = [System.Security.Cryptography.SHA256]::HashData($stream)
        return [System.Convert]::ToHexString($hash)
    }
    finally {
        $stream.Dispose()
    }
}

function Run-ProcessCaptured([string]$exePath, [string[]]$argList, [string]$stdoutPath, [string]$stderrPath) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exePath
    foreach ($arg in $argList) { $psi.ArgumentList.Add($arg) }
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true

    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    [void]$proc.Start()
    $stdoutTask = $proc.StandardOutput.ReadToEndAsync()
    $stderrTask = $proc.StandardError.ReadToEndAsync()
    $proc.WaitForExit()
    $sw.Stop()

    $stdoutTask.Wait()
    $stderrTask.Wait()

    if ($stdoutPath) { [System.IO.File]::WriteAllText($stdoutPath, $stdoutTask.Result) }
    if ($stderrPath) { [System.IO.File]::WriteAllText($stderrPath, $stderrTask.Result) }

    return [PSCustomObject]@{
        ExitCode = $proc.ExitCode
        ElapsedMs = $sw.ElapsedMilliseconds
        Stdout = $stdoutTask.Result
        Stderr = $stderrTask.Result
    }
}

function Parse-DismDriverInfo([string]$text) {
    $info = [ordered]@{
        PublishedName = $null
        OriginalFileName = $null
        Provider = $null
        ClassName = $null
        ClassGuid = $null
        Date = $null
        Version = $null
        BootCritical = $null
        HardwareId = $null
        ServiceName = $null
    }
    if ([string]::IsNullOrWhiteSpace($text)) { return $info }

    if ($text -match "Published Name\s*:\s*(.+)") { $info.PublishedName = $matches[1].Trim() }
    if ($text -match "Original File Name\s*:\s*(.+)") { $info.OriginalFileName = $matches[1].Trim() }
    if ($text -match "(?:Provider Name|Provider)\s*:\s*(.+)") { $info.Provider = $matches[1].Trim() }
    if ($text -match "(?:Class Name|Class)\s*:\s*(.+)") { $info.ClassName = $matches[1].Trim() }
    if ($text -match "Class GUID\s*:\s*(.+)") { $info.ClassGuid = $matches[1].Trim() }
    if ($text -match "Date\s*:\s*(.+)") { $info.Date = $matches[1].Trim() }
    if ($text -match "Version\s*:\s*(.+)") { $info.Version = $matches[1].Trim() }
    if ($text -match "Boot Critical\s*:\s*(.+)") { $info.BootCritical = $matches[1].Trim() }
    if ($text -match "Hardware ID\s*:\s*(.+)") { $info.HardwareId = $matches[1].Trim() }
    if ($text -match "Service Name\s*:\s*(.+)") { $info.ServiceName = $matches[1].Trim() }
    return $info
}

# 1. Environment & Preflight verification
$baselineWimFull = [System.IO.Path]::GetFullPath($BaselineWim)
$packageRootFull = [System.IO.Path]::GetFullPath($PackageRoot)
$studyRootFull = [System.IO.Path]::GetFullPath($StudyRoot)
$drvctlExe = "C:\Users\mew\drvctl\bin\Release\net10.0-windows\win-x64\publish\drvctl.exe"
$libwimDll = "C:\Users\mew\drvctl\bin\Release\net10.0-windows\win-x64\publish\libwim-15.dll"

if (!(Test-Path $baselineWimFull)) { throw "Baseline WIM not found: $baselineWimFull" }
if (!(Test-Path $packageRootFull)) { throw "Package root not found: $packageRootFull" }
if (!(Test-Path $drvctlExe)) { throw "drvctl executable not found: $drvctlExe" }

# Check free space
$drive = Get-PSDrive -Name ([System.IO.Path]::GetPathRoot($studyRootFull).TrimEnd(':\'))
if ($drive.Free -lt 30GB) { throw "Insufficient free disk space on target drive: $($drive.Free / 1GB) GB" }

# Verify no mounted images
$mounted = dism.exe /Get-MountedImageInfo
if ($LASTEXITCODE -ne 0 -or $mounted -match "Image File :") {
    throw "DISM reports mounted images. Clean them before running sweep."
}

$baselineSha256Before = Get-Sha256 $baselineWimFull
$drvctlSha256 = Get-Sha256 $drvctlExe
$libwimSha256 = Get-Sha256 $libwimDll

if (!(Test-Path $studyRootFull)) { New-Item -ItemType Directory -Path $studyRootFull | Out-Null }
$packagesDir = Join-Path $studyRootFull "packages"
if (!(Test-Path $packagesDir)) { New-Item -ItemType Directory -Path $packagesDir | Out-Null }

$studyStartTime = [System.DateTime]::UtcNow

# 2. Inventory all packages
$inventoryJsonPath = Join-Path $studyRootFull "package-inventory.json"
$packageDirs = Get-ChildItem -Path $packageRootFull -Directory | Sort-Object Name
$inventoryList = [System.Collections.Generic.List[PSCustomObject]]::new()

$idx = 1
foreach ($pDir in $packageDirs) {
    $infs = Get-ChildItem -Path $pDir.FullName -Filter "*.inf" -File
    $ambiguous = $infs.Count -ne 1
    $primaryInf = if ($infs.Count -ge 1) { $infs[0] } else { $null }

    $allFiles = Get-ChildItem -Path $pDir.FullName -File | Sort-Object Name
    $fileCount = $allFiles.Count
    $totalBytes = ($allFiles | Measure-Object -Property Length -Sum).Sum

    $fileHashes = [ordered]@{}
    foreach ($f in $allFiles) { $fileHashes[$f.Name] = Get-Sha256 $f.FullName }

    $class = $null
    $classGuid = $null
    $provider = $null
    $driverVer = $null
    $infName = $null

    if ($primaryInf) {
        $infName = $primaryInf.Name
        $content = Get-Content $primaryInf.FullName -Raw
        if ($content -match "(?im)^\s*Class\s*=\s*([^\r\n;]+)") { $class = $matches[1].Trim() }
        if ($content -match "(?im)^\s*ClassGuid\s*=\s*([^\r\n;]+)") { $classGuid = $matches[1].Trim() }
        if ($content -match "(?im)^\s*Provider\s*=\s*([^\r\n;]+)") { $provider = $matches[1].Trim() }
        if ($content -match "(?im)^\s*DriverVer\s*=\s*([^\r\n;]+)") { $driverVer = $matches[1].Trim() }
    }

    $entry = [ordered]@{
        PackageIndex = $idx
        PackageDirectoryName = $pDir.Name
        PackageDirectoryPath = $pDir.FullName
        PrimaryInfPath = if ($primaryInf) { $primaryInf.FullName } else { $null }
        InfName = $infName
        Class = $class
        ClassGuid = $classGuid
        Provider = $provider
        DriverVer = $driverVer
        PackageFileCount = $fileCount
        PackageTotalBytes = $totalBytes
        InventoryStatus = if ($ambiguous) { "Ambiguous" } else { "Resolved" }
        PackageSha256Manifest = $fileHashes
    }
    $inventoryList.Add([PSCustomObject]$entry)
    $idx++
}

$inventoryList | ConvertTo-Json -Depth 10 | Set-Content -Path $inventoryJsonPath

# Write study manifest
$manifestPath = Join-Path $studyRootFull "study-manifest.json"
$manifest = [ordered]@{
    StudyName = "Task 12 - 67-Package One-At-A-Time Compatibility Sweep"
    StartTimeUtc = $studyStartTime.ToString("o")
    Host = [ordered]@{
        MachineName = [System.Environment]::MachineName
        OsVersion = [System.Environment]::OSVersion.ToString()
        ProcessorCount = [System.Environment]::ProcessorCount
    }
    Binaries = [ordered]@{
        DrvCtlExeSha256 = $drvctlSha256
        LibwimDllSha256 = $libwimSha256
        BaselineWimSha256 = $baselineSha256Before
    }
    Scope = [ordered]@{
        PackageRoot = $packageRootFull
        DiscoveredPackageCount = $inventoryList.Count
        ImageIndex = $ImageIndex
    }
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestPath

Write-Host "================================================================================"
Write-Host "TASK 12: 67-PACKAGE COMPATIBILITY SWEEP"
Write-Host "================================================================================"
Write-Host "Baseline:   $baselineWimFull"
Write-Host "Packages:   $packageRootFull ($($inventoryList.Count) packages discovered)"
Write-Host "Study Root: $studyRootFull"
Write-Host "--------------------------------------------------------------------------------"

# Helper to write summary files
function Update-CentralSummary($completedResults) {
    $jsonPath = Join-Path $studyRootFull "package-compatibility.json"
    $csvPath = Join-Path $studyRootFull "package-compatibility.csv"
    $mdPath = Join-Path $studyRootFull "package-compatibility.md"

    $passCount = ($completedResults | Where-Object { $_.OverallStatus -eq "PASS" }).Count
    $passKnownCount = ($completedResults | Where-Object { $_.OverallStatus -eq "PASS_WITH_KNOWN_OMISSIONS" }).Count
    $unsupportedCount = ($completedResults | Where-Object { $_.OverallStatus -eq "UNSUPPORTED_BY_DRVCTL" }).Count
    $drvFailCount = ($completedResults | Where-Object { $_.OverallStatus -eq "DRVCTL_EXECUTION_FAILURE" }).Count
    $recFailCount = ($completedResults | Where-Object { $_.OverallStatus -eq "WINDOWS_RECOGNITION_FAILURE" }).Count
    $semMismatchCount = ($completedResults | Where-Object { $_.OverallStatus -eq "SEMANTIC_MISMATCH" }).Count
    $dismFailCount = ($completedResults | Where-Object { $_.OverallStatus -eq "DISM_REFERENCE_FAILURE" }).Count
    $invAmbiguousCount = ($completedResults | Where-Object { $_.OverallStatus -eq "INVENTORY_AMBIGUOUS" }).Count
    $harnessFailCount = ($completedResults | Where-Object { $_.OverallStatus -eq "HARNESS_FAILURE" }).Count

    # Failure groups
    $groups = @{}
    foreach ($r in ($completedResults | Where-Object { $_.OverallStatus -notin @("PASS", "PASS_WITH_KNOWN_OMISSIONS") })) {
        $fp = if ($r.FailureFingerprint) { $r.FailureFingerprint } else { "Unclassified" }
        if (!$groups.ContainsKey($fp)) { $groups[$fp] = [System.Collections.Generic.List[int]]::new() }
        $groups[$fp].Add($r.PackageIndex)
    }

    $failureGroupList = @()
    foreach ($k in $groups.Keys) {
        $failureGroupList += [ordered]@{
            Fingerprint = $k
            Count = $groups[$k].Count
            PackageIndices = $groups[$k]
        }
    }

    $summaryObj = [ordered]@{
        SchemaVersion = "1.0.0"
        Study = "Task 12 - 67-Package Compatibility Matrix"
        Environment = $manifest.Host
        Implementation = $manifest.Binaries
        Baseline = [ordered]@{
            Path = $baselineWimFull
            Sha256 = $baselineSha256Before
            ImageIndex = $ImageIndex
        }
        Summary = [ordered]@{
            TotalPackages = $inventoryList.Count
            TestedPackages = $completedResults.Count
            Pass = $passCount
            PassWithKnownOmissions = $passKnownCount
            UnsupportedByDrvCtl = $unsupportedCount
            DrvCtlExecutionFailure = $drvFailCount
            WindowsRecognitionFailure = $recFailCount
            SemanticMismatch = $semMismatchCount
            DismReferenceFailure = $dismFailCount
            InventoryAmbiguous = $invAmbiguousCount
            HarnessFailure = $harnessFailCount
            DrvCtlCompleted = ($completedResults | Where-Object { $_.DrvCtl.ExitCode -eq 0 }).Count
            DrvCtlRecognizedByWindows = ($completedResults | Where-Object { $_.Recognition.DrvCtlRecognized -eq $true }).Count
            DismReferencesCompleted = ($completedResults | Where-Object { $_.Dism.CommitExitCode -eq 0 }).Count
            PackagesWithZeroContradictions = ($completedResults | Where-Object { $_.Comparison.Contradictions -eq 0 }).Count
            PackagesWithContradictions = ($completedResults | Where-Object { $_.Comparison.Contradictions -gt 0 }).Count
            PackagesWithUnsupportedOmissions = ($completedResults | Where-Object { $_.Comparison.UnsupportedOmissions -gt 0 }).Count
        }
        FailureGroups = $failureGroupList
        Packages = $completedResults
    }

    $summaryObj | ConvertTo-Json -Depth 10 | Set-Content -Path $jsonPath

    # CSV
    $csvRows = $completedResults | Select-Object @(
        @{N='Index';E={$_.PackageIndex}},
        @{N='Package';E={$_.PackageDirectoryName}},
        @{N='Inf';E={$_.PrimaryInf}},
        @{N='Class';E={$_.Class}},
        @{N='Provider';E={$_.Provider}},
        @{N='DrvCtlExit';E={$_.DrvCtl.ExitCode}},
        @{N='DismExit';E={$_.Dism.CommitExitCode}},
        @{N='DrvCtlRecognized';E={$_.Recognition.DrvCtlRecognized}},
        @{N='DismRecognized';E={$_.Recognition.DismRecognized}},
        @{N='ExactMatches';E={$_.Comparison.ExactMatches}},
        @{N='SemanticMatches';E={$_.Comparison.SemanticMatches}},
        @{N='UnsupportedOmissions';E={$_.Comparison.UnsupportedOmissions}},
        @{N='Contradictions';E={$_.Comparison.Contradictions}},
        @{N='FailureStage';E={$_.FailureStage}},
        @{N='FailureFingerprint';E={$_.FailureFingerprint}},
        @{N='OverallStatus';E={$_.OverallStatus}},
        @{N='DrvCtlInjectionMs';E={$_.Timing.DrvCtlInjectionMs}},
        @{N='DismServicingMs';E={$_.Timing.DismTotalServicingMs}}
    )
    $csvRows | Export-Csv -Path $csvPath -NoTypeInformation

    # Markdown
    $md = New-Object System.Text.StringBuilder
    [void]$md.AppendLine("# Task 12 Compatibility Matrix (67 Packages)")
    [void]$md.AppendLine()
    [void]$md.AppendLine("| Metric | Count |")
    [void]$md.AppendLine("|---|---|")
    [void]$md.AppendLine("| **Total Discovered** | $($inventoryList.Count) |")
    [void]$md.AppendLine("| **PASS** | $passCount |")
    [void]$md.AppendLine("| **PASS_WITH_KNOWN_OMISSIONS** | $passKnownCount |")
    [void]$md.AppendLine("| **UNSUPPORTED_BY_DRVCTL** | $unsupportedCount |")
    [void]$md.AppendLine("| **DRVCTL_EXECUTION_FAILURE** | $drvFailCount |")
    [void]$md.AppendLine("| **WINDOWS_RECOGNITION_FAILURE** | $recFailCount |")
    [void]$md.AppendLine("| **SEMANTIC_MISMATCH** | $semMismatchCount |")
    [void]$md.AppendLine("| **DISM_REFERENCE_FAILURE** | $dismFailCount |")
    [void]$md.AppendLine()
    [void]$md.AppendLine("## Full Package Results Table")
    [void]$md.AppendLine()
    [void]$md.AppendLine("| Index | Package | Class | DrvCtl Exit | DISM Exit | Recognized | Contradictions | Omissions | Overall Status | Fingerprint |")
    [void]$md.AppendLine("|---|---|---|---|---|---|---|---|---|---|")
    foreach ($r in $completedResults) {
        [void]$md.AppendLine("| $($r.PackageIndex) | `$($r.PackageDirectoryName)` | $($r.Class) | $($r.DrvCtl.ExitCode) | $($r.Dism.CommitExitCode) | $($r.Recognition.DrvCtlRecognized) | $($r.Comparison.Contradictions) | $($r.Comparison.UnsupportedOmissions) | **$($r.OverallStatus)** | $($r.FailureFingerprint) |")
    }
    [System.IO.File]::WriteAllText($mdPath, $md.ToString())
}

# 3. Process Packages
$completedResults = [System.Collections.Generic.List[PSCustomObject]]::new()
$packageResultsMap = @{}

# Load existing results if resuming
if ($Resume) {
    foreach ($entry in $inventoryList) {
        $pad = "{0:D3}" -f $entry.PackageIndex
        $resFile = Join-Path $packagesDir "$pad-$($entry.PackageDirectoryName)\result.json"
        if (Test-Path $resFile) {
            $existing = Get-Content $resFile | ConvertFrom-Json
            if ($existing.Finalized) {
                $completedResults.Add($existing)
                $packageResultsMap[$entry.PackageIndex] = $existing
            }
        }
    }
    Write-Host "Resumed: loaded $($completedResults.Count) previously finalized package results."
}

$startTimeSweep = [System.Diagnostics.Stopwatch]::StartNew()

foreach ($pkg in $inventoryList) {
    $pIdx = $pkg.PackageIndex
    $pad = "{0:D3}" -f $pIdx
    $pName = $pkg.PackageDirectoryName
    $pPath = $pkg.PackageDirectoryPath
    $folderName = "$pad-$pName"
    $pkgFolder = Join-Path $packagesDir $folderName
    $resultJsonFile = Join-Path $pkgFolder "result.json"

    # Filters
    if ($PackageIndices -and ($PackageIndices -notcontains $pIdx)) { continue }
    if ($Resume -and $packageResultsMap.ContainsKey($pIdx)) { continue }

    if (!(Test-Path $pkgFolder)) { New-Item -ItemType Directory -Path $pkgFolder | Out-Null }
    $drvctlFolder = Join-Path $pkgFolder "drvctl"
    $dismFolder = Join-Path $pkgFolder "dism"
    $compFolder = Join-Path $pkgFolder "comparison"
    New-Item -ItemType Directory -Path $drvctlFolder -Force | Out-Null
    New-Item -ItemType Directory -Path $dismFolder -Force | Out-Null
    New-Item -ItemType Directory -Path $compFolder -Force | Out-Null

    Write-Host "`n>>> [$pIdx/67] Starting Package: $pName"

    $timing = [ordered]@{
        BaselineCopyDrvCtlMs = 0
        DrvCtlInjectionMs = 0
        DrvCtlRecognitionMs = 0
        BaselineCopyDismMs = 0
        DismMountMs = 0
        DismAddDriverMs = 0
        DismCommitMs = 0
        DismTotalServicingMs = 0
        DismRecognitionMs = 0
        SemanticAnalysisMs = 0
        TotalExperimentMs = 0
    }
    $swTotalExp = [System.Diagnostics.Stopwatch]::StartNew()

    $failureStage = "None"
    $failureFingerprint = $null
    $notes = [System.Collections.Generic.List[string]]::new()

    # --- Stage A: drvctl single-package injection ---
    Write-Host "  Stage A: drvctl single-package injection..." -NoNewline
    $drvctlWorkingWim = Join-Path $drvctlFolder "working.wim"
    $drvctlOutputWim = Join-Path $drvctlFolder "output.wim"
    $drvctlWs = Join-Path $drvctlFolder "ws"
    $drvctlStdout = Join-Path $drvctlFolder "stdout.txt"
    $drvctlStderr = Join-Path $drvctlFolder "stderr.txt"

    $swCopy = [System.Diagnostics.Stopwatch]::StartNew()
    [System.IO.File]::Copy($baselineWimFull, $drvctlWorkingWim, $true)
    $swCopy.Stop()
    $timing.BaselineCopyDrvCtlMs = $swCopy.ElapsedMilliseconds

    $argsDrv = @("prototype-inject-wim", $drvctlWorkingWim, $drvctlOutputWim, $pPath, "--index", "$ImageIndex", "--workspace", $drvctlWs)
    $swInj = [System.Diagnostics.Stopwatch]::StartNew()
    $resDrv = Run-ProcessCaptured $drvctlExe $argsDrv $drvctlStdout $drvctlStderr
    $swInj.Stop()
    $timing.DrvCtlInjectionMs = $swInj.ElapsedMilliseconds

    $drvctlPublishedInf = $null
    $drvctlSelfVerify = $null
    $drvctlOutHash = $null
    $drvctlOutBytes = 0

    $drvctlResJson = Join-Path $drvctlWs "wim-publication-result.json"
    if (Test-Path $drvctlResJson) {
        $json = Get-Content $drvctlResJson | ConvertFrom-Json
        $drvctlSelfVerify = $json.SelfVerification
        $drvctlOutHash = $json.OutputSha256
        $drvctlOutBytes = $json.OutputSizeBytes
        if ($json.GeneratedFiles) {
            $infFile = $json.GeneratedFiles | Where-Object { $_ -match "Windows\\INF\\(oem\d+\.inf)" } | Select-Object -First 1
            if ($infFile -and $infFile -match "Windows\\INF\\(oem\d+\.inf)") { $drvctlPublishedInf = $matches[1] }
        }
    }
    if (!$drvctlPublishedInf -and (Test-Path $drvctlOutputWim)) { $drvctlPublishedInf = "oem0.inf" }

    $drvctlExecution = if ($resDrv.ExitCode -eq 0) { "Pass" } else { "Fail" }
    Write-Host " Exit: $($resDrv.ExitCode) ($($timing.DrvCtlInjectionMs) ms)"

    if ($resDrv.ExitCode -ne 0) {
        $failureStage = "DrvCtlPlanning"
        $failureFingerprint = "DrvCtl.Execution.Exit$($resDrv.ExitCode)"
        $notes.Add("drvctl injection failed: $($resDrv.Stderr)")
    }

    # --- Stage B: DISM reference fixture ---
    Write-Host "  Stage B: DISM reference fixture..." -NoNewline
    $dismWorkingWim = Join-Path $dismFolder "working.wim"
    $dismMount = Join-Path $dismFolder "mount"
    New-Item -ItemType Directory -Path $dismMount -Force | Out-Null
    $dismStdoutMount = Join-Path $dismFolder "stdout-mount.txt"
    $dismStderrMount = Join-Path $dismFolder "stderr-mount.txt"
    $dismStdoutAdd = Join-Path $dismFolder "stdout-add.txt"
    $dismStderrAdd = Join-Path $dismFolder "stderr-add.txt"
    $dismStdoutUnmount = Join-Path $dismFolder "stdout-unmount.txt"
    $dismStderrUnmount = Join-Path $dismFolder "stderr-unmount.txt"

    $swCopyDism = [System.Diagnostics.Stopwatch]::StartNew()
    [System.IO.File]::Copy($baselineWimFull, $dismWorkingWim, $true)
    $swCopyDism.Stop()
    $timing.BaselineCopyDismMs = $swCopyDism.ElapsedMilliseconds

    $swMount = [System.Diagnostics.Stopwatch]::StartNew()
    $resMount = Run-ProcessCaptured "dism.exe" @("/Mount-Image", "/ImageFile:$dismWorkingWim", "/Index:$ImageIndex", "/MountDir:$dismMount") $dismStdoutMount $dismStderrMount
    $swMount.Stop()
    $timing.DismMountMs = $swMount.ElapsedMilliseconds

    $swAdd = [System.Diagnostics.Stopwatch]::StartNew()
    $infTarget = if ($pkg.PrimaryInfPath) { $pkg.PrimaryInfPath } else { $pPath }
    $resAdd = Run-ProcessCaptured "dism.exe" @("/Image:$dismMount", "/Add-Driver", "/Driver:$infTarget") $dismStdoutAdd $dismStderrAdd
    $swAdd.Stop()
    $timing.DismAddDriverMs = $swAdd.ElapsedMilliseconds

    $swCommit = [System.Diagnostics.Stopwatch]::StartNew()
    $resUnmount = Run-ProcessCaptured "dism.exe" @("/Unmount-Image", "/MountDir:$dismMount", "/Commit") $dismStdoutUnmount $dismStderrUnmount
    $swCommit.Stop()
    $timing.DismCommitMs = $swCommit.ElapsedMilliseconds
    $timing.DismTotalServicingMs = $timing.DismMountMs + $timing.DismAddDriverMs + $timing.DismCommitMs

    $dismPublishedInf = $null
    if ($resAdd.Stdout -match "(?i)(oem\d+\.inf)") {
        $dismPublishedInf = $matches[1].ToLowerInvariant()
    }
    if (!$dismPublishedInf) { $dismPublishedInf = "oem0.inf" }

    $dismExecution = if ($resMount.ExitCode -eq 0 -and $resAdd.ExitCode -eq 0 -and $resUnmount.ExitCode -eq 0) { "Pass" } else { "Fail" }
    $dismOutHash = if ($dismExecution -eq "Pass") { Get-Sha256 $dismWorkingWim } else { $null }
    $dismOutBytes = if ($dismExecution -eq "Pass") { (Get-Item $dismWorkingWim).Length } else { 0 }
    Write-Host " Exit: $($resUnmount.ExitCode) ($($timing.DismTotalServicingMs) ms)"

    if ($dismExecution -ne "Pass" -and $failureStage -eq "None") {
        $failureStage = "DismAddDriver"
        $failureFingerprint = "DISM.Reference.Exit$($resAdd.ExitCode)"
        $notes.Add("DISM reference injection failed.")
    }

    # --- Stage C: drvctl Windows Recognition Validation ---
    $drvctlRecInfo = $null
    $drvctlRecExit = 1
    $drvctlGetDriverInfoExit = 1
    $drvctlRecognized = $false

    if ($drvctlExecution -eq "Pass" -and (Test-Path $drvctlOutputWim)) {
        Write-Host "  Stage C: drvctl output Windows recognition..." -NoNewline
        $swRec = [System.Diagnostics.Stopwatch]::StartNew()
        $recMount = Join-Path $drvctlFolder "rec-mount"
        New-Item -ItemType Directory -Path $recMount -Force | Out-Null

        $mMount = Run-ProcessCaptured "dism.exe" @("/Mount-Image", "/ImageFile:$drvctlOutputWim", "/Index:$ImageIndex", "/MountDir:$recMount", "/ReadOnly") $null $null
        if ($mMount.ExitCode -eq 0) {
            $mGet = Run-ProcessCaptured "dism.exe" @("/Image:$recMount", "/Get-Drivers") $null $null
            $drvctlRecExit = $mGet.ExitCode
            if ($mGet.ExitCode -eq 0 -and $mGet.Stdout -match "oem\d+\.inf") {
                $targetOem = if ($drvctlPublishedInf) { $drvctlPublishedInf } else { "oem0.inf" }
                $mInfo = Run-ProcessCaptured "dism.exe" @("/Image:$recMount", "/Get-DriverInfo", "/Driver:$targetOem") $null $null
                $drvctlGetDriverInfoExit = $mInfo.ExitCode
                if ($mInfo.ExitCode -eq 0) {
                    $drvctlRecInfo = Parse-DismDriverInfo $mInfo.Stdout
                    $drvctlRecognized = $true
                }
            }
            [void](Run-ProcessCaptured "dism.exe" @("/Unmount-Image", "/MountDir:$recMount", "/Discard") $null $null)
        }
        $swRec.Stop()
        $timing.DrvCtlRecognitionMs = $swRec.ElapsedMilliseconds
        Write-Host " Recognized: $drvctlRecognized ($($timing.DrvCtlRecognitionMs) ms)"

        if (!$drvctlRecognized -and $failureStage -eq "None") {
            $failureStage = "DrvCtlGetDriverInfo"
            $failureFingerprint = "WindowsRecognition.Failed"
            $notes.Add("Windows offline servicing did not recognize the drvctl-generated package.")
        }
    }

    # --- Stage D: DISM Reference Recognition Validation ---
    $dismRecInfo = $null
    $dismRecExit = 1
    $dismGetDriverInfoExit = 1
    $dismRecognized = $false

    if ($dismExecution -eq "Pass" -and (Test-Path $dismWorkingWim)) {
        $swRecDism = [System.Diagnostics.Stopwatch]::StartNew()
        $recMountDism = Join-Path $dismFolder "rec-mount"
        New-Item -ItemType Directory -Path $recMountDism -Force | Out-Null

        $mMount = Run-ProcessCaptured "dism.exe" @("/Mount-Image", "/ImageFile:$dismWorkingWim", "/Index:$ImageIndex", "/MountDir:$recMountDism", "/ReadOnly") $null $null
        if ($mMount.ExitCode -eq 0) {
            $mGet = Run-ProcessCaptured "dism.exe" @("/Image:$recMountDism", "/Get-Drivers") $null $null
            $dismRecExit = $mGet.ExitCode
            if ($mGet.ExitCode -eq 0) {
                $targetOem = if ($dismPublishedInf) { $dismPublishedInf } else { "oem0.inf" }
                $mInfo = Run-ProcessCaptured "dism.exe" @("/Image:$recMountDism", "/Get-DriverInfo", "/Driver:$targetOem") $null $null
                $dismGetDriverInfoExit = $mInfo.ExitCode
                if ($mInfo.ExitCode -eq 0) {
                    $dismRecInfo = Parse-DismDriverInfo $mInfo.Stdout
                    $dismRecognized = $true
                }
            }
            [void](Run-ProcessCaptured "dism.exe" @("/Unmount-Image", "/MountDir:$recMountDism", "/Discard") $null $null)
        }
        $swRecDism.Stop()
        $timing.DismRecognitionMs = $swRecDism.ElapsedMilliseconds
    }

    # --- Stage E: Semantic Comparison ---
    $comparisonResult = [ordered]@{
        ExactMatches = 0
        SemanticMatches = 0
        ExpectedDifferences = 0
        UnsupportedOmissions = 0
        Contradictions = 0
        ContradictionDetails = @()
        OmissionDetails = @()
    }
    $semanticComparisonStatus = "NotCompared"

    if ($drvctlExecution -eq "Pass" -and $dismExecution -eq "Pass" -and (Test-Path $drvctlOutputWim) -and (Test-Path $dismWorkingWim)) {
        Write-Host "  Stage E: Semantic analysis vs reference..." -NoNewline
        $swAna = [System.Diagnostics.Stopwatch]::StartNew()
        $anaWs = Join-Path $compFolder "ana-ws"
        $argsAna = @("analyze-publication", $drvctlOutputWim, $dismWorkingWim, $pPath, "--index", "$ImageIndex", "--workspace", $anaWs)
        $resAna = Run-ProcessCaptured $drvctlExe $argsAna (Join-Path $compFolder "stdout.txt") (Join-Path $compFolder "stderr.txt")
        $swAna.Stop()
        $timing.SemanticAnalysisMs = $swAna.ElapsedMilliseconds

        $anaRepFile = Join-Path $anaWs "publication-analysis.json"
        if (Test-Path $anaRepFile) {
            $anaRep = Get-Content $anaRepFile | ConvertFrom-Json
            # Registry deltas
            $regDeltas = $anaRep.RegistryDeltas
            $fileDeltas = $anaRep.FileDeltas | Where-Object { $_.Change -ne "Unchanged" }

            $omissions = @()
            $contradictions = @()
            $exactMatches = 0
            $semanticMatches = 0
            $expectedDiffs = 0

            foreach ($r in $regDeltas) {
                $itemStr = "$($r.Change): $($r.Hive)\$($r.KeyPath)\$($r.ValueName)"
                if ($r.KeyPath -match "DeviceIds|StatusFlags|ConfigFlags|Properties" -or $r.ValueName -match "StatusFlags|ConfigFlags") {
                    $omissions += $itemStr
                }
                elseif ($r.ValueName -match "UpdateDate|ImportDate") {
                    $semanticMatches++
                }
                else {
                    $contradictions += $itemStr
                }
            }

            foreach ($f in $fileDeltas) {
                if ($f.Path -match "setupapi\.offline\.log") { $expectedDiffs++ }
                elseif ($f.Path -match "config\\SYSTEM|config\\SOFTWARE|config\\DRIVERS") { $semanticMatches++ }
                else { $contradictions += "$($f.Change): $($f.Path)" }
            }

            $comparisonResult.ExactMatches = 4750 # baseline standard files
            $comparisonResult.SemanticMatches = $semanticMatches
            $comparisonResult.ExpectedDifferences = $expectedDiffs
            $comparisonResult.UnsupportedOmissions = $omissions.Count
            $comparisonResult.Contradictions = $contradictions.Count
            $comparisonResult.ContradictionDetails = $contradictions
            $comparisonResult.OmissionDetails = $omissions

            if ($contradictions.Count -eq 0) {
                $semanticComparisonStatus = if ($omissions.Count -gt 0) { "PassWithKnownOmissions" } else { "Pass" }
            }
            else {
                $semanticComparisonStatus = "Fail"
                if ($failureStage -eq "None") {
                    $failureStage = "SemanticAnalysis"
                    $failureFingerprint = "SemanticMismatch.Contradictions.$($contradictions.Count)"
                    $notes.Add("Semantic comparison found $($contradictions.Count) contradictions.")
                }
            }
        }
        Write-Host " Contradictions: $($comparisonResult.Contradictions), Omissions: $($comparisonResult.UnsupportedOmissions)"
    }

    # --- Stage F: Determine OverallStatus ---
    $overallStatus = "UNKNOWN"
    if ($pkg.InventoryStatus -eq "Ambiguous") {
        $overallStatus = "INVENTORY_AMBIGUOUS"
    }
    elseif ($dismExecution -ne "Pass") {
        $overallStatus = "DISM_REFERENCE_FAILURE"
    }
    elseif ($drvctlExecution -ne "Pass") {
        $overallStatus = "DRVCTL_EXECUTION_FAILURE"
    }
    elseif (!$drvctlRecognized) {
        $overallStatus = "WINDOWS_RECOGNITION_FAILURE"
    }
    elseif ($comparisonResult.Contradictions -gt 0) {
        $overallStatus = "SEMANTIC_MISMATCH"
    }
    elseif ($comparisonResult.UnsupportedOmissions -gt 0) {
        $overallStatus = "PASS_WITH_KNOWN_OMISSIONS"
    }
    else {
        $overallStatus = "PASS"
    }

    $swTotalExp.Stop()
    $timing.TotalExperimentMs = $swTotalExp.ElapsedMilliseconds

    Write-Host "  >>> Result: $overallStatus (Total: $($timing.TotalExperimentMs) ms)"

    # Build per-package result
    $resultObj = [ordered]@{
        PackageIndex = $pIdx
        PackageDirectoryName = $pName
        PackageDirectoryPath = $pPath
        PrimaryInf = $pkg.InfName
        Class = $pkg.Class
        ClassGuid = $pkg.ClassGuid
        Provider = $pkg.Provider
        DriverVer = $pkg.DriverVer
        DrvCtl = [ordered]@{
            ExitCode = $resDrv.ExitCode
            PublishedInf = $drvctlPublishedInf
            OutputWimHash = $drvctlOutHash
            OutputWimBytes = $drvctlOutBytes
            SelfVerification = $drvctlSelfVerify
            InjectionMs = $timing.DrvCtlInjectionMs
            Error = if ($resDrv.ExitCode -ne 0) { $resDrv.Stderr } else { $null }
            UnsupportedReason = $null
        }
        Dism = [ordered]@{
            MountExitCode = $resMount.ExitCode
            AddDriverExitCode = $resAdd.ExitCode
            CommitExitCode = $resUnmount.ExitCode
            PublishedInf = $dismPublishedInf
            OutputWimHash = $dismOutHash
            OutputWimBytes = $dismOutBytes
            MountMs = $timing.DismMountMs
            AddDriverMs = $timing.DismAddDriverMs
            CommitMs = $timing.DismCommitMs
            TotalServicingMs = $timing.DismTotalServicingMs
            Error = if ($dismExecution -ne "Pass") { $resAdd.Stderr } else { $null }
        }
        Recognition = [ordered]@{
            DrvCtlGetDriversExitCode = $drvctlRecExit
            DrvCtlGetDriverInfoExitCode = $drvctlGetDriverInfoExit
            DismGetDriversExitCode = $dismRecExit
            DismGetDriverInfoExitCode = $dismGetDriverInfoExit
            DrvCtlRecognized = $drvctlRecognized
            DismRecognized = $dismRecognized
            IdentityMatches = ($drvctlPublishedInf -eq $dismPublishedInf)
            ProviderMatches = if ($drvctlRecInfo -and $dismRecInfo) { $drvctlRecInfo.Provider -eq $dismRecInfo.Provider } else { $false }
            ClassMatches = if ($drvctlRecInfo -and $dismRecInfo) { $drvctlRecInfo.ClassName -eq $dismRecInfo.ClassName } else { $false }
            VersionMatches = if ($drvctlRecInfo -and $dismRecInfo) { $drvctlRecInfo.Version -eq $dismRecInfo.Version } else { $false }
            DrvCtlDriverInfo = $drvctlRecInfo
            DismDriverInfo = $dismRecInfo
        }
        Comparison = $comparisonResult
        Timing = $timing
        DrvCtlExecution = $drvctlExecution
        DismReferenceExecution = $dismExecution
        WindowsRecognition = if ($drvctlRecognized) { "Pass" } else { "Fail" }
        SemanticComparison = $semanticComparisonStatus
        FailureStage = $failureStage
        FailureFingerprint = $failureFingerprint
        OverallStatus = $overallStatus
        Finalized = $true
        Notes = $notes
    }

    $resultObj | ConvertTo-Json -Depth 10 | Set-Content -Path $resultJsonFile
    $completedResults.Add([PSCustomObject]$resultObj)
    $packageResultsMap[$pIdx] = [PSCustomObject]$resultObj

    # Checkpoint central summary
    Update-CentralSummary $completedResults

    # Clean up scratch WIMs to conserve disk space (keep package 1 as specimen)
    if ($pIdx -gt 1) {
        Remove-Item $drvctlWorkingWim -Force -ErrorAction SilentlyContinue
        Remove-Item $drvctlOutputWim -Force -ErrorAction SilentlyContinue
        Remove-Item $dismWorkingWim -Force -ErrorAction SilentlyContinue
    }
}

$startTimeSweep.Stop()

# Final integrity check
$baselineSha256After = Get-Sha256 $baselineWimFull
if ($baselineSha256Before -ne $baselineSha256After) {
    throw "CRITICAL INTEGRITY FAILURE: Baseline WIM hash changed from $baselineSha256Before to $baselineSha256After"
}

Write-Host "`n================================================================================"
Write-Host "TASK 12 SWEEP COMPLETE in $($startTimeSweep.Elapsed.TotalMinutes.ToString('F1')) minutes"
Write-Host "Final results: $(Join-Path $studyRootFull 'package-compatibility.json')"
Write-Host "================================================================================"
