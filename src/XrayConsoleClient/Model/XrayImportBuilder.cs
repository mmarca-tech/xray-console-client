using System.Globalization;

namespace XrayConsoleClient;

static class XrayImportBuilder
{
    public const string ManualTestType = "Manual";
    public const string CucumberTestType = "Cucumber";
    public const string DefaultSummary = "Execution automatically imported";
    public const string DefaultDescription = "This execution is automatically created when importing execution results from an external source";

    // Xray rejects summaries and step texts longer than 255 characters.
    public const int MaxTextLength = 255;

    public static string Truncate(string text) => text.Length > MaxTextLength ? text[..MaxTextLength] : text;

    public static XrayTestInfo TestInfo(
        string projectKey,
        string type,
        string summary,
        List<XrayStepDefinition>? steps = null,
        List<string>? labels = null,
        List<string>? requirementKeys = null) => new()
    {
        ProjectKey = projectKey,
        Summary = Truncate(summary),
        Type = type,
        Steps = steps,
        Labels = labels,
        RequirementKeys = requirementKeys
    };

    public static XrayImport CreateImport(List<XrayTest> tests, UploadOptions options) => new()
    {
        Tests = tests,
        TestExecutionKey = options.ExecutionKey,
        Info = new XrayExecutionInfo
        {
            Project = options.ProjectKey,
            TestPlanKey = options.PlanKey,
            Summary = options.Summary ?? DefaultSummary,
            Description = options.Description ?? DefaultDescription,
            TestEnvironments = options.Environments?
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToList(),
            Version = options.ReleaseVersion
        }
    };

    // Xray wants second precision with an explicit offset: 2026-10-30T09:15:00+00:00
    public static string FormatDate(DateTimeOffset date) =>
        date.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + "+00:00";

    public static string? FormatDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? FormatDate(date)
            : null;

    public static string? FormatEpochMilliseconds(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds)
            ? FormatDate(DateTimeOffset.FromUnixTimeMilliseconds(milliseconds))
            : null;
}
