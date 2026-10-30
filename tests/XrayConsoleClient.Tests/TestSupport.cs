using System.Net;
using System.Text;

namespace XrayConsoleClient.Tests;

sealed class TempDir : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("xray-client-tests-").FullName;

    public string Write(string fileName, string content)
    {
        var file = System.IO.Path.Combine(Path, fileName);
        File.WriteAllText(file, content);
        return file;
    }

    public void Dispose() => Directory.Delete(Path, recursive: true);
}

sealed record RecordedRequest(HttpMethod Method, string Url, string Body, string? Authorization);

// Answers every request with the responder and records what was sent.
sealed class FakeHttpHandler(Func<RecordedRequest, HttpResponseMessage> responder) : HttpMessageHandler
{
    public List<RecordedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedRequest(request.Method, request.RequestUri!.ToString(), body, request.Headers.Authorization?.ToString());
        Requests.Add(recorded);
        return responder(recorded);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

static class Options
{
    public static UploadOptions Parse(params string[] extra) =>
        UploadOptions.Parse(
        [
            "--xu", "https://xray.example", "--ci", "id", "--cs", "secret", "--ju", "https://jira.example", "--jbt", "token",
            "--tt", "nunit-xml", "--f", "results", "--pk", "QATG", .. extra
        ], _ => null);
}
