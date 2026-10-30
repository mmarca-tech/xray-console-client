namespace XrayConsoleClient;

// Wrong or missing options, or a report that cannot be read.
public sealed class XrayInputException(string message) : Exception(message);

// A call to Xray or Jira failed.
public sealed class XrayClientException(string title, int? statusCode = null, string? responseBody = null)
    : Exception(statusCode is null ? title : $"{title}: HTTP {statusCode} {responseBody}".TrimEnd())
{
    public int? StatusCode { get; } = statusCode;
}
