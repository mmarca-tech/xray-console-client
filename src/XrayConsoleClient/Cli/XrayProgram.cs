using System.Globalization;

namespace XrayConsoleClient;

public static class XrayProgram
{
    private const string MinRequestIntervalVariable = "XRAY_MIN_REQUEST_INTERVAL_MS";
    private static readonly TimeSpan DefaultMinRequestInterval = TimeSpan.FromSeconds(1);

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Any(IsHelp))
        {
            Console.WriteLine(UploadOptions.HelpText());
            return 0;
        }

        try
        {
            var options = UploadOptions.Parse(args, Environment.GetEnvironmentVariable);
            Console.WriteLine(options.Describe());
            Console.WriteLine();

            // The rate limit handler times out every attempt itself, the client must not cut its 429 waits short.
            using var xrayHttp = new HttpClient(new RateLimitHandler(ReadMinRequestInterval()) { InnerHandler = new SocketsHttpHandler() })
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
            using var jiraHttp = new HttpClient();

            var apiUrl = options.XrayUrl.TrimEnd('/') + "/api/v2";
            var xray = new XrayRestClient(xrayHttp, apiUrl);
            await xray.AuthenticateAsync(options.ClientId, options.ClientSecret);
            var graphql = new XrayGraphqlClient(xrayHttp, apiUrl + "/graphql", xray.Token);
            var jira = new JiraRestClient(jiraHttp, options.JiraUrl.TrimEnd('/'), options.JiraBasicToken);

            await new ExecutionUploader(xray, graphql, jira).RunAsync(options);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static TimeSpan ReadMinRequestInterval()
    {
        var value = Environment.GetEnvironmentVariable(MinRequestIntervalVariable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultMinRequestInterval;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds) || milliseconds < 0)
        {
            throw new XrayInputException($"Invalid {MinRequestIntervalVariable} '{value}', expected milliseconds.");
        }

        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private static bool IsHelp(string value) =>
        value.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("--help", StringComparison.OrdinalIgnoreCase);
}
