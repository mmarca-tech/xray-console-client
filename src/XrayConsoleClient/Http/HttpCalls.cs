namespace XrayConsoleClient;

static class HttpCalls
{
    // Sends the request and returns the response body, or throws with the status and body when it failed.
    public static async Task<string> SendAsync(HttpClient http, HttpRequestMessage request, string errorTitle)
    {
        using var response = await http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new XrayClientException(errorTitle, (int)response.StatusCode, body);
        }

        return body;
    }
}
