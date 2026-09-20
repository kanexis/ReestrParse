# GitHub repository metadata

## Suggested description

WPF + Selenium crawler for ФГИС ЕИАС: adaptive DevExpress pagination, parallel organization workers, multi-form 4.1.1/1.0.1 parsing, data-quality validation and live observability.

## Короткое описание на русском

WPF-инструмент для сбора реестра ФГИС ЕИАС: Selenium, адаптивная DevExpress-пагинация, параллельные workers, формы 4.1.1/1.0.1, telemetry, timings и мониторинг ошибок.

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
- `eias`
- `telemetry`
- `observability`
- `performance-monitoring`
- `concurrency`
- `multithreading`
- `async-await`
- `fault-tolerance`
- `data-quality`

## Current stack

- .NET 10
- WPF
- CommunityToolkit.Mvvm
- Selenium.WebDriver / Selenium.Support
- AngleSharp
- Microsoft.Extensions.Hosting / Dependency Injection
- xUnit
- GitHub Actions
- in-process structured parser telemetry
- `Stopwatch` timings / ETA / throughput

## Architecture highlights

- Layered `Domain / Application / Infrastructure.Selenium / WPF` architecture.
- Stateful catalog pagination isolated in one Selenium session.
- Details phase uses independent worker browsers (`1..6`, default `3`).
- Direct `orgId`/DetailUrl fast path when DevExpress exposes row metadata.
- Adaptive catalog-click fallback: `SourcePage` is only a hint; neighboring pages are checked when ordering changes.
- Published form discovery extracts `TemplatePrinter.aspx` URLs without interacting with jQuery modal/iframe.
- A TemplatePrinter workbook is parsed as a multi-sheet document.
- Form 4.1.1 is the canonical contact source; form 1.0.1 adds regulated activity and territory context.
- Partial results are preserved instead of converted to failures.
- Contacts are parsed by stable parameter codes rather than physical HTML row IDs.
- Typed validation prevents URLs/service values from being silently accepted as emails.
- Telemetry exposes stage, operation, worker, item/page, duration, structured error code and data source.

## Portfolio documentation

[`ARCHITECTURE.md`](../ARCHITECTURE.md) is a Russian-language engineering walkthrough with diagrams and terminology for code review/interview discussion: async vs parallelism, Selenium worker pool, stateful pagination, fallback navigation, data-quality validation, telemetry, cancellation and fault isolation.
