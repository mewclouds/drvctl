using DrvCtl.Validation;

namespace DrvCtl.Offline;

internal sealed class OfflineApplyFixtureValidator
{
    internal OfflineApplyFixtureComparison Validate(OfflineApplyResult result)
    {
        DriverPlanFixture fixture = DriverPlanFixtures.FindForPackage(result.Plan.SourcePlan.Package.Directory);
        List<OfflineApplyFixtureResult> comparisons = [];

        bool copiesMatch = fixture.Copies.Length == result.Files.Length && fixture.Copies.All(expected =>
            result.Files.Any(actual =>
                Path.GetFileName(actual.SourcePath).Equals(expected.SourceFile, StringComparison.OrdinalIgnoreCase) &&
                Path.GetRelativePath(result.Plan.Workspace, actual.OutputPath).Equals(expected.DestinationPath, StringComparison.OrdinalIgnoreCase) &&
                actual.Matches));
        comparisons.Add(copiesMatch
            ? Matched("Reflected filesystem subset", $"{result.Files.Length} expected copy operation(s) applied and verified.")
            : Contradiction("Reflected filesystem subset", "Applied file operations differ from the known fixture subset."));

        bool servicesMatch = fixture.Services.Length * 4 == result.RegistryWrites.Length && fixture.Services.All(expected =>
            HasWrite(result, expected.Name, "Type", expected.Type.ToString()) &&
            HasWrite(result, expected.Name, "Start", expected.Start.ToString()) &&
            HasWrite(result, expected.Name, "ErrorControl", expected.ErrorControl.ToString()) &&
            HasWrite(result, expected.Name, "ImagePath", expected.ImagePath));
        comparisons.Add(servicesMatch
            ? Matched("Reflected service subset", $"{fixture.Services.Length} expected service(s) applied and verified.")
            : Contradiction("Reflected service subset", "Applied service values differ from the known fixture subset."));

        foreach (ObservedServicingFact observation in fixture.Observations)
        {
            if (observation.Field == ObservedServicingField.ReflectedFileByteIdentity)
            {
                comparisons.Add(result.Files.All(file => file.Matches)
                    ? Matched(observation.Name, "Every reflected output is byte-identical to its package source by size and SHA-256.")
                    : Contradiction(observation.Name, "At least one reflected output differs from its package source."));
            }
            else
            {
                comparisons.Add(new OfflineApplyFixtureResult(observation.Name, OfflineApplyFixtureStatus.ExpectedUnresolvedDifference, $"Observed by DISM: {observation.Value}; intentionally not applied by the simulator."));
            }
        }
        foreach (OfflineVerificationResult verification in result.Verification.Where(item => !item.Succeeded))
            comparisons.Add(Contradiction(verification.Name, verification.Detail));

        return new OfflineApplyFixtureComparison(fixture.Name, [.. comparisons]);
    }

    private static bool HasWrite(OfflineApplyResult result, string serviceName, string valueName, string value) =>
        result.RegistryWrites.Any(write =>
            write.KeyPath.EndsWith("\\Services\\" + serviceName, StringComparison.OrdinalIgnoreCase) &&
            write.Name.Equals(valueName, StringComparison.OrdinalIgnoreCase) &&
            write.Value.Equals(value, StringComparison.OrdinalIgnoreCase));

    private static OfflineApplyFixtureResult Matched(string name, string detail) => new(name, OfflineApplyFixtureStatus.MatchedAppliedSubset, detail);
    private static OfflineApplyFixtureResult Contradiction(string name, string detail) => new(name, OfflineApplyFixtureStatus.Contradiction, detail);
}

internal enum OfflineApplyFixtureStatus
{
    MatchedAppliedSubset,
    ExpectedUnresolvedDifference,
    Contradiction
}

internal sealed record OfflineApplyFixtureResult(string Name, OfflineApplyFixtureStatus Status, string Detail);

internal sealed record OfflineApplyFixtureComparison(string FixtureName, OfflineApplyFixtureResult[] Results)
{
    internal bool HasContradictions => Results.Any(result => result.Status == OfflineApplyFixtureStatus.Contradiction);
}
