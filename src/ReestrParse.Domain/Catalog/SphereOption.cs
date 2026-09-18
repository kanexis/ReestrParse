namespace ReestrParse.Domain.Catalog;

public sealed record SphereOption(string ExternalId, string Name)
{
    public static SphereOption HeatSupply { get; } = new("WARM", "Теплоснабжение");
    public override string ToString() => Name;
}
