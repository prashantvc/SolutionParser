using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Build.Construction;

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

        var solution = new Solution
        {
            Name = Path.GetFileNameWithoutExtension(settings.Solution),
            Path = settings.Solution
        };


        var projs = sln.ProjectsInOrder.Where(prj => prj.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat)
            .Select(prj => new Project { Name = prj.ProjectName, Path = prj.AbsolutePath })
            .ToList();

        solution.Projects = projs;

        Console.WriteLine(solution);

        return 0;
    }
}
