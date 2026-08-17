[CmdletBinding()]
param(
    [string] $Report = (Join-Path $PSScriptRoot 'results\driverdatabase-encoding-study.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Study([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

$study = Get-Content -LiteralPath $Report -Raw | ConvertFrom-Json -Depth 100
Assert-Study ($study.PackageCount -ge 67) "Expected at least 67 normalized packages; found $($study.PackageCount)."
Assert-Study (@($study.VersionObservations | Where-Object { -not $_.CoreMatch }).Count -eq 0) 'The explained Version core did not match every specimen.'
Assert-Study (@($study.SignerObservations | Where-Object { -not $_.ScoreMatch }).Count -eq 0) 'SetupAPI SignerScore did not match every observed DriverDatabase value.'
Assert-Study (@($study.SignerObservations | Where-Object { $_.ObservedName -and -not $_.NameMatch }).Count -eq 0) 'SetupAPI signer identity did not match a stored SignerName.'
Assert-Study (@($study.PnpLockdownObservations | Where-Object { -not $_.SourceMatch }).Count -eq 0) 'A PnpLockdown Source prediction failed.'
Assert-Study (@($study.PnpLockdownObservations | Where-Object { -not $_.OwnersMatch }).Count -eq 0) 'A new PnpLockdown Owners prediction failed.'
Assert-Study (@($study.PnpLockdownObservations | Where-Object { -not $_.ClassMatch }).Count -eq 0) 'The prototype PnpLockdown Class hypothesis failed.'
Assert-Study (-not $study.CanTask7GenerateEveryRequiredField) 'The study incorrectly marked Task 7 as complete.'

$validStatuses = @('Solved', 'Prototype-supported', 'Partially understood', 'Unsupported')
Assert-Study (@($study.FinalMatrix | Where-Object Status -notin $validStatuses).Count -eq 0) 'The final matrix contains an invalid status.'

Write-Output "Encoding study checks passed: $($study.PackageCount) packages, $(@($study.VersionObservations).Count) Version values, $(@($study.DeviceIdObservations).Count) DeviceIds values, $(@($study.PnpLockdownObservations).Count) PnpLockdown records."
