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
    DetailsForm411 = 8,
    DetailsCompleted = 9,
    Completed = 10,
    Cancelled = 11,
    Failed = 12
}
