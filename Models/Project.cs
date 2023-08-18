namespace Models;
internal class Project
{
    public required string Name { get; set; }
    public required string Path { get; set; }
    public override string ToString() => $"{Name} ({Path})";
}