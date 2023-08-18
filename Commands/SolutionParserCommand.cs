using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Build.Construction;
using Spectre.Console.Cli;
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
        sln.ProjectsInOrder.Where(prj => prj.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat)
            .Select(prj => new { Name = prj.ProjectName, Path = prj.AbsolutePath })
            .ToList().ForEach(Console.WriteLine);

        return 0;
    }
}
