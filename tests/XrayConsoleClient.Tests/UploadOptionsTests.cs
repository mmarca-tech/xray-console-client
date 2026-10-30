namespace XrayConsoleClient.Tests;

public class UploadOptionsTests
{
    [Fact]
    public void Parse_reads_short_long_and_inline_names()
    {
        var options = UploadOptions.Parse(
        [
            "--xu", "https://xray.example", "-ci", "id", "--clientSecret", "secret", "--ju=https://jira.example", "--jbt", "token",
            "--tt", "NUnit-Xml", "--f", "results.xml", "--pk", "QATG", "--pn", "QATG-1", "--s", "-starts with a dash"
        ], _ => null);

        Assert.Equal("https://xray.example", options.XrayUrl);
        Assert.Equal("id", options.ClientId);
        Assert.Equal("secret", options.ClientSecret);
        Assert.Equal("https://jira.example", options.JiraUrl);
        Assert.Equal("NUnit-Xml", options.TestType);
        Assert.Equal("QATG-1", options.PlanKey);
        Assert.Equal("-starts with a dash", options.Summary);
        Assert.Null(options.ExecutionKey);
    }

    [Fact]
    public void Parse_takes_credentials_from_environment_when_not_passed()
    {
        var environment = new Dictionary<string, string>
        {
            ["XRAY_BASE_URL"] = "https://xray.example",
            ["XRAY_CLIENT_ID"] = "id",
            ["XRAY_CLIENT_SECRET"] = "secret",
            ["JIRA_URL"] = "https://jira.example",
            ["JIRA_BASIC_TOKEN"] = "token"
        };

        var options = UploadOptions.Parse(["--tt", "junit-xml", "--f", "results", "--pk", "QATG"], environment.GetValueOrDefault);

        Assert.Equal("secret", options.ClientSecret);
        Assert.Equal("token", options.JiraBasicToken);
    }

    [Fact]
    public void Parse_treats_empty_values_as_not_passed()
    {
        var options = Options.Parse("--pn", "", "--ek", "  ");

        Assert.Null(options.PlanKey);
        Assert.Null(options.ExecutionKey);
    }

    [Fact]
    public void Parse_lists_every_missing_required_option()
    {
        var error = Assert.Throws<XrayInputException>(() => UploadOptions.Parse(["--tt", "nunit-xml"], _ => null));

        Assert.Contains("--xu (--xrayUrl)", error.Message);
        Assert.Contains("--jbt (--jiraBasicToken)", error.Message);
        Assert.Contains("--pk (--projectKey)", error.Message);
        Assert.DoesNotContain("--tt", error.Message);
    }

    [Theory]
    [InlineData("--unknown", "value")]
    [InlineData("positional")]
    public void Parse_rejects_unknown_arguments(params string[] extra)
    {
        Assert.Throws<XrayInputException>(() => Options.Parse(extra));
    }

    [Fact]
    public void Parse_rejects_option_without_value()
    {
        Assert.Throws<XrayInputException>(() => Options.Parse("--pn", "--s", "summary"));
    }

    [Fact]
    public void Parse_rejects_unknown_test_type()
    {
        var error = Assert.Throws<XrayInputException>(() => Options.Parse("--tt", "xunit"));
        Assert.Contains("nunit-xml", error.Message);
    }

    [Fact]
    public void Parse_rejects_unresolved_pipeline_variable_secret()
    {
        var error = Assert.Throws<XrayInputException>(() => Options.Parse("--cs", "$(jira-client-secret)"));
        Assert.Contains("unresolved pipeline variable", error.Message);
    }

    [Fact]
    public void Describe_masks_secrets()
    {
        var description = Options.Parse("--pn", "QATG-1").Describe();

        Assert.Contains("Plan Key: QATG-1", description);
        Assert.Contains("Client Secret: ************", description);
        Assert.DoesNotContain("secret\n", description.Replace("\r", ""));
        Assert.DoesNotContain(": token", description);
        Assert.DoesNotContain(": id", description);
    }
}
