namespace Models;
internal class Solution
{
    public required string Name { get; set; }
    public required string Path { get; set; }
    public List<Project> Projects { get; set; } = new();
    public override string ToString() => $"{Name} ({Path}), Projects: {Projects.Count}";
}
