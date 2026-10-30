using System.Text.Json.Nodes;
using System.Xml.Linq;
using static XrayConsoleClient.ReportFiles;
using static XrayConsoleClient.XrayImportBuilder;
using static XrayConsoleClient.XrayJson;

namespace XrayConsoleClient;

static class AllureReport
{
    // Allure 1 xml results folder: only the test cases with a testId label are uploaded (as that test),
    // with the "issue" label as defect and the step attachments as evidence.
    public static List<XrayTest> ReadXml(string resultsDir, string projectKey)
    {
        Console.WriteLine("Generate xray json from allure xml results");
        var dir = RequireDirectory(resultsDir);
        var tests = new List<XrayTest>();
        foreach (var file in Resolve(dir, name => name.Contains(".xml", StringComparison.Ordinal)))
        {
            var suite = ReportFiles.ReadXml(file);
            if (suite.Name.LocalName != "test-suite")
            {
                throw new XrayInputException($"{file} is not an Allure xml report (no test-suite element)");
            }

            foreach (var testCase in Children(Child(suite, "test-cases"), "test-case"))
            {
                var testId = LastSegment(Label(testCase, "testId"));
                if (string.IsNullOrEmpty(testId))
                {
                    continue;
                }

                var issueId = LastSegment(Label(testCase, "issue"));
                tests.Add(new XrayTest
                {
                    Status = (string?)testCase.Attribute("status") ?? "",
                    TestInfo = TestInfo(projectKey, ManualTestType, Child(testCase, "name")?.Value ?? ""),
                    TestKey = testId,
                    Start = FormatEpochMilliseconds((string?)testCase.Attribute("start")),
                    Finish = FormatEpochMilliseconds((string?)testCase.Attribute("stop")),
                    Defects = string.IsNullOrEmpty(issueId) ? [] : [issueId],
                    Evidence = StepAttachments(testCase, dir).ToList()
                });
            }
        }

        return tests;
    }

    // Allure 2 json results folder (*-result.json): tags TEST_<key> is the test to update, REQ_<key> a requirement,
    // other tags labels; "story" labels are defects; the test attachments are evidence.
    public static List<XrayTest> ReadJson(string resultsDir, string projectKey)
    {
        Console.WriteLine("Generate xray json from allure json results");
        var dir = RequireDirectory(resultsDir);
        return Resolve(dir, name => name.Contains("result", StringComparison.Ordinal))
            .Select(file => ReportFiles.ReadJson(file))
            .OfType<JsonNode>()
            .Select(result => FromJsonResult(result, dir, projectKey))
            .ToList();
    }

    internal static XrayTest FromJsonResult(JsonNode result, string resultsDir, string projectKey)
    {
        var evidence = Items(result["attachments"])
            .Where(attachment => !string.IsNullOrEmpty(Text(attachment?["source"])))
            .Select(attachment => new XrayEvidence(
                Base64(Path.Combine(resultsDir, Text(attachment?["source"])!)),
                Text(attachment?["name"]) ?? "",
                Text(attachment?["type"]) ?? ""))
            .ToList();

        var expectedSteps = new List<XrayStepDefinition>();
        var actualSteps = new List<XrayStepResult>();
        foreach (var step in Items(result["steps"]))
        {
            var name = Truncate(Text(step?["name"]) ?? "");
            var passed = Text(step?["status"]) == "passed";
            expectedSteps.Add(new XrayStepDefinition(name, "", "action is correct"));
            actualSteps.Add(new XrayStepResult
            {
                Status = passed ? "PASSED" : "FAILED",
                Comment = name,
                ActualResult = passed ? "Step passed OK" : "Step FAILED"
            });
        }

        string? testKey = null;
        var requirementKeys = new List<string>();
        var labels = new List<string>();
        var defects = new List<string>();
        foreach (var label in Items(result["labels"]))
        {
            var labelName = Text(label?["name"]);
            var value = Text(label?["value"]) ?? "";
            if (labelName == "tag")
            {
                if (value.StartsWith("TEST", StringComparison.Ordinal))
                {
                    testKey = value.Replace("TEST_", "", StringComparison.Ordinal);
                }
                else if (value.StartsWith("REQ", StringComparison.Ordinal))
                {
                    requirementKeys.Add(value.Replace("REQ_", "", StringComparison.Ordinal));
                }
                else
                {
                    labels.Add(value);
                }
            }

            if (labelName == "story")
            {
                defects.Add(value);
            }
        }

        var testName = Text(result["name"]);
        return new XrayTest
        {
            Status = Text(result["status"]) == "passed" ? "PASSED" : "FAILED",
            TestInfo = TestInfo(
                projectKey,
                ManualTestType,
                string.IsNullOrEmpty(testName) ? Text(result["historyId"]) ?? "" : testName,
                expectedSteps,
                labels,
                requirementKeys.Count > 0 ? requirementKeys : null),
            TestKey = testKey,
            Steps = actualSteps,
            Defects = defects,
            Evidence = evidence
        };
    }

    // Attachments of every step and nested step, in order.
    private static IEnumerable<XrayEvidence> StepAttachments(XElement parent, string resultsDir)
    {
        foreach (var step in Children(Child(parent, "steps"), "step"))
        {
            foreach (var attachment in Children(Child(step, "attachments"), "attachment"))
            {
                var source = (string?)attachment.Attribute("source");
                var type = (string?)attachment.Attribute("type");
                if (source is not null && type is not null)
                {
                    yield return new XrayEvidence(Base64(Path.Combine(resultsDir, source)), source, type);
                }
            }

            foreach (var evidence in StepAttachments(step, resultsDir))
            {
                yield return evidence;
            }
        }
    }

    private static string? Label(XElement testCase, string name) =>
        Children(Child(testCase, "labels"), "label")
            .FirstOrDefault(label => (string?)label.Attribute("name") == name)
            ?.Attribute("value")?.Value;

    private static string? LastSegment(string? value) => value?.Split('/')[^1];
}
