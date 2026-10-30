using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace XrayConsoleClient;

sealed class XrayGraphqlClient(HttpClient http, string url, string token)
{
    public async Task<JsonNode?> QueryAsync(string query, JsonObject? variables = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = XrayJson.Content(new JsonObject { ["query"] = query, ["variables"] = variables ?? new JsonObject() })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var body = await HttpCalls.SendAsync(http, request, "Xray GraphQL request failed");
        var response = JsonNode.Parse(body) as JsonObject;
        if (response?["errors"] is { } errors)
        {
            throw new XrayClientException($"Xray GraphQL request failed: {errors.ToJsonString()}");
        }

        return response?["data"];
    }

    // The test with that key, or else the test of the project whose summary is exactly the given one.
    public async Task<ExistingTest?> FindTestAsync(string projectKey, string? key, string summary)
    {
        var data = await QueryAsync(
            "query($jql: String, $limit: Int!) { getTests(jql: $jql, limit: $limit) { results { issueId testType { name } steps { action data result } jira(fields: [\"key\", \"summary\"]) } } }",
            new JsonObject { ["jql"] = FindTestJql(projectKey, key, summary), ["limit"] = key is null ? 100 : 1 });

        var tests = XrayJson.Items(data?["getTests"]?["results"]).Select(ToExistingTest).ToList();
        return key is null ? tests.FirstOrDefault(test => test.Summary == summary) : tests.FirstOrDefault();
    }

    // Replaces all the steps of a manual test with the given ones.
    public async Task ReplaceTestStepsAsync(string issueId, IReadOnlyList<XrayStepDefinition> steps)
    {
        await QueryAsync(
            "mutation($issueId: String!) { removeAllTestSteps(issueId: $issueId) }",
            new JsonObject { ["issueId"] = issueId });

        foreach (var step in steps)
        {
            await QueryAsync(
                "mutation($issueId: String!, $step: CreateStepInput!) { addTestStep(issueId: $issueId, step: $step) { id } }",
                new JsonObject
                {
                    ["issueId"] = issueId,
                    ["step"] = new JsonObject { ["action"] = step.Action, ["data"] = step.Data, ["result"] = step.Result }
                });
        }
    }

    public async Task UpdateTestTypeAsync(string issueId, string testType)
    {
        await QueryAsync(
            "mutation($issueId: String!, $testType: UpdateTestTypeInput!) { updateTestType(issueId: $issueId, testType: $testType) { issueId } }",
            new JsonObject { ["issueId"] = issueId, ["testType"] = new JsonObject { ["name"] = testType } });
    }

    // Keys of the first 100 tests of the plan.
    public async Task<IReadOnlyList<string>> GetTestKeysOfTestPlanAsync(string planKey)
    {
        var plans = await QueryAsync(
            "query($jql: String) { getTestPlans(jql: $jql, limit: 1) { results { issueId } } }",
            new JsonObject { ["jql"] = $"key={planKey}" });
        var planId = XrayJson.Text(XrayJson.Items(plans?["getTestPlans"]?["results"]).FirstOrDefault()?["issueId"])
            ?? throw new XrayClientException($"Test plan {planKey} not found in Xray");

        var plan = await QueryAsync(
            "query($issueId: String) { getTestPlan(issueId: $issueId) { tests(limit: 100) { results { jira(fields: [\"key\"]) } } } }",
            new JsonObject { ["issueId"] = planId });

        return XrayJson.Items(plan?["getTestPlan"]?["tests"]?["results"])
            .Select(test => XrayJson.Text(test?["jira"]?["key"]))
            .OfType<string>()
            .ToList();
    }

    // summary ~ "\"text\"" is a phrase search; the exact match is checked on the results.
    internal static string FindTestJql(string projectKey, string? key, string summary) =>
        key is not null
            ? $"key = \"{key}\""
            : $"project = \"{projectKey}\" AND issuetype = Test AND summary ~ {JsonString(JsonString(summary))}";

    private static string JsonString(string value) => JsonSerializer.Serialize(value, XrayJson.Options);

    private static ExistingTest ToExistingTest(JsonNode? result) => new(
        XrayJson.Text(result?["issueId"]) ?? "",
        XrayJson.Text(result?["jira"]?["key"]) ?? "",
        XrayJson.Text(result?["jira"]?["summary"]) ?? "",
        XrayJson.Text(result?["testType"]?["name"]),
        XrayJson.Items(result?["steps"])
            .Select(step => new XrayStepDefinition(
                XrayJson.Text(step?["action"]) ?? "",
                XrayJson.Text(step?["data"]) ?? "",
                XrayJson.Text(step?["result"]) ?? ""))
            .ToList());
}

sealed record ExistingTest(string IssueId, string Key, string Summary, string? TestType, IReadOnlyList<XrayStepDefinition> Steps);
