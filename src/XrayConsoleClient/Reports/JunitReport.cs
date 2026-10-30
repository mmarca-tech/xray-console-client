using System.Xml.Linq;
using static XrayConsoleClient.ReportFiles;
using static XrayConsoleClient.XrayImportBuilder;

namespace XrayConsoleClient;

// JUnit xml (a file, or a folder with xml files): one Xray test per <testsuite>, failed when the suite
// reports failures or errors. No steps, labels or evidence.
static class JunitReport
{
    public static List<XrayTest> Read(string path, string projectKey)
    {
        Console.WriteLine("Generate xray json from junit xml results");
        var tests = new List<XrayTest>();
        foreach (var file in Resolve(path, name => name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var root = ReadXml(file);
            var suites = root.Name.LocalName switch
            {
                "testsuites" => Children(root, "testsuite"),
                "testsuite" => [root],
                _ => throw new XrayInputException($"{file} is not a JUnit report (no testsuites element)")
            };

            tests.AddRange(suites.Select(suite => new XrayTest
            {
                Status = HasFailures(suite) ? "FAILED" : "PASSED",
                TestInfo = TestInfo(projectKey, ManualTestType, (string?)suite.Attribute("name") ?? "", [], []),
                Steps = []
            }));
        }

        return tests;
    }

    private static bool HasFailures(XElement suite) => NonZero(suite.Attribute("failures")) || NonZero(suite.Attribute("errors"));

    private static bool NonZero(XAttribute? count) => count is not null && count.Value.Trim() != "0";
}
