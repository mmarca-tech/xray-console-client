namespace XrayConsoleClient;

// Body of the Xray json import (POST /import/execution). Property order follows the npm client.
sealed class XrayImport
{
    public List<XrayTest> Tests { get; init; } = [];
    public string? TestExecutionKey { get; init; }
    public XrayExecutionInfo? Info { get; init; }
}

sealed class XrayExecutionInfo
{
    public required string Project { get; init; }
    public string? TestPlanKey { get; init; }
    public required string Summary { get; init; }
    public required string Description { get; init; }
    public List<string>? TestEnvironments { get; init; }
    public string? Version { get; init; }
}

sealed class XrayTest
{
    public required string Status { get; init; }
    public required XrayTestInfo TestInfo { get; init; }

    // The NUnit sync sets it to the key of the existing test, or clears it when that key does not exist.
    public string? TestKey { get; set; }
    public string? Start { get; init; }
    public string? Finish { get; init; }
    public string? Comment { get; init; }
    public List<XrayStepResult>? Steps { get; init; }
    public List<string>? Defects { get; init; }
    public List<XrayEvidence>? Evidence { get; init; }
}

// Definition of the test, used by Xray when it creates the test.
sealed class XrayTestInfo
{
    public required string ProjectKey { get; init; }
    public required string Summary { get; init; }
    public required string Type { get; init; }
    public List<XrayStepDefinition>? Steps { get; init; }
    public List<string>? Labels { get; init; }
    public List<string>? RequirementKeys { get; init; }
}

sealed record XrayStepDefinition(string Action, string Data, string Result);

sealed class XrayStepResult
{
    public required string Status { get; init; }
    public required string Comment { get; init; }
    public required string ActualResult { get; init; }
    public List<XrayEvidence>? Evidences { get; init; }
}

sealed record XrayEvidence(string Data, string Filename, string ContentType);
