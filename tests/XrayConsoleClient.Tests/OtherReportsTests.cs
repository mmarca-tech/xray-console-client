using System.Text;

namespace XrayConsoleClient.Tests;

public class OtherReportsTests
{
    [Fact]
    public void Junit_creates_one_test_per_suite()
    {
        using var dir = new TempDir();
        var file = dir.Write("junit.xml", """
            <testsuites>
              <testsuite name="Passing" tests="2" failures="0" errors="0"><testcase name="a" /></testsuite>
              <testsuite name="Failing" tests="1" failures="1" errors="0"><testcase name="b"><failure>boom</failure></testcase></testsuite>
              <testsuite name="Erroring" tests="1" failures="0" errors="2" />
            </testsuites>
            """);

        var tests = JunitReport.Read(file, "QATG");

        Assert.Equal(["Passing", "Failing", "Erroring"], tests.Select(t => t.TestInfo.Summary));
        Assert.Equal(["PASSED", "FAILED", "FAILED"], tests.Select(t => t.Status));
        Assert.Empty(tests[0].TestInfo.Steps!);
        Assert.Empty(tests[0].Steps!);
    }

    [Fact]
    public void Junit_accepts_a_single_testsuite_root()
    {
        using var dir = new TempDir();
        var tests = JunitReport.Read(dir.Write("junit.xml", "<testsuite name=\"Only\" failures=\"0\" errors=\"0\" />"), "QATG");
        Assert.Equal("Only", Assert.Single(tests).TestInfo.Summary);
    }

    [Fact]
    public void Specflow_creates_cucumber_tests_per_scenario()
    {
        using var dir = new TempDir();
        var file = dir.Write("TestExecution.json", """
            { "ExecutionResults": [ { "ScenarioTitle": "Logs in", "Status": "OK" }, { "ScenarioTitle": "Logs out", "Status": "TestError" } ] }
            """);

        var tests = SpecflowReport.Read(file, "QATG");

        Assert.Equal(["PASSED", "FAILED"], tests.Select(t => t.Status));
        Assert.All(tests, t => Assert.Equal("Cucumber", t.TestInfo.Type));
        Assert.Equal("Logs out", tests[1].TestInfo.Summary);
    }

    [Fact]
    public void Cucumber_maps_steps_tags_and_embeddings()
    {
        using var dir = new TempDir();
        var file = dir.Write("cucumber.json", """
            [ { "elements": [ {
                "name": "Pays", "line": 12,
                "tags": [ { "name": "@TEST_QATG-5" }, { "name": "@REQ_QATG-1" }, { "name": "@smoke" } ],
                "steps": [
                  { "keyword": "Given ", "name": "a \"valid\" card", "result": { "status": "passed" },
                    "embeddings": [ { "data": "aGk=", "mime_type": "image/png" } ] },
                  { "keyword": "Then ", "name": "it pays", "result": { "status": "failed", "error_message": "declined" } }
                ] } ] } ]
            """);

        var test = Assert.Single(CucumberReport.Read(file, "QATG"));

        Assert.Equal("Pays - Example line: 12", test.TestInfo.Summary);
        Assert.Equal("QATG-5", test.TestKey);
        Assert.Equal(["QATG-1"], test.TestInfo.RequirementKeys!);
        Assert.Equal(["smoke"], test.TestInfo.Labels!);
        Assert.Equal("FAILED", test.Status);
        Assert.Equal(new XrayStepDefinition("Given a \"valid\" card", "", "action is correct"), test.TestInfo.Steps![0]);
        Assert.Equal("declined", test.Steps![1].ActualResult);
        Assert.Equal(new XrayEvidence("aGk=", "a_valid_card.png", "image/png"), Assert.Single(test.Steps![0].Evidences!));
        Assert.Null(test.Steps![1].Evidences);
    }

    [Fact]
    public void AllureXml_uploads_only_tests_with_test_id_and_collects_nested_step_attachments()
    {
        using var dir = new TempDir();
        File.WriteAllBytes(Path.Combine(dir.Path, "shot.png"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(dir.Path, "log.txt"), Encoding.UTF8.GetBytes("log"));
        dir.Write("suite-testsuite.xml", """
            <ns2:test-suite xmlns:ns2="urn:model.allure.qatools.yandex.ru" start="1" stop="2">
              <name>Suite</name><title>Suite</title>
              <test-cases>
                <test-case start="1761815700000" stop="1761815701000" status="passed">
                  <name>With id</name>
                  <steps>
                    <step start="1" stop="2" status="passed"><name>outer</name>
                      <attachments><attachment title="s" source="shot.png" type="image/png" /></attachments>
                      <steps><step start="1" stop="2" status="passed"><name>inner</name>
                        <attachments><attachment title="l" source="log.txt" type="text/plain" /></attachments>
                      </step></steps>
                    </step>
                  </steps>
                  <labels>
                    <label name="testId" value="https://jira.example/browse/QATG-7" />
                    <label name="issue" value="https://jira.example/browse/QATG-8" />
                  </labels>
                </test-case>
                <test-case start="1" stop="2" status="failed"><name>No id</name><labels><label name="feature" value="x" /></labels></test-case>
              </test-cases>
            </ns2:test-suite>
            """);

        var test = Assert.Single(AllureReport.ReadXml(dir.Path, "QATG"));

        Assert.Equal("QATG-7", test.TestKey);
        Assert.Equal(["QATG-8"], test.Defects!);
        Assert.Equal("passed", test.Status);
        Assert.Equal("2025-10-30T09:15:00+00:00", test.Start);
        Assert.Null(test.TestInfo.Steps);
        Assert.Equal(["shot.png", "log.txt"], test.Evidence!.Select(e => e.Filename));
        Assert.Equal(Convert.ToBase64String([1, 2, 3]), test.Evidence![0].Data);
    }

    [Fact]
    public void AllureJson_maps_labels_steps_and_attachments()
    {
        using var dir = new TempDir();
        File.WriteAllBytes(Path.Combine(dir.Path, "a1-attachment.png"), [9]);
        dir.Write("a1-result.json", """
            { "name": "Transfers", "fullName": "Bank.Transfers", "historyId": "h1", "status": "failed",
              "attachments": [ { "name": "screen", "source": "a1-attachment.png", "type": "image/png" } ],
              "steps": [ { "name": "open", "status": "passed" }, { "name": "send", "status": "failed" } ],
              "labels": [ { "name": "tag", "value": "TEST_QATG-3" }, { "name": "tag", "value": "REQ_QATG-2" },
                          { "name": "tag", "value": "regression" }, { "name": "story", "value": "QATG-9" } ] }
            """);
        dir.Write("a1-container.json", "{}");

        var test = Assert.Single(AllureReport.ReadJson(dir.Path, "QATG"));

        Assert.Equal("Transfers", test.TestInfo.Summary);
        Assert.Equal("FAILED", test.Status);
        Assert.Equal("QATG-3", test.TestKey);
        Assert.Equal(["QATG-2"], test.TestInfo.RequirementKeys!);
        Assert.Equal(["regression"], test.TestInfo.Labels!);
        Assert.Equal(["QATG-9"], test.Defects!);
        Assert.Equal(["Step passed OK", "Step FAILED"], test.Steps!.Select(s => s.ActualResult));
        Assert.Equal(new XrayEvidence(Convert.ToBase64String([9]), "screen", "image/png"), Assert.Single(test.Evidence!));
    }

    [Fact]
    public void Allure_needs_the_results_folder()
    {
        using var dir = new TempDir();
        var file = dir.Write("a-result.json", "{}");
        Assert.Throws<XrayInputException>(() => AllureReport.ReadJson(file, "QATG"));
    }

    [Theory]
    [InlineData("cucumber-specflow")]
    [InlineData("cucumber-json")]
    [InlineData("junit-xml")]
    [InlineData("nunit-xml")]
    [InlineData("allure-xml")]
    [InlineData("allure-json")]
    public void ReadReport_dispatches_every_import_test_type(string testType)
    {
        using var dir = new TempDir();
        var missing = Path.Combine(dir.Path, "missing");
        var error = Assert.Throws<XrayInputException>(() => ExecutionUploader.ReadReport(testType, missing, "QATG"));
        Assert.Contains("missing", error.Message);
    }
}
