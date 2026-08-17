using DrvCtl.Drivers;

namespace DrvCtl.Validation;

internal sealed class DriverPlanValidator
{
    internal DriverPlanValidation Validate(string packageDirectory)
    {
        DriverPlanFixture fixture = DriverPlanFixtures.FindForPackage(packageDirectory);
        DriverStagingPlan plan = new DriverStagingPlanner().Create(packageDirectory);
        List<SemanticValidationResult> results = [];

        CompareSet("Package store files", fixture.StoreFiles, plan.StoreFiles.Select(file => file.FileName), results);
        foreach (string deviceId in fixture.DeviceIds)
        {
            AddPresenceResult("Device ID " + deviceId, deviceId, plan.DeviceIds, results);
        }

        CompareCopies(fixture.Copies, plan.Reflection.Copies, results);
        CompareServices(fixture.Services, plan.Reflection.Services, results);
        foreach (ObservedServicingFact observation in fixture.Observations)
        {
            results.Add(CompareObservation(observation, plan));
        }

        return new DriverPlanValidation(fixture, plan, [.. results]);
    }

    private static void CompareCopies(ExpectedCopy[] expected, ReflectedFileCopy[] actual, List<SemanticValidationResult> results)
    {
        if (expected.Length == 0)
        {
            results.Add(actual.Length == 0
                ? Derived("No critical-driver file reflection", "The plan contains no global reflected file copies.")
                : Contradiction("No critical-driver file reflection", $"The plan contains {actual.Length} unexpected reflected file copy operation(s)."));
            return;
        }
        foreach (ExpectedCopy copy in expected)
        {
            bool found = actual.Any(candidate => candidate.SourceFile.Equals(copy.SourceFile, StringComparison.OrdinalIgnoreCase) && candidate.DestinationPath.Equals(copy.DestinationPath, StringComparison.OrdinalIgnoreCase));
            results.Add(found
                ? Derived("Copy " + copy.SourceFile, copy.DestinationPath)
                : Contradiction("Copy " + copy.SourceFile, $"Expected destination '{copy.DestinationPath}' was not derived."));
        }
        if (actual.Length != expected.Length) results.Add(Contradiction("Reflected copy count", $"Expected {expected.Length}, derived {actual.Length}."));
    }

    private static void CompareServices(ExpectedService[] expected, ReflectedService[] actual, List<SemanticValidationResult> results)
    {
        if (expected.Length == 0)
        {
            results.Add(actual.Length == 0
                ? Derived("No service reflection", "The plan contains no reflected services.")
                : Contradiction("No service reflection", $"The plan contains {actual.Length} unexpected reflected service(s)."));
            return;
        }
        foreach (ExpectedService service in expected)
        {
            bool found = actual.Any(candidate =>
                candidate.Name.Equals(service.Name, StringComparison.OrdinalIgnoreCase) &&
                candidate.Type == service.Type && candidate.Start == service.Start &&
                candidate.ErrorControl == service.ErrorControl &&
                candidate.ImagePath.Equals(service.ImagePath, StringComparison.OrdinalIgnoreCase));
            results.Add(found
                ? Derived("AddService " + service.Name, $"Type={service.Type}, Start={service.Start}, ErrorControl={service.ErrorControl}, ImagePath={service.ImagePath}")
                : Contradiction("AddService " + service.Name, "The derived service does not match the observed service semantics."));
        }
        if (actual.Length != expected.Length) results.Add(Contradiction("Reflected service count", $"Expected {expected.Length}, derived {actual.Length}."));
    }

    private static SemanticValidationResult CompareObservation(ObservedServicingFact observation, DriverStagingPlan plan)
    {
        string? derived = observation.Field switch
        {
            ObservedServicingField.PublishedInfIdentity => plan.PublishedInf.PublishedIdentity,
            ObservedServicingField.CatalogPublication => plan.PublishedCatalog.PublishedIdentity,
            ObservedServicingField.DriverDatabaseHive => plan.DriverDatabase.TargetHive,
            ObservedServicingField.DriverDatabaseRepresentation => plan.DriverDatabase.Representation,
            _ => null
        };
        if (derived is null)
        {
            return new SemanticValidationResult(observation.Name, SemanticValidationStatus.ObservedButUnresolved, $"Observed: {observation.Value}; the plan does not claim this servicing state.");
        }
        return derived.Equals(observation.Value, StringComparison.OrdinalIgnoreCase)
            ? Derived(observation.Name, derived)
            : Contradiction(observation.Name, $"Observed '{observation.Value}', but the plan derived '{derived}'.");
    }

    private static void CompareSet(string name, IEnumerable<string> expected, IEnumerable<string> actual, List<SemanticValidationResult> results)
    {
        string[] expectedValues = expected.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        string[] actualValues = actual.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        bool equal = expectedValues.SequenceEqual(actualValues, StringComparer.OrdinalIgnoreCase);
        results.Add(equal
            ? Derived(name, string.Join(", ", actualValues))
            : Contradiction(name, $"Expected [{string.Join(", ", expectedValues)}], derived [{string.Join(", ", actualValues)}]."));
    }

    private static void AddPresenceResult(string name, string expected, IEnumerable<string> actual, List<SemanticValidationResult> results)
    {
        results.Add(actual.Contains(expected, StringComparer.OrdinalIgnoreCase)
            ? Derived(name, expected)
            : Contradiction(name, $"Expected value '{expected}' was not derived."));
    }

    private static SemanticValidationResult Derived(string name, string detail) => new(name, SemanticValidationStatus.DerivedCorrectly, detail);
    private static SemanticValidationResult Contradiction(string name, string detail) => new(name, SemanticValidationStatus.Contradiction, detail);
}
