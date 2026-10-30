using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace XrayConsoleClient;

sealed class JiraRestClient(HttpClient http, string jiraUrl, string basicToken)
{
    public async Task LinkIssueAsync(string linkTypeId, string executionKey, string issueKey)
    {
        Console.WriteLine($"Linking execution {executionKey} to issue {issueKey} (link type {linkTypeId})");
        var body = new JsonObject
        {
            ["type"] = new JsonObject { ["id"] = linkTypeId },
            ["inwardIssue"] = new JsonObject { ["key"] = executionKey },
            ["outwardIssue"] = new JsonObject { ["key"] = issueKey },
            ["comment"] = new JsonObject { ["body"] = "Linked related issue!" }
        };

        await SendAsync(HttpMethod.Post, "/rest/api/2/issueLink", body, "Error linking the execution to the issue");
    }

    public async Task SetCustomFieldAsync(string fieldId, string value, string issueKey)
    {
        Console.WriteLine($"Adding value {value} in custom field {fieldId} of issue {issueKey}");
        var body = new JsonObject { ["fields"] = new JsonObject { [fieldId] = value } };

        await SendAsync(HttpMethod.Put, $"/rest/api/3/issue/{Uri.EscapeDataString(issueKey)}", body, $"Error setting custom field {fieldId} of {issueKey}");
    }

    private async Task SendAsync(HttpMethod method, string path, JsonObject body, string errorTitle)
    {
        using var request = new HttpRequestMessage(method, $"{jiraUrl}{path}") { Content = XrayJson.Content(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicToken);
        await HttpCalls.SendAsync(http, request, errorTitle);
    }
}
