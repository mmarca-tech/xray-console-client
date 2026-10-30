using System.Text;

namespace XrayConsoleClient;

// Same options as the xray-console-client npm package (yargs): short or long name, "--name value" or "--name=value".
// Credentials and urls can also come from environment variables, so a pipeline does not have to put secrets on the command line.
public sealed class UploadOptions
{
    internal static readonly string[] TestTypes =
        ["cucumber-specflow", "cucumber-json", "junit-xml", "nunit-xml", "nunit", "allure-xml", "allure-json"];

    internal static readonly OptionSpec[] Specs =
    [
        new("xu", "xrayUrl", "Xray Url", "Xray host, /api/v2 is appended", "XRAY_BASE_URL", Required: true),
        new("ci", "clientId", "Client Id", "Xray client id", "XRAY_CLIENT_ID", Required: true, Secret: true),
        new("cs", "clientSecret", "Client Secret", "Xray client secret", "XRAY_CLIENT_SECRET", Required: true, Secret: true),
        new("ju", "jiraUrl", "Jira Url", "Jira base url", "JIRA_URL", Required: true),
        new("jbt", "jiraBasicToken", "Jira Basic Token", "Jira basic token, base64 of email:api-token", "JIRA_BASIC_TOKEN", Required: true, Secret: true),
        new("tt", "testType", "Test type", string.Join(" | ", TestTypes), Required: true),
        new("f", "filePath", "Path", "Results file or folder", Required: true),
        new("pk", "projectKey", "Project Key", "Jira project of the tests and the execution", Required: true),
        new("ek", "executionKey", "Execution Key", "Execution to update instead of creating a new one"),
        new("pn", "planKey", "Plan Key", "Test plan the execution is linked to"),
        new("s", "summary", "Summary", "Execution summary, a generic one when empty"),
        new("d", "description", "Description", "Execution description, a generic one when empty"),
        new("rv", "releaseVersion", "Release version", "Fix version of the execution"),
        new("i", "issueKey", "Issue Key", "Issue the execution is linked to, needs issueLinkType"),
        new("ilt", "issueLinkType", "Issue Key Link type", "Jira issue link type id"),
        new("e", "environments", "Environments", "Comma separated Xray test environments"),
        new("jcf", "jiraCustomFields", "Jira Custom Fields", "'<fieldId>,<value>' set on every test of the plan"),
    ];

    private readonly IReadOnlyDictionary<string, string> values;

    private UploadOptions(IReadOnlyDictionary<string, string> values) => this.values = values;

    public string XrayUrl => values["xrayUrl"];
    public string ClientId => values["clientId"];
    public string ClientSecret => values["clientSecret"];
    public string JiraUrl => values["jiraUrl"];
    public string JiraBasicToken => values["jiraBasicToken"];
    public string TestType => values["testType"];
    public string FilePath => values["filePath"];
    public string ProjectKey => values["projectKey"];
    public string? ExecutionKey => values.GetValueOrDefault("executionKey");
    public string? PlanKey => values.GetValueOrDefault("planKey");
    public string? Summary => values.GetValueOrDefault("summary");
    public string? Description => values.GetValueOrDefault("description");
    public string? ReleaseVersion => values.GetValueOrDefault("releaseVersion");
    public string? IssueKey => values.GetValueOrDefault("issueKey");
    public string? IssueLinkType => values.GetValueOrDefault("issueLinkType");
    public string? Environments => values.GetValueOrDefault("environments");
    public string? JiraCustomFields => values.GetValueOrDefault("jiraCustomFields");

    // Empty values count as not passed, like the pipeline template that only adds the options it has.
    public static UploadOptions Parse(IReadOnlyList<string> args, Func<string, string?> environment)
    {
        var arguments = ReadArguments(args);
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        var missing = new List<string>();

        foreach (var spec in Specs)
        {
            var value = arguments.GetValueOrDefault(spec.Long);
            if (string.IsNullOrWhiteSpace(value) && spec.Environment is not null)
            {
                value = environment(spec.Environment);
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                if (spec.Required)
                {
                    missing.Add(spec.Usage);
                }

                continue;
            }

            value = value.Trim();
            if (spec.Secret && value.StartsWith("$(", StringComparison.Ordinal))
            {
                throw new XrayInputException($"{spec.Usage} is the unresolved pipeline variable '{value}', check the Key Vault secrets.");
            }

            resolved[spec.Long] = value;
        }

        if (missing.Count > 0)
        {
            throw new XrayInputException($"Missing required option(s): {string.Join(", ", missing)}. Run with --help for usage.");
        }

        var options = new UploadOptions(resolved);
        if (!TestTypes.Contains(options.TestType.ToLowerInvariant()))
        {
            throw new XrayInputException($"Unknown test type '{options.TestType}', options available are {string.Join(" | ", TestTypes)}");
        }

        return options;
    }

    public string Describe() =>
        string.Join(Environment.NewLine, Specs.Select(spec =>
            $"{spec.Label}: {(spec.Secret ? "************" : values.GetValueOrDefault(spec.Long, ""))}"));

    public static string HelpText()
    {
        var help = new StringBuilder();
        help.AppendLine("Usage: dotnet run --file XrayUploadResults.cs -- --tt <testType> --f <path> --pk <projectKey> [options]");
        help.AppendLine();
        help.AppendLine("Options:");
        foreach (var spec in Specs)
        {
            var required = spec.Required ? "Required. " : "";
            var environment = spec.Environment is null ? "" : $" [env {spec.Environment}]";
            help.AppendLine($"  --{spec.Short,-4} --{spec.Long,-17} {required}{spec.Description}{environment}");
        }

        help.AppendLine();
        help.AppendLine("XRAY_MIN_REQUEST_INTERVAL_MS spaces the Xray calls apart (default 1000).");
        return help.ToString();
    }

    private static Dictionary<string, string> ReadArguments(IReadOnlyList<string> args)
    {
        var arguments = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Count; index++)
        {
            var (spec, value) = ReadOption(args[index])
                ?? throw new XrayInputException($"Unknown option or unexpected argument '{args[index]}'. Run with --help for usage.");

            if (value is null)
            {
                if (index + 1 >= args.Count || ReadOption(args[index + 1]) is not null)
                {
                    throw new XrayInputException($"Option '{args[index]}' needs a value.");
                }

                value = args[++index];
            }

            arguments[spec.Long] = value;
        }

        return arguments;
    }

    // "--tt", "-tt", "--testType" or "--testType=value"; null when the token is not a known option.
    private static (OptionSpec Spec, string? Value)? ReadOption(string token)
    {
        if (token.Length < 2 || token[0] != '-')
        {
            return null;
        }

        var name = token.TrimStart('-');
        string? value = null;
        var equals = name.IndexOf('=');
        if (equals >= 0)
        {
            value = name[(equals + 1)..];
            name = name[..equals];
        }

        var spec = Specs.FirstOrDefault(s =>
            s.Short.Equals(name, StringComparison.OrdinalIgnoreCase) || s.Long.Equals(name, StringComparison.OrdinalIgnoreCase));
        return spec is null ? null : (spec, value);
    }
}

internal sealed record OptionSpec(
    string Short,
    string Long,
    string Label,
    string Description,
    string? Environment = null,
    bool Required = false,
    bool Secret = false)
{
    public string Usage => $"--{Short} (--{Long})";
}
