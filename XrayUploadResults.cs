#:property TargetFramework=net10.0
#:property PublishAot=false
#:project src/XrayConsoleClient/XrayConsoleClient.csproj

using XrayConsoleClient;

return await XrayProgram.RunAsync(args);
