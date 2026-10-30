using System.Net;
using System.Text.Json.Nodes;

namespace XrayConsoleClient.Tests;

public class ExecutionUploaderTests
{
    private static XrayTest NunitTest(string summary, string? key = null, params XrayStepDefinition[] steps) => new()
    {
        Status = "PASSED",
        TestInfo = XrayImportBuilder.TestInfo("QATG", "Manual", summary, [.. steps]),
        TestKey = key
    };

    private static async Task<(ExecutionUploader Uploader, FakeHttpHandler Xray, FakeHttpHandler Jira)> Create(
        Func<RecordedRequest, HttpResponseMessage> xrayResponder,
        Func<RecordedRequest, HttpResponseMessage>? jiraResponder = null)
    {
        var xrayHandler = new FakeHttpHandler(request => request.Url.EndsWith("/authenticate")
            ? FakeHttpHandler.Json("\"xray-token\"")
            : xrayResponder(request));
        var jiraHandler = new FakeHttpHandler(jiraResponder ?? (_ => FakeHttpHandler.Json("{}")));
        var xrayHttp = new HttpClient(xrayHandler);
        var rest = new XrayRestClient(xrayHttp, "https://xray.example/api/v2");
        await rest.AuthenticateAsync("id", "secret");
        xrayHandler.Requests.Clear();
        var graphql = new XrayGraphqlClient(xrayHttp, "https://xray.example/api/v2/graphql", rest.Token);
        var jira = new JiraRestClient(new HttpClient(jiraHandler), "https://jira.example", "jira-token");
        return (new ExecutionUploader(rest, graphql, jira, _ => Task.CompletedTask), xrayHandler, jiraHandler);
    }

    private static string? Query(RecordedRequest request) => (string?)JsonNode.Parse(request.Body)?["query"];

    [Fact]
    public async Task ImportAsync_sends_tranches_of_50_and_adds_them_to_the_first_execution()
    {
        var (uploader, xray, _) = await Create(_ => FakeHttpHandler.Json("""{"id":"1","key":"QATG-100"}"""));
        var tests = Enumerable.Range(0, 120).Select(i => NunitTest($"test {i}")).ToList();

        var key = await uploader.ImportAsync(XrayImportBuilder.CreateImport(tests, Options.Parse("--ek", "QATG-99")));

        Assert.Equal("QATG-100", key);
        var bodies = xray.Requests.Select(r => JsonNode.Parse(r.Body)!).ToList();
        Assert.Equal([50, 50, 20], bodies.Select(b => b["tests"]!.AsArray().Count));
        Assert.Equal(["QATG-99", "QATG-100", "QATG-100"], bodies.Select(b => (string?)b["testExecutionKey"]));
        Assert.All(xray.Requests, r => Assert.Equal("https://xray.example/api/v2/import/execution", r.Url));
        Assert.All(xray.Requests, r => Assert.Equal("Bearer xray-token", r.Authorization));
    }

    [Fact]
    public async Task SyncTestCases_replaces_steps_of_existing_test_found_by_summary()
    {
        var (uploader, xray, _) = await Create(request => Query(request) switch
        {
            { } q when q.Contains("getTests") => FakeHttpHandler.Json("""
                {"data":{"getTests":{"results":[
                  {"issueId":"1","testType":{"name":"Manual"},"steps":[],"jira":{"key":"QATG-1","summary":"Otp.Sends partially"}},
                  {"issueId":"2","testType":{"name":"Generic"},"steps":[],"jira":{"key":"QATG-2","summary":"Otp.Sends"}}]}}}
                """),
            _ => FakeHttpHandler.Json("""{"data":{}}""")
        });
        var test = NunitTest("Otp.Sends", null, new XrayStepDefinition("GET /otp", "", "200 OK"));

        await uploader.SyncTestCasesAsync([test], "QATG");

        Assert.Equal("QATG-2", test.TestKey);
        var queries = xray.Requests.Select(Query).ToList();
        Assert.Contains("getTests", queries[0]);
        Assert.Contains("updateTestType", queries[1]);
        Assert.Contains("removeAllTestSteps", queries[2]);
        Assert.Contains("addTestStep", queries[3]);
        Assert.Equal(4, queries.Count);
        Assert.Equal("GET /otp", (string?)JsonNode.Parse(xray.Requests[3].Body)!["variables"]!["step"]!["action"]);
    }

    [Fact]
    public async Task SyncTestCases_leaves_up_to_date_test_alone()
    {
        var (uploader, xray, _) = await Create(_ => FakeHttpHandler.Json("""
            {"data":{"getTests":{"results":[
              {"issueId":"1","testType":{"name":"Manual"},"steps":[{"action":"GET /otp ","data":null,"result":"200 OK"}],"jira":{"key":"QATG-1","summary":"Otp.Sends"}}]}}}
            """));

        await uploader.SyncTestCasesAsync([NunitTest("Otp.Sends", "QATG-1", new XrayStepDefinition("GET /otp", "", "200 OK"))], "QATG");

        Assert.Single(xray.Requests);
        Assert.Contains("key = \\\"QATG-1\\\"", xray.Requests[0].Body);
    }

    [Fact]
    public async Task SyncTestCases_drops_unknown_key_so_the_import_creates_the_test()
    {
        var (uploader, _, _) = await Create(_ => FakeHttpHandler.Json("""{"data":{"getTests":{"results":[]}}}"""));
        var test = NunitTest("Otp.Sends", "QATG-404");

        await uploader.SyncTestCasesAsync([test], "QATG");

        Assert.Null(test.TestKey);
    }

    [Fact]
    public async Task SyncTestCases_keeps_going_when_xray_fails()
    {
        var (uploader, _, _) = await Create(_ => FakeHttpHandler.Json("""{"errors":[{"message":"nope"}]}"""));
        var test = NunitTest("Otp.Sends", "QATG-1");

        await uploader.SyncTestCasesAsync([test, NunitTest("Other")], "QATG");

        Assert.Equal("QATG-1", test.TestKey);
    }

    [Fact]
    public async Task RunAsync_imports_links_issue_and_sets_custom_fields()
    {
        using var dir = new TempDir();
        var file = dir.Write("results.xml", """
            <test-run><test-case name="A" fullname="Suite.A" result="Passed" /></test-run>
            """);
        var (uploader, xray, jira) = await Create(request => request.Url.EndsWith("/import/execution")
            ? FakeHttpHandler.Json("""{"key":"QATG-100"}""")
            : Query(request) switch
            {
                { } q when q.Contains("getTests(") => FakeHttpHandler.Json("""{"data":{"getTests":{"results":[]}}}"""),
                { } q when q.Contains("getTestPlans") => FakeHttpHandler.Json("""{"data":{"getTestPlans":{"results":[{"issueId":"77"}]}}}"""),
                _ => FakeHttpHandler.Json("""{"data":{"getTestPlan":{"tests":{"results":[{"jira":{"key":"QATG-5"}},{"jira":{"key":"QATG-6"}}]}}}}""")
            });

        await uploader.RunAsync(Options.Parse(
            "--f", file, "--pn", "QATG-3", "--i", "CLIENT-1", "--ilt", "10618", "--jcf", "customfield_1, Team A"));

        var import = JsonNode.Parse(xray.Requests.Single(r => r.Url.EndsWith("/import/execution")).Body)!;
        Assert.Equal("Suite.A", (string?)import["tests"]![0]!["testInfo"]!["summary"]);
        Assert.Equal("QATG-3", (string?)import["info"]!["testPlanKey"]);

        Assert.Equal(3, jira.Requests.Count);
        var link = JsonNode.Parse(jira.Requests[0].Body)!;
        Assert.Equal("https://jira.example/rest/api/2/issueLink", jira.Requests[0].Url);
        Assert.Equal("10618", (string?)link["type"]!["id"]);
        Assert.Equal("QATG-100", (string?)link["inwardIssue"]!["key"]);
        Assert.Equal("CLIENT-1", (string?)link["outwardIssue"]!["key"]);
        Assert.Equal("Basic jira-token", jira.Requests[0].Authorization);
        Assert.Equal(["https://jira.example/rest/api/3/issue/QATG-5", "https://jira.example/rest/api/3/issue/QATG-6"], jira.Requests.Skip(1).Select(r => r.Url));
        Assert.Equal("Team A", (string?)JsonNode.Parse(jira.Requests[1].Body)!["fields"]!["customfield_1"]);
    }

    [Fact]
    public async Task RunAsync_does_not_fail_when_linking_the_issue_fails()
    {
        using var dir = new TempDir();
        var file = dir.Write("junit.xml", "<testsuite name=\"S\" failures=\"0\" errors=\"0\" />");
        var (uploader, _, jira) = await Create(
            _ => FakeHttpHandler.Json("""{"key":"QATG-100"}"""),
            _ => FakeHttpHandler.Json("""{"errors":{}}""", HttpStatusCode.BadRequest));

        await uploader.RunAsync(Options.Parse("--tt", "junit-xml", "--f", file, "--i", "CLIENT-1", "--ilt", "10618"));

        Assert.Single(jira.Requests);
    }

    [Fact]
    public async Task RunAsync_fails_when_the_import_fails()
    {
        using var dir = new TempDir();
        var file = dir.Write("junit.xml", "<testsuite name=\"S\" failures=\"0\" errors=\"0\" />");
        var (uploader, _, _) = await Create(_ => FakeHttpHandler.Json("""{"error":"bad"}""", HttpStatusCode.BadRequest));

        var error = await Assert.ThrowsAsync<XrayClientException>(() => uploader.RunAsync(Options.Parse("--tt", "junit-xml", "--f", file)));
        Assert.Equal(400, error.StatusCode);
    }

    [Fact]
    public async Task RunAsync_nunit_sends_the_report_to_the_xray_nunit_import()
    {
        using var dir = new TempDir();
        var file = dir.Write("results.xml", "<test-run />");
        var (uploader, xray, _) = await Create(_ => FakeHttpHandler.Json("{}"));

        await uploader.RunAsync(Options.Parse("--tt", "nunit", "--f", file, "--pn", "QATG-3"));

        var request = Assert.Single(xray.Requests);
        Assert.Equal("https://xray.example/api/v2/import/execution/nunit?projectKey=QATG&testPlanKey=QATG-3", request.Url);
        Assert.Equal("<test-run />", request.Body);
    }

    [Fact]
    public void SameSteps_compares_trimmed_texts_in_order()
    {
        XrayStepDefinition[] steps = [new("a", "b", "c"), new("d", "", "f")];
        Assert.True(ExecutionUploader.SameSteps([new(" a", "b ", "c"), new("d", "", "f")], steps));
        Assert.False(ExecutionUploader.SameSteps([new("d", "", "f"), new("a", "b", "c")], steps));
        Assert.False(ExecutionUploader.SameSteps([new("a", "b", "c")], steps));
    }

    [Fact]
    public void FindTestJql_quotes_summary_as_a_phrase()
    {
        Assert.Equal(
            """project = "QATG" AND issuetype = Test AND summary ~ "\"Otp.Sends(\\\"x\\\")\"" """.TrimEnd(),
            XrayGraphqlClient.FindTestJql("QATG", null, "Otp.Sends(\"x\")"));
        Assert.Equal("key = \"QATG-1\"", XrayGraphqlClient.FindTestJql("QATG", "QATG-1", "ignored"));
    }
}
