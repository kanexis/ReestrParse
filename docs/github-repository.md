# GitHub repository metadata

## Suggested description

WPF + Selenium crawler for ФГИС ЕИАС: region catalogs, DevExpress pagination, parallel organization details, form 4.1.1 contacts and resilient click fallback.

## Короткое описание на русском

WPF-приложение для сбора реестра регулируемых организаций ФГИС ЕИАС и контактных данных из формы 4.1.1 с Selenium, DevExpress pagination и параллельными worker-сессиями.

## Suggested topics

- `csharp`
- `dotnet`
- `dotnet-10`
- `wpf`
- `mvvm`
- `selenium`
- `selenium-webdriver`
- `web-scraping`
- `web-automation`
- `anglesharp`
- `devexpress`
- `parallel-processing`
- `crawler`
- `russian-software`
- `eias`

## Current stack

- .NET 10
- WPF
- CommunityToolkit.Mvvm
- Selenium.WebDriver / Selenium.Support
- AngleSharp
- Microsoft.Extensions.Hosting / Dependency Injection
- xUnit
- GitHub Actions

## Architecture highlights

- Layered Domain / Application / Infrastructure / WPF architecture.
- Stateful catalog pagination isolated in one Selenium session.
- Details phase uses independent worker browsers (`1..6`, default `3`).
- Direct `orgId`/DetailUrl fast path when DevExpress exposes row metadata.
- Catalog-click fallback when row keys are unavailable: worker restores filtered catalog, navigates to `SourcePage`, matches `Name + INN + KPP`, clicks the first cell and captures the resulting organization URL.
- Form 4.1.1 is selected structurally from `ASPxGridViewDet`.
- `TemplatePrinter.aspx` is opened directly; modal dialog / iframe and workbook tab clicks are avoided.
- Contacts are parsed by stable form parameter codes rather than HTML row numbers.
