namespace ReestrParse.Application.Monitoring;

public enum ParserPipelineStage
{
    Idle = 0,
    Regions = 1,
    CatalogNavigation = 2,
    CatalogFilters = 3,
    CatalogPages = 4,
    CatalogCompleted = 5,
    DetailsQueue = 6,
    DetailsNavigation = 7,
    DetailsFormDiscovery = 8,
    DetailsTemplate = 9,
    DetailsForm411 = 10,
    DetailsForm101 = 11,
    DetailsDataQuality = 12,
    DetailsCompleted = 13,
    Completed = 14,
    Cancelled = 15,
    Failed = 16,
    Report = 17,
    Batch = 18
}
