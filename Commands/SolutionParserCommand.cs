using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Build.Construction;
using System.Collections.Concurrent;
using Microsoft.Build.Definition;
using MSProject = Microsoft.Build.Evaluation.Project;
using System.Text.Json;

namespace Commands;
public sealed class SolutionParserCommand : Command<SolutionParserCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [Description("The solution file (.sln) path.")]
        [CommandArgument(0, "<SOLUTION>")]
        public required string Solution { get; init; }
    }

    record ProjectRecord(string Name, string Path);

    public override int Execute([NotNull] CommandContext context, [NotNull] Settings settings)
    {
        string solutionPath = Path.GetFullPath(settings.Solution);
        IEnumerable<ProjectRecord>? projFiles = null;

        if (!solutionPath.EndsWith(".sln") && Directory.Exists(solutionPath))
        {
            projFiles = Directory.GetFiles(solutionPath, "*.csproj")
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
                ProjectReferences = referencesPath

            };
        }
        catch
        {
            return null;
        }
    }
}
