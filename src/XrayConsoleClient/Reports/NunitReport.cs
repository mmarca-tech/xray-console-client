using System.Text.RegularExpressions;
using System.Xml.Linq;
using static XrayConsoleClient.ReportFiles;
using static XrayConsoleClient.XrayImportBuilder;

namespace XrayConsoleClient;

// NUnit3 xml report (a file, or a folder with xml files).
// [FeatureKey("X-1")] is linked as requirement of the test, [TestCaseKey("X-2")] is the test case to update,
// Category becomes labels and every logged http call becomes a step.
static partial class NunitReport
{
    private const string FeatureKeyProperty = "FeatureKey";
    private const string TestCaseKeyProperty = "TestCaseKey";
    private const string CategoryProperty = "Category";

    public static List<XrayTest> Read(string path, string projectKey)
    {
        Console.WriteLine("Generate xray json from nunit xml results");
        return ReadTestCases(path).Select(test => ToXrayTest(test, projectKey)).ToList();
    }

    internal static List<NunitTestCase> ReadTestCases(string path)
    {
        var files = Resolve(path, name => name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        if (files.Count == 0)
        {
            throw new XrayInputException($"No nunit xml files found in {path}");
        }

        var tests = new List<NunitTestCase>();
        foreach (var file in files)
        {
            var run = ReadXml(file);
            if (run.Name.LocalName != "test-run")
            {
                throw new XrayInputException($"{file} is not a NUnit3 report (no test-run element)");
            }

            Walk(run, new Dictionary<string, List<string>>(), tests);
        }

        return tests;
    }

    internal static XrayTest ToXrayTest(NunitTestCase test, string projectKey)
    {
        var status = test.Result switch
        {
            "Passed" => "PASSED",
            "Skipped" or "Inconclusive" => "TODO",
            _ => "FAILED"
        };

        // NUnit has no per step status, so when the test fails the failure is attached to the last step.
        var actualSteps = test.Steps.Select((step, index) =>
        {
            var failedHere = status == "FAILED" && index == test.Steps.Count - 1;
            return new XrayStepResult
            {
                Status = failedHere ? "FAILED" : "PASSED",
                Comment = step.Action,
                ActualResult = failedHere ? test.FailureMessage ?? "Step FAILED" : step.Result
            };
        }).ToList();

        return new XrayTest
        {
            Status = status,
            TestInfo = TestInfo(
                projectKey,
                ManualTestType,
                test.FullName,
                [.. test.Steps],
                [.. test.Categories],
                test.FeatureKey is null ? null : [test.FeatureKey]),
            TestKey = test.TestCaseKey,
            Start = FormatDate(test.Start),
            Finish = FormatDate(test.Stop),
            Comment = status == "FAILED" && test.FailureMessage is not null ? Truncate(test.FailureMessage) : null,
            Steps = actualSteps
        };
    }

    // Every logged http call becomes a step: action = "METHOD path", data = request body,
    // result = "status + response body". The tests log each exchange twice on non-2xx responses
    // (a second plain "Request:/Response:" block), only the "Request: Method:" / "Response: StatusCode:"
    // entries are used so nothing is duplicated. Headers (Authorization!) are never copied.
    internal static List<XrayStepDefinition> ExtractSteps(string output)
    {
        var steps = new List<StepBuilder>();
        StepBuilder? last = null;

        foreach (var rawEntry in LogEntrySplit().Split(output))
        {
            var entry = LogEntryHeader().Replace(rawEntry, "");
            Match match;
            if ((match = RequestLine().Match(entry)).Success)
            {
                last = new StepBuilder($"{match.Groups[1].Value} {PathOf(match.Groups[2].Value)}") { Target = StepTarget.Data };
                steps.Add(last);
            }
            else if ((match = ResponseLine().Match(entry)).Success && last is not null)
            {
                last.Result = $"{match.Groups[1].Value} {match.Groups[2].Value}";
                last.Target = StepTarget.Result;
            }
            else if (entry.StartsWith("Content: ", StringComparison.Ordinal) && last?.Target is not null)
            {
                var body = entry["Content: ".Length..].Trim();
                if (last.Target == StepTarget.Data)
                {
                    last.Data = body;
                }
                else
                {
                    last.Result += $"\n{body}";
                }

                last.Target = null;
            }
        }

        return steps.Select(step => new XrayStepDefinition(Redact(step.Action), Redact(step.Data), Redact(step.Result))).ToList();
    }

    // Safety net: never ship anything that looks like a JWT or bearer token.
    internal static string Redact(string text)
    {
        var redacted = BearerToken().Replace(text, "Bearer [REDACTED]");
        return Jwt().Replace(redacted, "[REDACTED_JWT]");
    }

    // FeatureKey, TestCaseKey and Category can be set on the fixture, the parameterized method or the test case,
    // the closest one wins.
    private static void Walk(XElement node, IReadOnlyDictionary<string, List<string>> inherited, List<NunitTestCase> tests)
    {
        foreach (var suite in Children(node, "test-suite"))
        {
            Walk(suite, Merge(inherited, ReadProperties(suite)), tests);
        }

        foreach (var testCase in Children(node, "test-case"))
        {
            tests.Add(ExtractTestCase(testCase, Merge(inherited, ReadProperties(testCase))));
        }
    }

    private static NunitTestCase ExtractTestCase(XElement testCase, IReadOnlyDictionary<string, List<string>> properties)
    {
        var name = (string?)testCase.Attribute("name") ?? "";
        return new NunitTestCase(
            name,
            (string?)testCase.Attribute("fullname") ?? name,
            (string?)testCase.Attribute("result"),
            (string?)testCase.Attribute("start-time"),
            (string?)testCase.Attribute("end-time"),
            ValidKey(properties.GetValueOrDefault(FeatureKeyProperty)?.FirstOrDefault()),
            ValidKey(properties.GetValueOrDefault(TestCaseKeyProperty)?.FirstOrDefault()),
            properties.GetValueOrDefault(CategoryProperty) ?? [],
            Child(Child(testCase, "failure"), "message")?.Value,
            ExtractSteps(Child(testCase, "output")?.Value ?? ""));
    }

    private static Dictionary<string, List<string>> ReadProperties(XElement node)
    {
        var properties = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var property in Children(Child(node, "properties"), "property"))
        {
            var name = (string?)property.Attribute("name");
            var value = (string?)property.Attribute("value");
            if (name is null || value is null)
            {
                continue;
            }

            if (!properties.TryGetValue(name, out var values))
            {
                properties[name] = values = [];
            }

            values.Add(value);
        }

        return properties;
    }

    private static Dictionary<string, List<string>> Merge(
        IReadOnlyDictionary<string, List<string>> parent,
        Dictionary<string, List<string>> child)
    {
        var merged = new Dictionary<string, List<string>>(parent, StringComparer.Ordinal);
        foreach (var (name, values) in child)
        {
            merged[name] = values;
        }

        return merged;
    }

    private static string? ValidKey(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var key = value.Trim().ToUpperInvariant();
        if (!JiraKey().IsMatch(key))
        {
            throw new XrayInputException($"'{value}' is not a valid Jira key");
        }

        return key;
    }

    private static string PathOf(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            ? parsed.AbsolutePath + parsed.Query
            : uri;

    [GeneratedRegex(@"^[A-Z][A-Z0-9_]+-\d+$")]
    private static partial Regex JiraKey();

    [GeneratedRegex(@"\r?\n(?=\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+ [+-]\d{2}:\d{2} \[)")]
    private static partial Regex LogEntrySplit();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+ [+-]\d{2}:\d{2} \[\w+\] ")]
    private static partial Regex LogEntryHeader();

    [GeneratedRegex(@"^Request: Method: (\w+), RequestUri: '([^']*)'")]
    private static partial Regex RequestLine();

    [GeneratedRegex(@"^Response: StatusCode: (\d+), ReasonPhrase: '([^']*)'")]
    private static partial Regex ResponseLine();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9._~+/=-]+")]
    private static partial Regex BearerToken();

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]*")]
    private static partial Regex Jwt();

    private enum StepTarget
    {
        Data,
        Result
    }

    private sealed class StepBuilder(string action)
    {
        public string Action { get; } = action;
        public string Data { get; set; } = "";
        public string Result { get; set; } = "";

        // Where the next "Content:" entry goes, null once it has been read.
        public StepTarget? Target { get; set; }
    }
}

sealed record NunitTestCase(
    string Name,
    string FullName,
    string? Result,
    string? Start,
    string? Stop,
    string? FeatureKey,
    string? TestCaseKey,
    IReadOnlyList<string> Categories,
    string? FailureMessage,
    IReadOnlyList<XrayStepDefinition> Steps);
