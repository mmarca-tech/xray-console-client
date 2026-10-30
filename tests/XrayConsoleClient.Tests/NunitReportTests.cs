namespace XrayConsoleClient.Tests;

public class NunitReportTests
{
    private const string Output = """
        2026-10-30 09:15:00.123 +00:00 [INF] Request: Method: POST, RequestUri: 'https://api.example/v1/otp?channel=sms', Version: 1.1
        2026-10-30 09:15:00.124 +00:00 [INF] Content: {"iban":"DE00"}
        2026-10-30 09:15:00.200 +00:00 [INF] Response: StatusCode: 400, ReasonPhrase: 'Bad Request', Version: 1.1
        Headers: Authorization: Bearer abc.def.ghi
        2026-10-30 09:15:00.201 +00:00 [INF] Content: {"error":"invalid"}
        2026-10-30 09:15:00.202 +00:00 [INF] Request:
        2026-10-30 09:15:00.203 +00:00 [INF] Content: duplicated plain block
        2026-10-30 09:15:00.300 +00:00 [INF] Request: Method: GET, RequestUri: 'https://api.example/v1/otp/1', Version: 1.1
        2026-10-30 09:15:00.400 +00:00 [INF] Response: StatusCode: 200, ReasonPhrase: 'OK', Version: 1.1
        2026-10-30 09:15:00.401 +00:00 [INF] Content: {"token":"Bearer secret-token"}
        """;

    private static string Report(string output = Output) => $"""
        <test-run>
          <test-suite type="TestFixture" name="OtpTests">
            <properties>
              <property name="FeatureKey" value="qatg-10" />
              <property name="Category" value="smoke" />
            </properties>
            <test-case name="Sends_otp" fullname="Otp.OtpTests.Sends_otp" result="Failed"
                       start-time="2026-10-30 09:15:00.123Z" end-time="2026-10-30T09:15:01.900Z">
              <properties>
                <property name="TestCaseKey" value="QATG-20" />
              </properties>
              <failure><message><![CDATA[Expected 200]]></message></failure>
              <output><![CDATA[{output}]]></output>
            </test-case>
            <test-suite type="ParameterizedMethod" name="Rejects">
              <properties>
                <property name="Category" value="negative" />
                <property name="Category" value="api" />
              </properties>
              <test-case name="Rejects(1)" fullname="Otp.OtpTests.Rejects(1)" result="Skipped" />
            </test-suite>
          </test-suite>
          <test-case name="Root" fullname="Root" result="Passed" />
        </test-run>
        """;

    [Fact]
    public void ReadTestCases_inherits_properties_and_the_closest_one_wins()
    {
        using var dir = new TempDir();
        var tests = NunitReport.ReadTestCases(dir.Write("results.xml", Report()));

        Assert.Equal(["Otp.OtpTests.Rejects(1)", "Otp.OtpTests.Sends_otp", "Root"], tests.Select(t => t.FullName).Order());

        var sends = tests.Single(t => t.Name == "Sends_otp");
        Assert.Equal("QATG-10", sends.FeatureKey);
        Assert.Equal("QATG-20", sends.TestCaseKey);
        Assert.Equal(["smoke"], sends.Categories);
        Assert.Equal("Expected 200", sends.FailureMessage);

        var rejects = tests.Single(t => t.Name == "Rejects(1)");
        Assert.Equal("QATG-10", rejects.FeatureKey);
        Assert.Null(rejects.TestCaseKey);
        Assert.Equal(["negative", "api"], rejects.Categories);

        Assert.Null(tests.Single(t => t.Name == "Root").FeatureKey);
    }

    [Fact]
    public void ExtractSteps_turns_each_logged_call_into_a_step_without_headers_or_tokens()
    {
        var steps = NunitReport.ExtractSteps(Output.ReplaceLineEndings("\n"));

        Assert.Equal(2, steps.Count);
        Assert.Equal(new XrayStepDefinition("POST /v1/otp?channel=sms", "{\"iban\":\"DE00\"}", "400 Bad Request\n{\"error\":\"invalid\"}"), steps[0]);
        Assert.Equal(new XrayStepDefinition("GET /v1/otp/1", "", "200 OK\n{\"token\":\"Bearer [REDACTED]\"}"), steps[1]);
        Assert.DoesNotContain(steps, step => step.Result.Contains("abc.def.ghi") || step.Data.Contains("duplicated"));
    }

    [Fact]
    public void ExtractSteps_handles_windows_line_endings()
    {
        Assert.Equal(2, NunitReport.ExtractSteps(Output.ReplaceLineEndings("\r\n")).Count);
    }

    [Fact]
    public void Redact_removes_jwts()
    {
        var jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.signature";
        Assert.Equal("token [REDACTED_JWT] end", NunitReport.Redact($"token {jwt} end"));
    }

    [Fact]
    public void Read_maps_status_steps_and_failure_to_xray()
    {
        using var dir = new TempDir();
        var tests = NunitReport.Read(dir.Write("results.xml", Report()), "QATG");

        var failed = tests.Single(t => t.TestInfo.Summary == "Otp.OtpTests.Sends_otp");
        Assert.Equal("FAILED", failed.Status);
        Assert.Equal("QATG-20", failed.TestKey);
        Assert.Equal(["QATG-10"], failed.TestInfo.RequirementKeys!);
        Assert.Equal(["smoke"], failed.TestInfo.Labels!);
        Assert.Equal("Manual", failed.TestInfo.Type);
        Assert.Equal("2026-10-30T09:15:00+00:00", failed.Start);
        Assert.Equal("2026-10-30T09:15:01+00:00", failed.Finish);
        Assert.Equal("Expected 200", failed.Comment);
        Assert.Equal(["PASSED", "FAILED"], failed.Steps!.Select(s => s.Status));
        Assert.Equal("Expected 200", failed.Steps![1].ActualResult);

        Assert.Equal("TODO", tests.Single(t => t.TestInfo.Summary == "Otp.OtpTests.Rejects(1)").Status);
        var passed = tests.Single(t => t.TestInfo.Summary == "Root");
        Assert.Equal("PASSED", passed.Status);
        Assert.Null(passed.Comment);
        Assert.Null(passed.TestInfo.RequirementKeys);
    }

    [Fact]
    public void ReadTestCases_reads_every_xml_file_of_a_folder()
    {
        using var dir = new TempDir();
        dir.Write("a.xml", Report());
        dir.Write("b.XML", Report());
        dir.Write("notes.txt", "not a report");

        Assert.Equal(6, NunitReport.ReadTestCases(dir.Path).Count);
    }

    [Fact]
    public void ReadTestCases_rejects_invalid_jira_keys()
    {
        using var dir = new TempDir();
        var file = dir.Write("results.xml", Report().Replace("QATG-20", "not a key"));

        var error = Assert.Throws<XrayInputException>(() => NunitReport.ReadTestCases(file));
        Assert.Contains("'not a key' is not a valid Jira key", error.Message);
    }

    [Fact]
    public void ReadTestCases_rejects_files_that_are_not_nunit3_reports()
    {
        using var dir = new TempDir();
        Assert.Throws<XrayInputException>(() => NunitReport.ReadTestCases(dir.Write("junit.xml", "<testsuites />")));
    }

    [Fact]
    public void ReadTestCases_rejects_empty_folder_and_missing_path()
    {
        using var dir = new TempDir();
        Assert.Throws<XrayInputException>(() => NunitReport.ReadTestCases(dir.Path));
        Assert.Throws<XrayInputException>(() => NunitReport.ReadTestCases(Path.Combine(dir.Path, "missing.xml")));
    }
}
