using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace XrayConsoleClient;

sealed class XrayRestClient(HttpClient http, string apiUrl)
{
    private string? token;

    public string Token => token ?? throw new InvalidOperationException("Not logged into Xray, call AuthenticateAsync first.");

    public async Task AuthenticateAsync(string clientId, string clientSecret)
    {
        Console.WriteLine("Logging into Jira Xray");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{apiUrl}/authenticate")
        {
            Content = XrayJson.Content(new JsonObject { ["client_id"] = clientId, ["client_secret"] = clientSecret })
        };

        // The token comes back as a json string
        var body = await HttpCalls.SendAsync(http, request, "Error logging in to Xray");
        try
        {
            token = JsonSerializer.Deserialize<string>(body);
        }
        catch (JsonException)
        {
            token = body.Trim();
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new XrayClientException("Xray did not return a token");
        }
    }

    // Xray json import, returns the key of the created (or updated) test execution.
    public async Task<string> ImportExecutionAsync(XrayImport import)
    {
        Console.WriteLine($"Sending xray json results ({import.Tests.Count} tests) into Jira Xray");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{apiUrl}/import/execution")
        {
            Content = XrayJson.Content(import)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        request.Headers.Accept.ParseAdd("*/*");

        var body = await HttpCalls.SendAsync(http, request, "Error sending Xray json results to Xray");
        var key = JsonNode.Parse(body) is JsonObject response ? XrayJson.Text(response["key"]) : null;
        return key ?? throw new XrayClientException($"Xray import did not return a test execution key: {body}");
    }

    // Xray's own NUnit import: Xray creates the tests from the report, without steps.
    public async Task ImportNunitAsync(string projectKey, string? planKey, string? executionKey, string nunitXml)
    {
        Console.WriteLine("Sending Nunit results into Jira Xray");
        var query = $"projectKey={Uri.EscapeDataString(projectKey)}";
        if (planKey is not null)
        {
            query += $"&testPlanKey={Uri.EscapeDataString(planKey)}";
        }

        if (executionKey is not null)
        {
            query += $"&testExecKey={Uri.EscapeDataString(executionKey)}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{apiUrl}/import/execution/nunit?{query}")
        {
            Content = new StringContent(nunitXml, Encoding.UTF8, "text/xml")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        await HttpCalls.SendAsync(http, request, "Error sending Nunit results to Xray");
    }
}
