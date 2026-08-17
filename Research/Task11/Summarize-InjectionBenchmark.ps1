# Research/Task11/Summarize-InjectionBenchmark.ps1
# Summarizes benchmark-results.json and validates outputs

[CmdletBinding()]
param(
    [string]$BenchmarkJson = "C:\DrvCtlTask11Benchmark\benchmark-results.json"
)

$ErrorActionPreference = "Stop"

if (!(Test-Path $BenchmarkJson)) { throw "Benchmark JSON not found: $BenchmarkJson" }

$data = Get-Content $BenchmarkJson | ConvertFrom-Json

Write-Host "================================================================================"
Write-Host "TASK 11 BENCHMARK SUMMARY"
Write-Host "================================================================================"
Write-Host "Execution Mode: $($data.Environment.ExecutionMode)"
Write-Host "Machine Name:   $($data.Environment.MachineName) ($($data.Environment.ProcessorCount) logical processors)"
Write-Host "OS Version:     $($data.Environment.OsVersion)"
Write-Host "Disclaimer:     $($data.ExecutionSummary.CacheDisclaimer)"
Write-Host "--------------------------------------------------------------------------------"
Write-Host ("{0,-16} {1,6} {2,18} {3,16} {4,14} {5,14}" -f "Implementation", "Runs", "Median Service (ms)", "Median E2E (ms)", "Min E2E (ms)", "Max E2E (ms)")
Write-Host ("{0,-16} {1,6} {2,18} {3,16} {4,14} {5,14}" -f "drvctl", $data.DrvCtlStatistics.Runs, $data.DrvCtlStatistics.MutationMs.Median, $data.DrvCtlStatistics.EndToEndMs.Median, $data.DrvCtlStatistics.EndToEndMs.Min, $data.DrvCtlStatistics.EndToEndMs.Max)
Write-Host ("{0,-16} {1,6} {2,18} {3,16} {4,14} {5,14}" -f "DISM", $data.DismStatistics.Runs, $data.DismStatistics.ServicingMs.Median, $data.DismStatistics.EndToEndMs.Median, $data.DismStatistics.EndToEndMs.Min, $data.DismStatistics.EndToEndMs.Max)
Write-Host "--------------------------------------------------------------------------------"
Write-Host "Mutation/Servicing Speedup: $($data.Comparison.MutationOrServicingSpeedup) ($($data.Comparison.MutationOrServicingTimeReductionPercent) reduction)"
Write-Host "End-to-End Speedup:         $($data.Comparison.EndToEndSpeedup) ($($data.Comparison.EndToEndTimeReductionPercent) reduction)"
Write-Host "================================================================================"
