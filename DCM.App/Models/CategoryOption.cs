namespace DCM.App.Models;

public sealed class CategoryOption
{
    public string Id { get; }
    public string Name { get; }
    public string DisplayName => string.IsNullOrWhiteSpace(Id) ? Name : $"{Name} ({Id})";

    public CategoryOption(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public override string ToString() => DisplayName;
}
