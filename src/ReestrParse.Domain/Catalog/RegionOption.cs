namespace ReestrParse.Domain.Catalog;

public sealed record RegionOption(string ExternalId, string Name)
{
    public override string ToString() => Name;
}
