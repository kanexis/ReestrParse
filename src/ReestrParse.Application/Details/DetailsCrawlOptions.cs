namespace ReestrParse.Application.Details;

public sealed record DetailsCrawlOptions(
    int MaxParallelism = 3,
    bool Headless = true)
{
    public int NormalizedParallelism => Math.Clamp(MaxParallelism, 1, 6);
}
