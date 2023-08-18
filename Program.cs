using Spectre.Console.Cli;

var app = new CommandApp<SolutionParserCommand>();
return await app.RunAsync(args);

internal sealed class SolutionParserCommand : Command<SolutionParserCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<SOLUTION>")]
        public string Solution { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        // Do something with the settings
        return 0;
    }
}
