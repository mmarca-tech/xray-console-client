using static XrayConsoleClient.ReportFiles;
using static XrayConsoleClient.XrayImportBuilder;
using static XrayConsoleClient.XrayJson;

namespace XrayConsoleClient;

// SpecFlow TestExecution.json (a file, or a folder with json files): one Cucumber test per scenario,
// no steps, labels or evidence.
static class SpecflowReport
{
    public static List<XrayTest> Read(string path, string projectKey)
    {
        Console.WriteLine("Generate xray json from specflow results");
        var tests = new List<XrayTest>();
        foreach (var file in Resolve(path, name => name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            tests.AddRange(Items(ReadJson(file)?["ExecutionResults"]).Select(result => new XrayTest
            {
                Status = Text(result?["Status"]) == "OK" ? "PASSED" : "FAILED",
                TestInfo = TestInfo(projectKey, CucumberTestType, Text(result?["ScenarioTitle"]) ?? "", [], []),
                Steps = []
            }));
        }

        return tests;
    }
}
