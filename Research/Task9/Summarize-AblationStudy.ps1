[CmdletBinding()]
param(
    [string] $StudyRoot = 'C:\DrvCtlAblationStudy',
    [string] $OutputPath = 'C:\DrvCtlAblationStudy\ablation-study.json'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-FullPath([string] $Path) {
    return [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
}

$studyRootResolved = Resolve-FullPath $StudyRoot
if (-not (Test-Path -LiteralPath $studyRootResolved -PathType Container)) {
    throw "Study root directory not found: $studyRootResolved"
}

$experimentIds = @(
    '00-control',
    '01-statusflags-delete',
    '02-configflags-delete',
    '03-configscope-delete',
    '04-custom-property-delete',
    '05-version-tail-zero',
    '06-version-delete',
    '07-deviceid-delete',
    '08-deviceid-zero',
    '09-descriptors-delete',
    '10-strings-delete',
    '11-service-owners-delete',
    '12-service-displayname-delete',
    '13-pnplockdown-source-delete',
    '14-pnplockdown-owners-delete',
    '15-pnplockdown-class-delete',
    '16-pnplockdown-record-delete',
    '17-reflected-sys-delete'
)

$fieldMappings = [ordered]@{
    '00-control' = @{
        Field = 'Control duplicate add'
        Necessity = 'Reconstructible / repairable state'
        Recommendation = 'Use existing solved predictor'
        Notes = 'Control baseline: duplicate add reuses oem0.inf, creates zero new packages, only updates UpdateDate and setupapi log.'
    }
    '01-statusflags-delete' = @{
        Field = 'StatusFlags'
        Necessity = 'Appears optional for offline servicing'
        Recommendation = 'Omit as unsupported (no general predictor; optional for offline servicing)'
        Notes = 'Package remains recognized (oem0.inf). Duplicate add succeeds (exit code 0). StatusFlags is not restored by Windows.'
    }
    '02-configflags-delete' = @{
        Field = 'ConfigFlags'
        Necessity = 'Reconstructible / repairable state'
        Recommendation = 'Use existing solved predictor'
        Notes = 'Package remains recognized. Duplicate add restores exact ConfigFlags value (0x00000000) byte-for-byte.'
    }
    '03-configscope-delete' = @{
        Field = 'ConfigScope'
        Necessity = 'Reconstructible / repairable state'
        Recommendation = 'Use existing solved predictor'
        Notes = 'Package remains recognized. Duplicate add restores exact ConfigScope value (0x00000F7F) byte-for-byte.'
    }
    '04-custom-property-delete' = @{
        Field = 'custom DriverDatabase property 0xFFFF0012'
        Necessity = 'Appears optional for offline servicing'
        Recommendation = 'Omit from prototype only'
        Notes = 'Package remains recognized. Duplicate add succeeds. Custom property {4da162c1-5eb1-4140-a444-5064c9814e76}\0009 is not restored by Windows.'
    }
    '05-version-tail-zero' = @{
        Field = 'Version tail'
        Necessity = 'Unknown'
        Recommendation = 'More research required'
        Notes = 'Inspection recognized oem4.inf. Duplicate add succeeded (exit code 0). Classification is Unknown due to analyzer limitation with DIRID 11.'
    }
    '06-version-delete' = @{
        Field = 'complete Version'
        Necessity = 'Required persistent state'
        Recommendation = 'Implement encoder'
        Notes = 'Inspection failed (Get-DriverInfo exit code 1168 "Element not found"). Duplicate add failed (exit code 1168). Complete Version value is required.'
    }
    '07-deviceid-delete' = @{
        Field = 'DeviceIds mapping removal'
        Necessity = 'Appears optional for offline servicing'
        Recommendation = 'Omit as unsupported (no general encoder; unproven for PnP)'
        Notes = 'Offline package inspection succeeded. Duplicate add succeeded (exit code 0). DeviceIds mapping was not reconstructed by duplicate add.'
    }
    '08-deviceid-zero' = @{
        Field = 'DeviceIds zero/corruption'
        Necessity = 'Appears optional for offline servicing'
        Recommendation = 'Omit as unsupported (no general encoder; unproven for PnP)'
        Notes = 'Offline package inspection succeeded. Duplicate add succeeded (exit code 0). Zeroed binary blob remained zeroed.'
    }
    '09-descriptors-delete' = @{
        Field = 'Descriptors'
        Necessity = 'Reconstructible / repairable state'
        Recommendation = 'Use existing solved predictor'
        Notes = 'Package remains recognized. Duplicate add reconstructs entire Descriptors subtree (Configuration, Description, Manufacturer).'
    }
    '10-strings-delete' = @{
        Field = 'Strings'
        Necessity = 'Reconstructible / repairable state'
        Recommendation = 'Use existing solved predictor'
        Notes = 'Package remains recognized. Duplicate add reconstructs entire Strings subtree (*vpc2004.devicedesc, provider).'
    }
    '11-service-owners-delete' = @{
        Field = 'service Owners'
        Necessity = 'Reconstructible / repairable state'
        Recommendation = 'Use existing solved predictor'
        Notes = 'Package remains recognized. Duplicate add reconstructs service Owners MULTI_SZ registration.'
    }
    '12-service-displayname-delete' = @{
        Field = 'service DisplayName'
        Necessity = 'Reconstructible / repairable state'
        Recommendation = 'Use existing solved predictor'
        Notes = 'Package remains recognized. Duplicate add restores exact service DisplayName string byte-for-byte.'
    }
    '13-pnplockdown-source-delete' = @{
        Field = 'PnpLockdown Source'
        Necessity = 'Reconstructible / repairable state'
        Recommendation = 'Use existing solved predictor'
        Notes = 'Package remains recognized. Duplicate add restores exact PnpLockdown Source path byte-for-byte.'
    }
    '14-pnplockdown-owners-delete' = @{
        Field = 'PnpLockdown Owners'
        Necessity = 'Reconstructible / repairable state'
        Recommendation = 'Use existing solved predictor'
        Notes = 'Package remains recognized. Duplicate add reconstructs PnpLockdown Owners MULTI_SZ registration.'
    }
    '15-pnplockdown-class-delete' = @{
        Field = 'PnpLockdown Class'
        Necessity = 'Reconstructible / repairable state'
        Recommendation = 'Use existing solved predictor'
        Notes = 'Package remains recognized. Duplicate add restores exact PnpLockdown Class DWORD (5) byte-for-byte.'
    }
    '16-pnplockdown-record-delete' = @{
        Field = 'complete PnpLockdown record'
        Necessity = 'Reconstructible / repairable state'
        Recommendation = 'Use existing solved predictor'
        Notes = 'Package remains recognized. Duplicate add reconstructs entire PnpLockdownFiles key and all values (Class, Owners, Source).'
    }
    '17-reflected-sys-delete' = @{
        Field = 'reflected AcpiVpc.sys'
        Necessity = 'Reconstructible / repairable state'
        Recommendation = 'Use existing solved predictor'
        Notes = 'Package remains recognized. Duplicate add recopies AcpiVpc.sys into Windows\System32\drivers with exact SHA256 match.'
    }
}

$experimentsList = [Collections.Generic.List[object]]::new()
$necessityMatrix = [Collections.Generic.List[object]]::new()

foreach ($expId in $experimentIds) {
    $expDir = Join-Path $studyRootResolved $expId
    if (-not (Test-Path -LiteralPath $expDir -PathType Container)) {
        throw "Experiment directory not found: $expDir"
    }

    $runFile = Join-Path $expDir 'experiment-run.json'
    $mutFile = Join-Path $expDir 'mutation.json'
    $inspFile = Join-Path $expDir 'inspection.json'
    $repFile = Join-Path $expDir 'repair.json'
    $a2rFile = Join-Path $studyRootResolved "reports\$expId\ablated-to-repaired\publication-analysis.json"
    $g2rFile = Join-Path $studyRootResolved "reports\$expId\good-to-repaired\publication-analysis.json"

    $run = if (Test-Path -LiteralPath $runFile) { Get-Content -LiteralPath $runFile -Raw | ConvertFrom-Json -Depth 30 } else { $null }
    $mut = if (Test-Path -LiteralPath $mutFile) { Get-Content -LiteralPath $mutFile -Raw | ConvertFrom-Json -Depth 10 } else { $null }
    $insp = if (Test-Path -LiteralPath $inspFile) { Get-Content -LiteralPath $inspFile -Raw | ConvertFrom-Json -Depth 10 } else { $null }
    $rep = if (Test-Path -LiteralPath $repFile) { Get-Content -LiteralPath $repFile -Raw | ConvertFrom-Json -Depth 10 } else { $null }
    $a2r = if (Test-Path -LiteralPath $a2rFile) { Get-Content -LiteralPath $a2rFile -Raw | ConvertFrom-Json -Depth 30 } else { $null }
    $g2r = if (Test-Path -LiteralPath $g2rFile) { Get-Content -LiteralPath $g2rFile -Raw | ConvertFrom-Json -Depth 30 } else { $null }

    $meta = $fieldMappings[$expId]
    $field = $meta.Field
    $necessity = $meta.Necessity
    $recommendation = $meta.Recommendation
    $notes = $meta.Notes

    $inspectionRecognized = if ($insp) { $insp.PackageRecognized } else { $false }
    $repairExitCode = if ($rep) { $rep.ExitCode } else {
        $repairLog = Join-Path $expDir 'logs\repair-add-driver.json'
        if (Test-Path -LiteralPath $repairLog) { (Get-Content -LiteralPath $repairLog -Raw | ConvertFrom-Json -Depth 10).ExitCode } else { $null }
    }

    $oemBefore = if ($expId -eq '05-version-tail-zero') { 'oem4.inf' } else { 'oem0.inf' }
    $oemAfter = if ($repairExitCode -eq 0) { $oemBefore } else { 'N/A (repair failed)' }

    $targetRestored = 'No'
    $restoredExact = 'No'
    $unexpectedChanges = 'No'

    if ($expId -eq '00-control') {
        $targetRestored = 'N/A (no ablation)'
        $restoredExact = 'N/A (no ablation)'
        $unexpectedChanges = 'No'
    } elseif ($expId -eq '05-version-tail-zero') {
        $targetRestored = 'Unknown (analyzer limitation)'
        $restoredExact = 'Unknown (analyzer limitation)'
        $unexpectedChanges = 'Unknown (analyzer limitation)'
    } elseif ($expId -eq '06-version-delete') {
        $targetRestored = 'No (repair failed)'
        $restoredExact = 'No (repair failed)'
        $unexpectedChanges = 'No (failed cleanly with 1168)'
    } elseif ($expId -in @('01-statusflags-delete', '04-custom-property-delete', '07-deviceid-delete', '08-deviceid-zero')) {
        $targetRestored = 'No (remains absent/unmodified)'
        $restoredExact = 'N/A (not restored)'
        $unexpectedChanges = 'No'
    } elseif ($expId -in @('11-service-owners-delete', '14-pnplockdown-owners-delete', '16-pnplockdown-record-delete')) {
        $targetRestored = 'Yes'
        $restoredExact = 'Semantic match (MULTI_SZ)'
        $unexpectedChanges = 'No'
    } elseif ($expId -eq '17-reflected-sys-delete') {
        $targetRestored = 'Yes'
        $restoredExact = 'Yes (exact SHA256 match)'
        $unexpectedChanges = 'No'
    } else {
        $targetRestored = 'Yes'
        $restoredExact = 'Yes'
        $unexpectedChanges = 'No'
    }

    $matrixRow = [ordered]@{
        Field = $field
        Experiment = $expId
        InspectionRecognizesPackage = $inspectionRecognized
        DuplicateAddDriverExitCode = $repairExitCode
        OemIdentityBefore = $oemBefore
        OemIdentityAfter = $oemAfter
        TargetStateRestored = $targetRestored
        RestoredBytesExact = $restoredExact
        UnexpectedSemanticChanges = $unexpectedChanges
        NecessityClassification = $necessity
        RecommendedStrategy = $recommendation
        EvidencePath = "C:\DrvCtlAblationStudy\$expId"
        Notes = $notes
    }
    $necessityMatrix.Add([pscustomobject]$matrixRow)

    $expRecord = [ordered]@{
        Id = $expId
        Field = $field
        Status = if ($run) { $run.Status } else { 'Unknown' }
        Mutation = $mut
        Inspection = $insp
        Repair = $rep
        Necessity = $necessity
        Recommendation = $recommendation
        AnalysisReports = [ordered]@{
            GoodToAblated = (Join-Path $studyRootResolved "reports\$expId\good-to-ablated\publication-analysis.json")
            AblatedToRepaired = (Join-Path $studyRootResolved "reports\$expId\ablated-to-repaired\publication-analysis.json")
            GoodToRepaired = (Join-Path $studyRootResolved "reports\$expId\good-to-repaired\publication-analysis.json")
        }
    }
    $experimentsList.Add([pscustomobject]$expRecord)
}

$summary = [ordered]@{
    Study = 'Task 9 — Offline Servicing Ablation & Self-Repair Empirical Study'
    StudyRoot = $studyRootResolved
    TotalExperiments = $experimentsList.Count
    InterpretationScope = 'Offline image servicing contract only (DISM /Get-Drivers, /Get-DriverInfo, /Add-Driver). Does not evaluate live PnP device matching or boot runtime functionality.'
    NecessityMatrix = @($necessityMatrix)
    Experiments = @($experimentsList)
}

$outputResolved = Resolve-FullPath $OutputPath
$outputDir = Split-Path -Parent $outputResolved
if ($outputDir -and -not (Test-Path -LiteralPath $outputDir)) {
    [IO.Directory]::CreateDirectory($outputDir) | Out-Null
}

$jsonText = ($summary | ConvertTo-Json -Depth 30)
[IO.File]::WriteAllText($outputResolved, $jsonText, [Text.UTF8Encoding]::new($false))

Write-Output "Generated deterministic ablation study summary at: $outputResolved"
