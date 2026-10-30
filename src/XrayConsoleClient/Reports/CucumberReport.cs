using System.Text.Json.Nodes;
using static XrayConsoleClient.ReportFiles;
using static XrayConsoleClient.XrayImportBuilder;
using static XrayConsoleClient.XrayJson;

namespace XrayConsoleClient;

// Cucumber json (a file, or a folder with json files): one Xray test per scenario.
// Tags: @TEST_<key> is the test to update, @REQ_<key> a requirement, any other tag a label.
static class CucumberReport
{
    public static List<XrayTest> Read(string path, string projectKey)
    {
        Console.WriteLine("Generate xray json from cucumber json results");
        var tests = new List<XrayTest>();
        foreach (var file in Resolve(path, name => name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var feature in Items(ReadJson(file)))
            {
                foreach (var scenario in Items(feature?["elements"]))
                {
                    if (scenario is not null)
                    {
                        tests.Add(ToXrayTest(scenario, projectKey));
                    }
                }
            }
        }

        return tests;
    }

    internal static XrayTest ToXrayTest(JsonNode scenario, string projectKey)
    {
        var expectedSteps = new List<XrayStepDefinition>();
        var actualSteps = new List<XrayStepResult>();
        foreach (var step in Items(scenario["steps"]))
        {
            var text = Text(step?["keyword"]) + Text(step?["name"]);
            expectedSteps.Add(new XrayStepDefinition(text, "", "action is correct"));

            var evidences = Items(step?["embeddings"])
                .Select(embedding => Evidence(Text(step?["name"]) ?? "", embedding))
                .ToList();

            actualSteps.Add(new XrayStepResult
            {
                Status = Text(step?["result"]?["status"]) == "passed" ? "PASSED" : "FAILED",
                Comment = text,
                ActualResult = Text(step?["result"]?["error_message"]) ?? "Step passed OK",
                Evidences = evidences.Count > 0 ? evidences : null
            });
        }

        string? testKey = null;
        var requirementKeys = new List<string>();
        var labels = new List<string>();
        foreach (var tag in Items(scenario["tags"]))
        {
            var name = Text(tag?["name"]) ?? "";
            if (name.Contains("TEST", StringComparison.Ordinal))
            {
                testKey = name.Replace("@TEST_", "", StringComparison.Ordinal);
            }
            else if (name.Contains("REQ", StringComparison.Ordinal))
            {
                requirementKeys.Add(name.Replace("@REQ_", "", StringComparison.Ordinal));
            }
            else
            {
                labels.Add(name.Replace("@", "", StringComparison.Ordinal));
            }
        }

        return new XrayTest
        {
            Status = actualSteps.Any(step => step.Status == "FAILED") ? "FAILED" : "PASSED",
            TestInfo = TestInfo(
                projectKey,
                ManualTestType,
                $"{Text(scenario["name"])} - Example line: {Text(scenario["line"])}",
                expectedSteps,
                labels,
                requirementKeys.Count > 0 ? requirementKeys : null),
            TestKey = testKey,
            Steps = actualSteps
        };
    }

    private static XrayEvidence Evidence(string stepName, JsonNode? embedding)
    {
        var mimeType = Text(embedding?["mime_type"]) ?? "";
        var extension = mimeType.Contains('/') ? mimeType[(mimeType.IndexOf('/') + 1)..] : mimeType;
        var fileName = stepName.Replace(' ', '_').Replace("\"", "", StringComparison.Ordinal);
        return new XrayEvidence(Text(embedding?["data"]) ?? "", $"{fileName}.{extension}", mimeType);
    }
}
