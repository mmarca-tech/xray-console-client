# Xray Console Client

A .NET 10 command-line client that imports test results into Xray and links executions to Jira issues.

## Build and Test

```powershell
dotnet build xray-console-client.slnx
dotnet test tests/XrayConsoleClient.Tests/XrayConsoleClient.Tests.csproj
```

## Run

The root `XrayUploadResults.cs` launcher accepts the same options as the former console client. Required credentials and URLs can be supplied as options or environment variables.

```powershell
$env:XRAY_BASE_URL = 'https://xray.cloud.getxray.app'
$env:XRAY_CLIENT_ID = 'your-client-id'
$env:XRAY_CLIENT_SECRET = 'your-client-secret'
$env:JIRA_URL = 'https://your-domain.atlassian.net'
$env:JIRA_BASIC_TOKEN = 'base64-email-and-api-token'

dotnet run --file .\XrayUploadResults.cs -- --tt junit-xml --f .\test-results --pk PROJECT
```

Run with `--help` to see all options and supported result formats. Keep credentials in environment variables or a secret store; do not commit them.

## Release Package

Push a version tag such as `v1.0.0` to run the GitHub Actions workflow. It runs the unit tests, publishes the `xray-console-client` .NET tool to GitHub Packages, and attaches `xray-console-client-linux-x64.tar.gz` to the GitHub Release. The tarball is a self-contained Linux x64 executable and does not require the .NET runtime on the consuming agent. The NuGet tool requires the .NET 10 runtime.

### Install from GitHub Packages

Create a GitHub personal access token with `read:packages`, then configure the NuGet source and credentials:

```powershell
dotnet nuget add source https://nuget.pkg.github.com/mmarca-tech/index.json `
	--name github `
	--username YOUR_GITHUB_USERNAME `
	--password YOUR_GITHUB_TOKEN `
	--store-password-in-clear-text

dotnet tool install --global xray-console-client --version 1.0.0 --add-source github
```

The installed command is `xray-console-client`. Use `dotnet tool update --global xray-console-client --version <version> --add-source github` to upgrade it.