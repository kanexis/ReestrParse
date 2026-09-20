# GitHub repository metadata

## Suggested description

WPF + Selenium crawler for ФГИС ЕИАС: DevExpress catalog pagination, parallel Form 4.1.1 contact extraction, resilient click fallback, live observability, timings, ETA and operator monitoring UI.

## Короткое описание на русском

WPF-приложение для сбора реестра организаций ФГИС ЕИАС и контактов формы 4.1.1: Selenium, DevExpress pagination, parallel workers, live progress, telemetry, timings и отдельный монитор логов.

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
- `telemetry`
- `monitoring`
- `performance-monitoring`
- `observability`
- `concurrency`
- `multithreading`
- `async-await`
## Current stack

- .NET 10
- WPF
- CommunityToolkit.Mvvm
- Selenium.WebDriver / Selenium.Support
- AngleSharp
- Microsoft.Extensions.Hosting / Dependency Injection
- xUnit
- GitHub Actions
- In-process parser telemetry (`IParserTelemetry`)
- Stopwatch-based timings / ETA / throughput metrics

## Architecture highlights

- Layered Domain / Application / Infrastructure / WPF architecture.
- Stateful catalog pagination isolated in one Selenium session.
- Details phase uses independent worker browsers (`1..6`, default `3`).
- Direct `orgId`/DetailUrl fast path when DevExpress exposes row metadata.
- Catalog-click fallback when row keys are unavailable: worker restores filtered catalog, navigates to `SourcePage`, matches `Name + INN + KPP`, clicks the first cell and captures the resulting organization URL.
- Form 4.1.1 is selected structurally from `ASPxGridViewDet`.
- `TemplatePrinter.aspx` is opened directly; modal dialog / iframe and workbook tab clicks are avoided.
- Contacts are parsed by stable form parameter codes rather than HTML row numbers.

## v0.7 monitoring highlights

- Separate WPF parser-monitor window; the organization DataGrid remains compact.
- One telemetry stream for catalog and details workers.
- Pipeline stages update both the main sidebar and the monitor UI.
- Per-page and per-organization timings.
- Per-step details timings: catalog fallback, organization card, 4.1.1 link extraction, TemplatePrinter load, HTML parse.
- Active workers, completed/total, success/error counters, average item time, throughput and ETA.
- Runtime-log filtering and CSV/TXT export.
## Portfolio documentation

В корне репозитория находится [`ARCHITECTURE.md`](../ARCHITECTURE.md) — отдельный русскоязычный технический разбор с Mermaid-диаграммами и объяснением решений, который можно использовать при code review и на собеседованиях.
