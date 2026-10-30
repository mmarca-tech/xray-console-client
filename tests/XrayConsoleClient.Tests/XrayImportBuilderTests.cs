using System.Text.Json.Nodes;

namespace XrayConsoleClient.Tests;

public class XrayImportBuilderTests
{
    [Fact]
    public void CreateImport_uses_generic_texts_and_leaves_out_what_was_not_passed()
    {
        var import = XrayImportBuilder.CreateImport([], Options.Parse());

        var json = JsonNode.Parse(XrayJson.Serialize(import))!.AsObject();
        Assert.Equal(["tests", "info"], json.Select(p => p.Key));
        var info = json["info"]!.AsObject();
        Assert.Equal(["project", "summary", "description"], info.Select(p => p.Key));
        Assert.Equal(XrayImportBuilder.DefaultSummary, (string?)info["summary"]);
    }

    [Fact]
    public void CreateImport_passes_execution_plan_version_and_trimmed_environments()
    {
        var import = XrayImportBuilder.CreateImport([], Options.Parse(
            "--ek", "QATG-4", "--pn", "QATG-3", "--rv", "2026.10.1", "--e", "test, staging,", "--s", "Nightly"));

        Assert.Equal("QATG-4", import.TestExecutionKey);
        Assert.Equal("QATG-3", import.Info!.TestPlanKey);
        Assert.Equal("2026.10.1", import.Info.Version);
        Assert.Equal(["test", "staging"], import.Info.TestEnvironments!);
        Assert.Equal("Nightly", import.Info.Summary);
    }

    [Fact]
    public void Test_json_is_camel_case_and_keeps_empty_lists()
    {
        var test = new XrayTest
        {
            Status = "PASSED",
            TestInfo = XrayImportBuilder.TestInfo("QATG", "Manual", "Café <ü>", [], []),
            Steps = []
        };

        var json = XrayJson.Serialize(test);

        Assert.Equal("""{"status":"PASSED","testInfo":{"projectKey":"QATG","summary":"Café <ü>","type":"Manual","steps":[],"labels":[]},"steps":[]}""", json);
    }

    [Fact]
    public void TestInfo_truncates_summary_to_255_characters()
    {
        Assert.Equal(255, XrayImportBuilder.TestInfo("QATG", "Manual", new string('a', 300)).Summary.Length);
    }

    [Theory]
    [InlineData("2026-10-30 09:15:00.987Z", "2026-10-30T09:15:00+00:00")]
    [InlineData("2026-10-30T11:15:00+02:00", "2026-10-30T09:15:00+00:00")]
    [InlineData("not a date", null)]
    [InlineData(null, null)]
    public void FormatDate_converts_to_utc_seconds(string? value, string? expected)
    {
        Assert.Equal(expected, XrayImportBuilder.FormatDate(value));
    }
}
