using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Build.Construction;
using System.Collections.Concurrent;
using Microsoft.Build.Definition;
using MSProject = Microsoft.Build.Evaluation.Project;
using System.Text.Json;
using System.Drawing;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Commands;
public sealed class SolutionParserCommand : Command<SolutionParserCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [Description("The solution file (.sln) path.")]
        [CommandArgument(0, "<SOLUTION>")]
        public required string Solution { get; init; }

        [Description("The .NET SDK version.")]
        [CommandOption("-s|--sdk <SDK>")]
        public required string Sdk { get; init; }

        [Description("Include prerelease sdk versions.")]
        [CommandOption("-p|--prerelease")]
        public required bool Prerelease { get; init; }
    }

    record ProjectRecord(string Name, string Path);

    public override int Execute([NotNull] CommandContext context, [NotNull] Settings settings)
    {
        InitializeMSBuilePath(settings.Sdk, settings.Prerelease);

        string solutionPath = Path.GetFullPath(settings.Solution);
        IEnumerable<ProjectRecord>? projFiles = null;

        if (!solutionPath.EndsWith(".sln") && Directory.Exists(solutionPath))
        {
            string[] projFileGlobs = ["*.csproj", "*.fsproj"];
            projFiles = projFileGlobs
                .SelectMany(glob => Directory.GetFiles(solutionPath, glob))
                .Select(p => new ProjectRecord(Path.GetFileNameWithoutExtension(p), p));
        }

        if (File.Exists(settings.Solution) && projFiles is null)
        {
            var sln = SolutionFile.Parse(settings.Solution);
            projFiles = sln.ProjectsInOrder.Where(prj => prj.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat)
               .Select(prj => new ProjectRecord(prj.ProjectName, prj.AbsolutePath));
        }

        if (projFiles is null)
        {
            Console.WriteLine("Invalid solution path");
            return 1;
        }

        var projects = new ConcurrentBag<Project>();
        Parallel.ForEach(projFiles, proj =>
        {
            var projectDetails = GetProjectDetails(proj.Name, proj.Path);
            if (projectDetails != null)
                projects.Add(projectDetails);
        });

        var allProjects = projects.ToList();

        List<ProjectFile> designerFiles = new();

        foreach (var proj in allProjects)
        {
            proj.CoreProject?.GetItems("AvaloniaXaml").ToList().ForEach(item =>
            {
                var filePath = Path.GetFullPath(item.EvaluatedInclude, proj.DirectoryPath ?? "");
                var designerFile = new ProjectFile
                {
                    Path = filePath,
                    TargetPath = proj.TargetPath,
                    ProjectPath = proj.Path
                };
                designerFiles.Add(designerFile);
            });
        }

        var json = new { settings.Solution, Projects = allProjects, Files = designerFiles };

        var jsonStr = JsonSerializer.Serialize(json, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        string jsonFilePath = Path.Combine(Path.GetTempPath(), $"{Path.GetFileName(settings.Solution)}.json");
        File.WriteAllText(jsonFilePath, jsonStr);

        Console.WriteLine(jsonStr);

        return 0;
    }

    Project? GetProjectDetails(string name, string projPath)
    {
        try
        {
            var proj = MSProject.FromFile(projPath, new ProjectOptions());

            var assembly = proj.GetPropertyValue("TargetPath");
            var outputType = proj.GetPropertyValue("outputType");
            var desingerHostPath = proj.GetPropertyValue("AvaloniaPreviewerNetCoreToolPath");

            var targetfx = proj.GetPropertyValue("TargetFramework");
            var projectDepsFilePath = proj.GetPropertyValue("ProjectDepsFilePath");
            var projectRuntimeConfigFilePath = proj.GetPropertyValue("ProjectRuntimeConfigFilePath");

            var references = proj.GetItems("ProjectReference");
            var referencesPath = references.Select(p => Path.GetFullPath(p.EvaluatedInclude, projPath)).ToArray();
            desingerHostPath = string.IsNullOrEmpty(desingerHostPath) ? "" : Path.GetFullPath(desingerHostPath);

            var intermediateOutputPath = GetIntermediateOutputPath(proj);

            return new Project
            {
                Name = name,
                Path = projPath,
                TargetPath = assembly,
                OutputType = outputType,
                DesignerHostPath = desingerHostPath,

                TargetFramework = targetfx,
                DepsFilePath = projectDepsFilePath,
                RuntimeConfigFilePath = projectRuntimeConfigFilePath,

                CoreProject = proj,
                ProjectReferences = referencesPath,
                IntermediateOutputPath = intermediateOutputPath

            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing project {name}: {ex.Message}");
            return null;
        }
    }

    static string GetIntermediateOutputPath(MSProject proj)
    {
        var intermediateOutputPath = proj.GetPropertyValue("IntermediateOutputPath");
        var iop = Path.Combine(intermediateOutputPath, "Avalonia", "references");

        if (!Path.IsPathRooted(intermediateOutputPath))
        {
            iop = Path.Combine(proj.DirectoryPath ?? "", iop);
            if (Path.DirectorySeparatorChar == '/')
                iop = iop.Replace("\\", "/");
        }

        return iop;
    }

    static void InitializeMSBuilePath(string sdk, bool prerelease)
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
            string pattern = @"(\d+\.\d+\.\d+[-\w\.]*)\s+\[(.*)\]";
            var sdkPaths = Regex.Matches(output, pattern)
                .OfType<Match>()
                .Where(m => prerelease ? true : (m.Value.IndexOf('-') >= 0 ? false : true))
                .Select(m => new { Version = m.Groups[1].Value, Path = Path.Combine(m.Groups[2].Value, m.Groups[1].Value, "MSBuild.dll") });

            var sdkPath = (sdk == null ? sdkPaths.LastOrDefault() :
                 sdkPaths.Where(p => p.Version.StartsWith(sdk)).FirstOrDefault())
                 ?? throw new InvalidOperationException($"Could not find .NET SDK version {sdk}");

            Environment.SetEnvironmentVariable("MSBUILD_EXE_PATH", sdkPath.Path);
        }
        catch (Exception exception)
        {
            Console.WriteLine("Could not set MSBUILD_EXE_PATH: " + exception);
            throw;
        }
    }
}
