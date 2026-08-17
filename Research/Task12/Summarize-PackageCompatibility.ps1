# Research/Task12/Summarize-PackageCompatibility.ps1
# Summarizes package-compatibility.json

[CmdletBinding()]
param(
    [string]$StudyRoot = "C:\DrvCtlPackageCompatibility"
)

$jsonPath = Join-Path $StudyRoot "package-compatibility.json"
if (!(Test-Path $jsonPath)) { throw "Compatibility JSON not found: $jsonPath" }

$data = Get-Content $jsonPath | ConvertFrom-Json

Write-Host "================================================================================"
Write-Host "TASK 12 COMPATIBILITY SWEEP SUMMARY"
Write-Host "================================================================================"
Write-Host "Total Packages:              $($data.Summary.TotalPackages)"
Write-Host "Tested Packages:             $($data.Summary.TestedPackages)"
Write-Host "PASS:                        $($data.Summary.Pass)"
Write-Host "PASS_WITH_KNOWN_OMISSIONS:   $($data.Summary.PassWithKnownOmissions)"
Write-Host "UNSUPPORTED_BY_DRVCTL:       $($data.Summary.UnsupportedByDrvCtl)"
Write-Host "DRVCTL_EXECUTION_FAILURE:    $($data.Summary.DrvCtlExecutionFailure)"
Write-Host "WINDOWS_RECOGNITION_FAILURE: $($data.Summary.WindowsRecognitionFailure)"
Write-Host "SEMANTIC_MISMATCH:           $($data.Summary.SemanticMismatch)"
Write-Host "DISM_REFERENCE_FAILURE:      $($data.Summary.DismReferenceFailure)"
Write-Host "INVENTORY_AMBIGUOUS:         $($data.Summary.InventoryAmbiguous)"
Write-Host "HARNESS_FAILURE:             $($data.Summary.HarnessFailure)"
Write-Host "--------------------------------------------------------------------------------"
Write-Host "DrvCtl Completed:            $($data.Summary.DrvCtlCompleted)"
Write-Host "DrvCtl Recognized:           $($data.Summary.DrvCtlRecognizedByWindows)"
Write-Host "DISM References Completed:   $($data.Summary.DismReferencesCompleted)"
Write-Host "Zero Contradictions:         $($data.Summary.PackagesWithZeroContradictions)"
Write-Host "With Contradictions:         $($data.Summary.PackagesWithContradictions)"
Write-Host "With Unsupported Omissions:  $($data.Summary.PackagesWithUnsupportedOmissions)"
Write-Host "================================================================================"
Write-Host "`nFailure Groups:"
foreach ($g in $data.FailureGroups) {
    Write-Host "  $($g.Fingerprint): Count = $($g.Count) (Packages: $($g.PackageIndices -join ', '))"
}
Write-Host "================================================================================"
