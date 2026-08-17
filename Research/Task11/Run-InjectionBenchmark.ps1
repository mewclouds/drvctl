# Research/Task11/Run-InjectionBenchmark.ps1
# Full C:\Drivers (67-package) Throughput Benchmark: drvctl vs DISM /Recurse
# PERFORMANCE-ONLY EXPERIMENT. OUTPUT CORRECTNESS NOT VALIDATED.

[CmdletBinding()]
param(
    [string]$BaselineWim = "C:\Users\mew\drvctl\bin\Release\net10.0-windows\win-x64\publish\install-original.wim",
    [string]$DriversRoot = "C:\Drivers",
    [string]$OutputRoot = "C:\DrvCtlTask11Benchmark",
    [int]$ImageIndex = 1,
    [int]$Warmups = 1,
    [int]$Pairs = 3
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

    [System.IO.File]::WriteAllText($stdoutPath, $stdoutTask.Result)
    [System.IO.File]::WriteAllText($stderrPath, $stderrTask.Result)

    return [PSCustomObject]@{
        ExitCode = $proc.ExitCode
        ElapsedMs = $sw.ElapsedMilliseconds
        Stdout = $stdoutTask.Result
        Stderr = $stderrTask.Result
    }
}

# 1. Environment & Preflight verification
$baselineWimFull = [System.IO.Path]::GetFullPath($BaselineWim)
$driversRootFull = [System.IO.Path]::GetFullPath($DriversRoot)
$drvctlExe = "C:\Users\mew\drvctl\bin\Release\net10.0-windows\win-x64\publish\drvctl.exe"
$libwimDll = "C:\Users\mew\drvctl\bin\Release\net10.0-windows\win-x64\publish\libwim-15.dll"

if (!(Test-Path $baselineWimFull)) { throw "Baseline WIM not found: $baselineWimFull" }
if (!(Test-Path $driversRootFull)) { throw "Drivers directory not found: $driversRootFull" }
if (!(Test-Path $drvctlExe)) { throw "drvctl executable not found: $drvctlExe" }

$packageDirs = Get-ChildItem -Path $driversRootFull -Directory | Where-Object { (Get-ChildItem -Path $_.FullName -Filter "*.inf").Count -gt 0 }
$totalPackages = $packageDirs.Count
if ($totalPackages -eq 0) { throw "No driver packages found in $driversRootFull" }

# Check free space
$drive = Get-PSDrive -Name ([System.IO.Path]::GetPathRoot($OutputRoot).TrimEnd(':\'))
if ($drive.Free -lt 30GB) { throw "Insufficient free disk space on target drive: $($drive.Free / 1GB) GB" }

# Verify no mounted images
$mounted = dism.exe /Get-MountedImageInfo
if ($LASTEXITCODE -ne 0 -or $mounted -match "Image File :") {
    throw "DISM reports mounted images. Clean them before running benchmark."
}

$baselineSha256Before = Get-Sha256 $baselineWimFull
$drvctlSha256 = Get-Sha256 $drvctlExe
$libwimSha256 = Get-Sha256 $libwimDll

if (!(Test-Path $OutputRoot)) { New-Item -ItemType Directory -Path $OutputRoot | Out-Null }
$runsDir = Join-Path $OutputRoot "runs"
if (Test-Path $runsDir) { Remove-Item $runsDir -Recurse -Force }
New-Item -ItemType Directory -Path $runsDir | Out-Null

$allRuns = [System.Collections.Generic.List[PSCustomObject]]::new()
$startTime = [System.DateTime]::UtcNow

Write-Host "================================================================================"
Write-Host "TASK 11: FULL C:\DRIVERS (67-PACKAGE) THROUGHPUT BENCHMARK"
Write-Host "PERFORMANCE-ONLY EXPERIMENT. OUTPUT CORRECTNESS NOT VALIDATED."
Write-Host "================================================================================"
Write-Host "Baseline WIM:       $baselineWimFull"
Write-Host "Baseline SHA:       $baselineSha256Before"
Write-Host "Drivers Root:       $driversRootFull ($totalPackages packages)"
Write-Host "Output Root:        $OutputRoot"
Write-Host "Warmups:            $Warmups per implementation"
Write-Host "Measured Pairs:     $Pairs (Alternating execution)"
Write-Host "--------------------------------------------------------------------------------"

# Execution helper for drvctl
function Run-DrvCtlTest([int]$pair, [int]$order, [int]$runIndex, [bool]$isWarmup) {
    $runType = if ($isWarmup) { "Warmup" } else { "Measured" }
    $runName = "drvctl-pair$pair-run$runIndex"
    $workDir = Join-Path $runsDir $runName
    New-Item -ItemType Directory -Path $workDir | Out-Null

    $outputWim = Join-Path $workDir "drvctl-output.wim"
    $ws = Join-Path $workDir "ws"
    $stdoutFile = Join-Path $workDir "stdout.txt"
    $stderrFile = Join-Path $workDir "stderr.txt"

    Write-Host "[$runType] Pair $pair, Order $($order): drvctl direct WIM 67-package publication..." -NoNewline

    $args = @(
        "prototype-inject-wim",
        $baselineWimFull,
        $outputWim,
        $driversRootFull,
        "--index", "$ImageIndex",
        "--workspace", $ws,
        "--skip-self-verification"
    )

    $res = Run-ProcessCaptured $drvctlExe $args $stdoutFile $stderrFile

    $resultJson = Join-Path $ws "wim-publication-result.json"
    $valid = $false
    $copyMs = 0
    $mutationMs = 0
    $attempted = $totalPackages
    $processed = 0

    if ($res.ExitCode -eq 0 -and (Test-Path $resultJson)) {
        $json = Get-Content $resultJson | ConvertFrom-Json
        $valid = $json.WimMutationSuccess
        $copyMs = $json.Timings.BaselineCopyMs
        $mutationMs = $json.Timings.PlanMs + $json.Timings.HiveExtractMs + $json.Timings.HiveMutationMs + $json.Timings.WimUpdatePreparationMs + $json.Timings.WimWriteMs
        $attempted = $json.AttemptedPackagesCount
        $processed = $json.ProcessedPackagesCount
    }

    $endToEndMs = $copyMs + $mutationMs
    $outBytes = if (Test-Path $outputWim) { (Get-Item $outputWim).Length } else { 0 }
    $outHash = if ($valid) { Get-Sha256 $outputWim } else { $null }

    Write-Host " Done. Copy: ${copyMs}ms | Mutation: ${mutationMs}ms | E2E: ${endToEndMs}ms | Pkgs: $processed/$attempted | Exit: $($res.ExitCode)"

    return [PSCustomObject]@{
        Implementation = "drvctl"
        Pair = $pair
        Order = $order
        Run = $runIndex
        IsWarmup = $isWarmup
        PackagesAttempted = $attempted
        PackagesProcessed = $processed
        BaselineCopyMs = $copyMs
        MutationOrServicingMs = $mutationMs
        EndToEndMs = $endToEndMs
        TotalCommandMs = if ($json) { $json.Timings.TotalCommandMs } else { $res.ElapsedMs }
        ExitCode = $res.ExitCode
        Valid = ($res.ExitCode -eq 0 -and $processed -eq $attempted)
        OutputBytes = $outBytes
        OutputSha256 = $outHash
        WorkDir = $workDir
    }
}

# Execution helper for DISM
function Run-DismTest([int]$pair, [int]$order, [int]$runIndex, [bool]$isWarmup) {
    $runType = if ($isWarmup) { "Warmup" } else { "Measured" }
    $runName = "dism-pair$pair-run$runIndex"
    $workDir = Join-Path $runsDir $runName
    New-Item -ItemType Directory -Path $workDir | Out-Null

    $workingWim = Join-Path $workDir "dism-working.wim"
    $mountDir = Join-Path $workDir "mount"
    New-Item -ItemType Directory -Path $mountDir | Out-Null
    $stdoutMount = Join-Path $workDir "stdout-mount.txt"
    $stderrMount = Join-Path $workDir "stderr-mount.txt"
    $stdoutAdd = Join-Path $workDir "stdout-add.txt"
    $stderrAdd = Join-Path $workDir "stderr-add.txt"
    $stdoutUnmount = Join-Path $workDir "stdout-unmount.txt"
    $stderrUnmount = Join-Path $workDir "stderr-unmount.txt"

    Write-Host "[$runType] Pair $pair, Order $($order): DISM /Add-Driver /Driver:C:\Drivers /Recurse..." -NoNewline

    # 1. Baseline copy
    $swCopy = [System.Diagnostics.Stopwatch]::StartNew()
    [System.IO.File]::Copy($baselineWimFull, $workingWim, $true)
    $swCopy.Stop()
    $copyMs = $swCopy.ElapsedMilliseconds

    # 2. DISM servicing (mount + add-driver recurse + commit unmount)
    $swServicing = [System.Diagnostics.Stopwatch]::StartNew()

    $resMount = Run-ProcessCaptured "dism.exe" @("/Mount-Image", "/ImageFile:$workingWim", "/Index:$ImageIndex", "/MountDir:$mountDir") $stdoutMount $stderrMount
    $resAdd = Run-ProcessCaptured "dism.exe" @("/Image:$mountDir", "/Add-Driver", "/Driver:$driversRootFull", "/Recurse") $stdoutAdd $stderrAdd
    $resUnmount = Run-ProcessCaptured "dism.exe" @("/Unmount-Image", "/MountDir:$mountDir", "/Commit") $stdoutUnmount $stderrUnmount

    $swServicing.Stop()
    $servicingMs = $swServicing.ElapsedMilliseconds
    $endToEndMs = $copyMs + $servicingMs

    # Extract number of installed packages from DISM output
    $installedCount = 0
    if ($resAdd.Stdout) {
        $matches = [regex]::Matches($resAdd.Stdout, "Installing \d+ of \d+ -")
        $installedCount = $matches.Count
    }
    if ($installedCount -eq 0 -and $resAdd.ExitCode -eq 0) { $installedCount = $totalPackages }

    $completed = ($resMount.ExitCode -eq 0) -and ($resAdd.ExitCode -eq 0) -and ($resUnmount.ExitCode -eq 0)
    $outBytes = if (Test-Path $workingWim) { (Get-Item $workingWim).Length } else { 0 }
    $outHash = if ($completed) { Get-Sha256 $workingWim } else { $null }

    Write-Host " Done. Copy: ${copyMs}ms | Servicing: ${servicingMs}ms | E2E: ${endToEndMs}ms | Pkgs: $installedCount/$totalPackages | Exit: $($resUnmount.ExitCode)"

    return [PSCustomObject]@{
        Implementation = "DISM"
        Pair = $pair
        Order = $order
        Run = $runIndex
        IsWarmup = $isWarmup
        PackagesAttempted = $totalPackages
        PackagesProcessed = $installedCount
        BaselineCopyMs = $copyMs
        MutationOrServicingMs = $servicingMs
        EndToEndMs = $endToEndMs
        TotalCommandMs = $endToEndMs
        ExitCode = if ($completed) { 0 } else { 1 }
        Valid = $completed
        OutputBytes = $outBytes
        OutputSha256 = $outHash
        WorkDir = $workDir
    }
}

# --- Warm-up runs ---
Write-Host "`n>>> Running Warm-up Runs (1 per implementation)..."
$drvWarm = Run-DrvCtlTest 0 1 1 $true
$dismWarm = Run-DismTest 0 2 1 $true
$allRuns.Add($drvWarm)
$allRuns.Add($dismWarm)

# Clean warmup WIMs to conserve disk space
Get-ChildItem -Path $drvWarm.WorkDir -Filter "*.wim" | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $dismWarm.WorkDir -Filter "*.wim" | Remove-Item -Force -ErrorAction SilentlyContinue

# --- Measured runs (paired alternating order) ---
Write-Host "`n>>> Running 3 Measured Alternating Pairs..."
for ($p = 1; $p -le $Pairs; $p++) {
    Write-Host "`n--- Measured Pair $p of $Pairs ---"
    if ($p % 2 -eq 1) {
        # Odd pairs: drvctl first, DISM second
        $r1 = Run-DrvCtlTest $p 1 $p $false
        $r2 = Run-DismTest $p 2 $p $false
        $allRuns.Add($r1)
        $allRuns.Add($r2)
    }
    else {
        # Even pairs: DISM first, drvctl second
        $r1 = Run-DismTest $p 1 $p $false
        $r2 = Run-DrvCtlTest $p 2 $p $false
        $allRuns.Add($r1)
        $allRuns.Add($r2)
    }

    # Keep Pair 1 outputs, delete subsequent WIMs to conserve disk space
    if ($p -gt 1) {
        $prevPair = $p - 1
        foreach ($item in ($allRuns | Where-Object { $_.Pair -eq $prevPair -and $_.Pair -ne 1 })) {
            $wims = Get-ChildItem -Path $item.WorkDir -Filter "*.wim" -File -ErrorAction SilentlyContinue
            foreach ($w in $wims) { Remove-Item $w.FullName -Force -ErrorAction SilentlyContinue }
        }
    }
}

# Verify baseline invariant
$baselineSha256After = Get-Sha256 $baselineWimFull
if ($baselineSha256Before -ne $baselineSha256After) {
    throw "CRITICAL INTEGRITY FAILURE: Baseline WIM hash changed from $baselineSha256Before to $baselineSha256After"
}

# --- Calculate Statistics ---
$measuredDrv = $allRuns | Where-Object { $_.Implementation -eq "drvctl" -and -not $_.IsWarmup }
$measuredDism = $allRuns | Where-Object { $_.Implementation -eq "DISM" -and -not $_.IsWarmup }

function Get-Stats($list, [string]$prop) {
    $vals = $list | ForEach-Object { [double]$_.$prop } | Sort-Object
    $count = $vals.Count
    $min = $vals[0]
    $max = $vals[$count - 1]
    $mean = ($vals | Measure-Object -Average).Average
    $median = if ($count % 2 -eq 1) { $vals[[math]::Floor($count / 2)] } else { ($vals[$count / 2 - 1] + $vals[$count / 2]) / 2.0 }
    return [PSCustomObject]@{
        Count = $count
        Min = [math]::Round($min, 1)
        Max = [math]::Round($max, 1)
        Mean = [math]::Round($mean, 1)
        Median = [math]::Round($median, 1)
        Values = $vals
    }
}

$drvMutationStats = Get-Stats $measuredDrv "MutationOrServicingMs"
$drvE2EStats = Get-Stats $measuredDrv "EndToEndMs"
$dismServicingStats = Get-Stats $measuredDism "MutationOrServicingMs"
$dismE2EStats = Get-Stats $measuredDism "EndToEndMs"

$allCompleted = ($measuredDrv | Where-Object { $_.ExitCode -ne 0 }).Count -eq 0 -and ($measuredDism | Where-Object { $_.ExitCode -ne 0 }).Count -eq 0

$mutationSpeedup = if ($allCompleted) { [math]::Round($dismServicingStats.Median / $drvMutationStats.Median, 2) } else { $null }
$mutationReductionPct = if ($allCompleted) { [math]::Round((1.0 - ($drvMutationStats.Median / $dismServicingStats.Median)) * 100.0, 1) } else { $null }
$e2eSpeedup = if ($allCompleted) { [math]::Round($dismE2EStats.Median / $drvE2EStats.Median, 2) } else { $null }
$e2eReductionPct = if ($allCompleted) { [math]::Round((1.0 - ($drvE2EStats.Median / $dismE2EStats.Median)) * 100.0, 1) } else { $null }

# Emit CSV
$csvPath = Join-Path $OutputRoot "benchmark-runs.csv"
$csvRows = $allRuns | Select-Object Implementation, Pair, Order, Run, IsWarmup, PackagesAttempted, PackagesProcessed, BaselineCopyMs, MutationOrServicingMs, EndToEndMs, TotalCommandMs, ExitCode, Valid, OutputBytes, OutputSha256
$csvRows | Export-Csv -Path $csvPath -NoTypeInformation

# Emit JSON
$jsonPath = Join-Path $OutputRoot "benchmark-results.json"
$benchmarkResult = [ordered]@{
    Notice = "PERFORMANCE-ONLY EXPERIMENT. OUTPUT CORRECTNESS NOT VALIDATED."
    Environment = [ordered]@{
        OsVersion = [System.Environment]::OSVersion.ToString()
        MachineName = [System.Environment]::MachineName
        ProcessorCount = [System.Environment]::ProcessorCount
        BenchmarkStartTimeUtc = $startTime.ToString("o")
        ExecutionMode = "Native AOT win-x64 Release"
        TotalDriverPackagesInSet = $totalPackages
        DrvCtlExeSha256 = $drvctlSha256
        LibwimDllSha256 = $libwimSha256
        BaselineWimSha256 = $baselineSha256Before
    }
    ExecutionSummary = [ordered]@{
        WarmupRunsPerImplementation = $Warmups
        MeasuredRunsPerImplementation = $Pairs
        Ordering = "Paired alternating order (odd: drvctl->DISM, even: DISM->drvctl)"
        CacheDisclaimer = "Repeated same-machine benchmark under normal Windows filesystem cache behavior. Not cold-cache or post-reboot."
        AllCompletedWithoutCrash = $allCompleted
    }
    DrvCtlStatistics = [ordered]@{
        Runs = $measuredDrv.Count
        PackagesProcessed = $totalPackages
        MutationMs = $drvMutationStats
        EndToEndMs = $drvE2EStats
    }
    DismStatistics = [ordered]@{
        Runs = $measuredDism.Count
        PackagesProcessed = $totalPackages
        ServicingMs = $dismServicingStats
        EndToEndMs = $dismE2EStats
    }
    Comparison = [ordered]@{
        MutationOrServicingSpeedup = if ($mutationSpeedup) { "${mutationSpeedup}x" } else { "N/A" }
        MutationOrServicingTimeReductionPercent = if ($mutationReductionPct) { "${mutationReductionPct}%" } else { "N/A" }
        EndToEndSpeedup = if ($e2eSpeedup) { "${e2eSpeedup}x" } else { "N/A" }
        EndToEndTimeReductionPercent = if ($e2eReductionPct) { "${e2eReductionPct}%" } else { "N/A" }
    }
    Runs = $allRuns
}

$benchmarkResult | ConvertTo-Json -Depth 10 | Set-Content -Path $jsonPath

Write-Host "`n================================================================================"
Write-Host "BENCHMARK RESULTS SUMMARY (67 PACKAGES)"
Write-Host "PERFORMANCE-ONLY EXPERIMENT. OUTPUT CORRECTNESS NOT VALIDATED."
Write-Host "================================================================================"
Write-Host ("{0,-15} {1,6} {2,18} {3,16} {4,14} {5,14}" -f "Implementation", "Runs", "Median Service (ms)", "Median E2E (ms)", "Min E2E (ms)", "Max E2E (ms)")
Write-Host ("{0,-15} {1,6} {2,18} {3,16} {4,14} {5,14}" -f "drvctl (AOT)", $measuredDrv.Count, $drvMutationStats.Median, $drvE2EStats.Median, $drvE2EStats.Min, $drvE2EStats.Max)
Write-Host ("{0,-15} {1,6} {2,18} {3,16} {4,14} {5,14}" -f "DISM /Recurse", $measuredDism.Count, $dismServicingStats.Median, $dismE2EStats.Median, $dismE2EStats.Min, $dismE2EStats.Max)
Write-Host "--------------------------------------------------------------------------------"
if ($allCompleted) {
    Write-Host "Servicing/Mutation Speedup: ${mutationSpeedup}x (${mutationReductionPct}% time reduction)"
    Write-Host "End-to-End Speedup:         ${e2eSpeedup}x (${e2eReductionPct}% time reduction)"
} else {
    Write-Host "Speedup withheld due to execution failure."
}
Write-Host "--------------------------------------------------------------------------------"
Write-Host "JSON Report: $jsonPath"
Write-Host "CSV Summary: $csvPath"
Write-Host "================================================================================"
