using static XrayConsoleClient.XrayImportBuilder;

namespace XrayConsoleClient;

sealed class ExecutionUploader(
    XrayRestClient xray,
    XrayGraphqlClient graphql,
    JiraRestClient jira,
    Func<TimeSpan, Task>? delay = null)
{
    public const int ImportChunkSize = 50;
    private static readonly TimeSpan PauseBetweenChunks = TimeSpan.FromSeconds(3);

    private readonly Func<TimeSpan, Task> delay = delay ?? (wait => Task.Delay(wait));

    public async Task RunAsync(UploadOptions options)
    {
        var testType = options.TestType.ToLowerInvariant();
        if (testType == "nunit")
        {
            Console.WriteLine("Uploading nunit execution");
            var nunitXml = await File.ReadAllTextAsync(ReportFiles.RequireFile(options.FilePath));
            await xray.ImportNunitAsync(options.ProjectKey, options.PlanKey, options.ExecutionKey, nunitXml);
            return;
        }

        Console.WriteLine($"Uploading {testType} execution");
        var tests = ReadReport(testType, options.FilePath, options.ProjectKey);
        if (testType == "nunit-xml")
        {
            await SyncTestCasesAsync(tests, options.ProjectKey);
        }

        if (tests.Count == 0)
        {
            Console.WriteLine("No tests were found to upload via xray json");
            return;
        }

        var executionKey = await ImportAsync(CreateImport(tests, options));
        Console.WriteLine($"Test execution {executionKey} imported");

        if (options.IssueKey is not null)
        {
            await LinkIssueAsync(options, executionKey);
        }

        if (options.JiraCustomFields is not null)
        {
            await SetCustomFieldsAsync(options);
        }
    }

    internal static List<XrayTest> ReadReport(string testType, string path, string projectKey) => testType switch
    {
        "cucumber-specflow" => SpecflowReport.Read(path, projectKey),
        "cucumber-json" => CucumberReport.Read(path, projectKey),
        "junit-xml" => JunitReport.Read(path, projectKey),
        "nunit-xml" => NunitReport.Read(path, projectKey),
        "allure-xml" => AllureReport.ReadXml(path, projectKey),
        "allure-json" => AllureReport.ReadJson(path, projectKey),
        _ => throw new XrayInputException($"Unknown test type '{testType}', options available are {string.Join(" | ", UploadOptions.TestTypes)}")
    };

    // Big imports go in tranches of 50 tests: the first one creates (or updates) the execution,
    // the others are added to it.
    internal async Task<string> ImportAsync(XrayImport import)
    {
        var chunks = import.Tests.Chunk(ImportChunkSize).ToList();
        var executionKey = await xray.ImportExecutionAsync(new XrayImport
        {
            Tests = [.. chunks[0]],
            TestExecutionKey = import.TestExecutionKey,
            Info = import.Info
        });

        foreach (var chunk in chunks.Skip(1))
        {
            await xray.ImportExecutionAsync(new XrayImport { Tests = [.. chunk], TestExecutionKey = executionKey, Info = import.Info });
            await delay(PauseBetweenChunks);
        }

        return executionKey;
    }

    // Xray json import only creates tests that do not exist yet, it never updates existing ones.
    // So for every test: if it exists (by TestCaseKey, or else by exact summary) its steps are replaced,
    // otherwise it is left to the import to create it.
    internal async Task SyncTestCasesAsync(IReadOnlyList<XrayTest> tests, string projectKey)
    {
        foreach (var test in tests)
        {
            var summary = test.TestInfo.Summary;
            ExistingTest? existing;
            try
            {
                existing = await graphql.FindTestAsync(projectKey, test.TestKey, summary);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not look up test '{summary}', it will be left to the import: {ex.Message}");
                continue;
            }

            if (existing is null)
            {
                if (test.TestKey is not null)
                {
                    Console.WriteLine($"Test {test.TestKey} not found in Xray, a new test will be created for '{summary}'");
                    test.TestKey = null;
                }

                continue;
            }

            test.TestKey = existing.Key;
            var steps = test.TestInfo.Steps ?? [];
            if (existing.TestType == ManualTestType && SameSteps(existing.Steps, steps))
            {
                Console.WriteLine($"Test {existing.Key} is up to date");
                continue;
            }

            try
            {
                if (existing.TestType != ManualTestType)
                {
                    await graphql.UpdateTestTypeAsync(existing.IssueId, ManualTestType);
                }

                await graphql.ReplaceTestStepsAsync(existing.IssueId, steps);
                Console.WriteLine($"Updated test {existing.Key}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not update steps of {existing.Key}: {ex.Message}");
            }
        }
    }

    internal static bool SameSteps(IReadOnlyList<XrayStepDefinition> existing, IReadOnlyList<XrayStepDefinition> steps) =>
        existing.Count == steps.Count && existing.Zip(steps).All(pair => Normalize(pair.First) == Normalize(pair.Second));

    private static (string, string, string) Normalize(XrayStepDefinition step) =>
        ((step.Action ?? "").Trim(), (step.Data ?? "").Trim(), (step.Result ?? "").Trim());

    // A failed link is reported but does not fail the upload.
    private async Task LinkIssueAsync(UploadOptions options, string executionKey)
    {
        if (options.IssueLinkType is null)
        {
            Console.WriteLine($"Execution is not linked to {options.IssueKey}: the issue link type (--ilt) is needed for that");
            return;
        }

        try
        {
            await jira.LinkIssueAsync(options.IssueLinkType, executionKey, options.IssueKey!);
            Console.WriteLine("Execution mapped correctly to issue");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Was not possible to link execution {executionKey} to {options.IssueKey}: {ex.Message}");
        }
    }

    // '<fieldId>,<value>' is set on every test of the test plan; failures are reported but do not fail the upload.
    private async Task SetCustomFieldsAsync(UploadOptions options)
    {
        var separator = options.JiraCustomFields!.IndexOf(',');
        if (separator < 0 || options.PlanKey is null)
        {
            Console.WriteLine("Jira custom fields are not set: they need '<fieldId>,<value>' and a test plan (--pn)");
            return;
        }

        var fieldId = options.JiraCustomFields[..separator].Trim();
        var value = options.JiraCustomFields[(separator + 1)..].Trim();

        IReadOnlyList<string> testKeys;
        try
        {
            testKeys = await graphql.GetTestKeysOfTestPlanAsync(options.PlanKey);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Was not possible to add jira custom fields to tests in plan, because error: {ex.Message}");
            return;
        }

        foreach (var testKey in testKeys)
        {
            try
            {
                await jira.SetCustomFieldAsync(fieldId, value, testKey);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Was not possible to add jira custom field {fieldId} to test {testKey}, because error: {ex.Message}");
            }
        }
    }
}
