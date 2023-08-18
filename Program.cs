using System.Diagnostics;
using System.Text.RegularExpressions;

InitializeMSBuilePath();
var app = new CommandApp<SolutionParserCommand>();
return await app.RunAsync(args);


static void InitializeMSBuilePath()
{
    try
    {
        ProcessStartInfo startInfo = new("dotnet", "--list-sdks")
        {
            RedirectStandardOutput = true
        };

        var process = Process.Start(startInfo);
        if (process == null)
            throw new InvalidOperationException("Could not start dotnet process.");

        process.WaitForExit(1000);

        var output = process.StandardOutput.ReadToEnd();
        var sdkPaths = Regex.Matches(output, "([0-9]+.[0-9]+.[0-9]+) \\[(.*)\\]")
            .OfType<Match>()
            .Select(m => Path.Combine(m.Groups[2].Value, m.Groups[1].Value, "MSBuild.dll"));

        var sdkPath = sdkPaths.Last();
        Environment.SetEnvironmentVariable("MSBUILD_EXE_PATH", sdkPath);
    }
    catch (Exception exception)
    {
        Console.WriteLine("Could not set MSBUILD_EXE_PATH: " + exception);
        throw;
    }
}