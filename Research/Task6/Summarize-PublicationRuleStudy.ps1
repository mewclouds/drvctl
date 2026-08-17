[CmdletBinding()]
param([Parameter(Mandatory)] [string] $StudyRoot)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = [IO.Path]::GetFullPath($StudyRoot)
$manifestPath = Join-Path $root 'study-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Study manifest not found: $manifestPath" }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -Depth 30

function Get-ChangedValues($report, [string] $prefix) {
    return @($report.RegistryDeltas | Where-Object { $_.KeyPath -like "$prefix*" -and $_.AfterValue } | ForEach-Object {
        [ordered]@{ Hive=$_.Hive; KeyPath=$_.KeyPath; Name=$_.ValueName; Change=[string]$_.Change; Type=$_.AfterValue.TypeName; RawHex=$_.AfterValue.RawHex; Decoded=$_.AfterValue.Decoded; DecodedStrings=$_.AfterValue.DecodedStrings }
    })
}

function Get-OemInfMap($report) {
    $delta = @($report.RegistryDeltas | Where-Object { $_.Hive -eq 'SYSTEM' -and $_.KeyPath -eq 'DriverDatabase' -and $_.ValueName -eq 'OemInfMap' }) | Select-Object -Last 1
    if ($delta) { return $(if ($delta.AfterValue) { $delta.AfterValue.RawHex } else { $null }) }
    return $null
}

$observations = [Collections.Generic.List[object]]::new()
foreach ($experiment in $manifest.Experiments) {
    $report = Get-Content -LiteralPath $experiment.Report -Raw | ConvertFrom-Json -Depth 40
    $fileCounts = [ordered]@{}
    foreach ($group in $report.FileDeltas | Group-Object Change) { $fileCounts[[string]$group.Name] = $group.Count }
    $registryCounts = [ordered]@{}
    foreach ($group in $report.RegistryDeltas | Group-Object Change) { $registryCounts[[string]$group.Name] = $group.Count }
    $observations.Add([ordered]@{
        Name = $experiment.Name
        Kind = $experiment.Kind
        Package = $report.SourcePackage.RepositoryIdentity
        NewPublishedInfs = @($report.Observation.NewPublishedInfs)
        BaselinePublishedInfs = @($report.Observation.BaselinePublishedInfs)
        ServicedPublishedInfs = @($report.Observation.ServicedPublishedInfs)
        RepositoryIdentities = @($report.Observation.ObservedServicedRepositoryIdentities)
        DriverDatabaseHives = @($report.Observation.SelectedDriverDatabaseHives)
        OemInfMapAfterRawHex = Get-OemInfMap $report
        FileDeltaCounts = $fileCounts
        RegistryDeltaCounts = $registryCounts
        DriverPackageValues = @(Get-ChangedValues $report 'DriverDatabase\DriverPackages')
        DeviceIdValues = @(Get-ChangedValues $report 'DriverDatabase\DeviceIds')
        DriverInfFileValues = @(Get-ChangedValues $report 'DriverDatabase\DriverInfFiles')
        OwnershipValues = @($report.Observation.OwnershipRecords)
        ServiceComparisons = @($report.ServiceComparisons)
        Contradictions = @($report.Contradictions)
        Report = $experiment.Report
    })
}

$fieldNames = @('Version','Provider','InfName','OemPath','SignerName','ImportDate','Catalog','SignerScore','FileSize','ManifestHash','StatusFlags','OsVersionFloor','LockLevel','ConfigScope','Configurations','Descriptors','Strings','ExtensionId')
$stability = foreach ($packageName in @($observations.Package | Select-Object -Unique | Sort-Object)) {
    $matching = @($observations | Where-Object Package -eq $packageName | Where-Object { $_.DriverPackageValues.Count -gt 0 })
    foreach ($field in $fieldNames) {
        $entries = @($matching | ForEach-Object {
            $experimentName = $_.Name
            $_.DriverPackageValues | Where-Object {
                if (-not $_.KeyPath.StartsWith("DriverDatabase\DriverPackages\$packageName", [StringComparison]::OrdinalIgnoreCase)) {
                    $false
                } elseif ($field -in @('Configurations','Descriptors','Strings')) {
                    $_.KeyPath -like "*\$field*"
                } else {
                    $_.Name -eq $field
                }
            } | ForEach-Object { [ordered]@{ Experiment=$experimentName; KeyPath=$_.KeyPath; Name=$_.Name; RawHex=$_.RawHex } }
        })
        $rawValues = @($entries | ForEach-Object { $_.RawHex } | Select-Object -Unique)
        $signatures = @($entries | Group-Object Experiment | ForEach-Object {
            ($_.Group | Sort-Object KeyPath,Name | ForEach-Object { "$($_.KeyPath)|$($_.Name)|$($_.RawHex)" }) -join ';'
        } | Select-Object -Unique)
        $classification = if ($entries.Count -eq 0) {
            'Not observed for this package'
        } elseif ($matching.Count -lt 2) {
            'Observed once; stability unknown'
        } elseif (($field -in @('Configurations','Descriptors','Strings') -and $signatures.Count -eq 1) -or ($field -notin @('Configurations','Descriptors','Strings') -and $rawValues.Count -eq 1)) {
            'Stable across observed runs'
        } elseif ($field -eq 'ImportDate') {
            'Variable across observed runs'
        } else {
            'Variable or context-dependent across observed runs'
        }
        [ordered]@{
            Package = $packageName
            Field = $field
            Classification = $classification
            Observations = $entries
            RawValues = $rawValues
            SupportingExperiments = @($matching.Name)
        }
    }
}

$correlations = @($observations | Where-Object Kind -in @('Repeatability','DatabaseCorrelation','PairOrder') | Where-Object { $_.DriverDatabaseHives.Count -gt 0 } | ForEach-Object {
    $package = $manifest.Packages | Where-Object Directory -eq (($manifest.Experiments | Where-Object Name -eq $_.Name).Package) | Select-Object -First 1
    [ordered]@{ Experiment=$_.Name; Package=$_.Package; ObservedDatabaseHives=$_.DriverDatabaseHives; Inspection=$package.Inspection; Plan=$package.Plan }
})

$duplicate = $observations | Where-Object Name -eq 'duplicate-once-twice' | Select-Object -First 1
$gapRemoval = $observations | Where-Object Name -eq 'gap-populated-removed' | Select-Object -First 1
$gapReuse = $observations | Where-Object Name -eq 'gap-removed-refilled' | Select-Object -First 1
$contradictions = @($observations | ForEach-Object { $name=$_.Name; $_.Contradictions | ForEach-Object { [ordered]@{Experiment=$name; Detail=$_} } })

$report = [ordered]@{
    Study = $manifest.Study
    BaselineWim = $manifest.BaselineWim
    BaselineSha256Before = $manifest.BaselineSha256Before
    BaselineSha256After = $manifest.BaselineSha256After
    Environment = $manifest.Environment
    Experiments = @($observations)
    PackageObservations = @($manifest.Packages)
    OemAllocationObservations = @($observations | Select-Object Name,BaselinePublishedInfs,ServicedPublishedInfs,NewPublishedInfs)
    OemInfMapObservations = @($observations | Select-Object Name,OemInfMapAfterRawHex)
    StableVolatileFieldAnalysis = @($stability)
    SystemDriversCorrelations = $correlations
    DuplicateStagingObservation = $duplicate
    GapRemovalObservation = $gapRemoval
    GapReuseObservation = $gapReuse
    Contradictions = $contradictions
    Hypotheses = @(
        [ordered]@{ Text='OEM identities appear consistent with insertion order only within the observed experiments.'; SupportingExperiments=@('order-forward-first','order-forward-second','order-reverse-first','order-reverse-second','session-forward-reverse'); Counterexamples=@(); Scope='Observed fixtures only; not an allocation rule.' },
        [ordered]@{ Text='DriverDatabase hive selection appears package-dependent rather than publication-order-dependent in this sample.'; SupportingExperiments=@('pair-forward-first','pair-forward-second','pair-reverse-first','pair-reverse-second'); Counterexamples=@(); Scope='Observed fixtures only; no classifier inferred.' }
    )
    StillUnresolved = @('General OEM INF allocation algorithm','OemInfMap encoding','General SYSTEM versus DRIVERS selection rule','DriverDatabase value encoders','Catalog publication implementation','Driver package removal implementation','PnP ownership write semantics')
}

$output = Join-Path $root 'publication-rule-study.json'
[IO.File]::WriteAllText($output, ($report | ConvertTo-Json -Depth 40), [Text.UTF8Encoding]::new($false))
Write-Host $output
