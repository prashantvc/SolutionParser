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

    public override int Execute([NotNull] CommandContext context, [NotNull] Settings settings)
    {
        if (!File.Exists(settings.Solution))
        {
            Console.WriteLine("Solution file does not exist.");
            return -1;
        }

        var sln = SolutionFile.Parse(settings.Solution);

        var projs = sln.ProjectsInOrder.Where(prj => prj.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat)
            .ToList();

        projs.ForEach(Console.WriteLine);

        var projects = new ConcurrentBag<Project>();
        Parallel.ForEach(projs, proj =>
        {
            var p = GetProjectDetails(proj.ProjectName, proj.AbsolutePath);
            Console.WriteLine(p);
            projects.Add(p);
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
        });

        string jsonFilePath = Path.Combine(Path.GetTempPath(), $"{Path.GetFileName(settings.Solution)}.json");
        File.WriteAllTextAsync(jsonFilePath, jsonStr);

        return 0;
    }

    Project GetProjectDetails(string name, string projPath)
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

        return new Project
        {
            Name = name,
            Path = projPath,
            TargetPath = assembly,
            OutputType = outputType,
            DesignerHostPath = Path.GetFullPath(desingerHostPath),

            TargetFramework = targetfx,
            DepsFilePath = projectDepsFilePath,
            RuntimeConfigFilePath = projectRuntimeConfigFilePath,

            CoreProject = proj,
            ProjectReferences = referencesPath

        };
    }
}
