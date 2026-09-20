# Architecture

## Цель

Отделить динамическую browser automation от бизнес-модели и WPF, а также исключить старый сценарий `Back() → page 1 → повторная пагинация`.

## Dependency rule

```text
Domain ← Application ← Infrastructure.Selenium ← WPF composition root
```

`Domain` не знает о Selenium, AngleSharp, WPF и DI.

## Основные use cases

### Catalog

`IEiasCatalogService`

```text
Map.aspx
→ region
→ filters
→ #searchBtn
→ ASPxGridView2 pages
→ OrganizationReference[]
```

Каталог последовательно проходит DevExpress pagination одной browser session.

### Details

`IEiasOrganizationDetailsService`

```text
OrganizationReference[]
→ concurrent queue
→ 1..6 independent ChromeDriver workers
→ PublicDisclosureInfoOrg.aspx
→ form 4.1.1 row
→ TemplatePrinter URL
→ Form411Parser
→ OrganizationContactDetails[]
```

Один `IWebDriver` никогда не используется несколькими worker'ами одновременно.

## Почему TemplatePrinter не требует UI automation

Карточка организации уже содержит прямой URL внутри `openTemplateDialog(...)`. Поэтому details crawler не кликает иконку, jQuery dialog и iframe.

TemplatePrinter в свою очередь уже содержит HTML всех листов workbook. Парсер ищет table, содержащий заголовок формы 4.1.1, и читает параметры по стабильным кодам.

## Модели

### OrganizationReference

Лёгкая запись каталога:

- название;
- ИНН;
- КПП;
- регион;
- сфера;
- source page;
- `OrganizationId`;
- `DetailUrl`.

### OrganizationContactDetails

Результат формы 4.1.1:

- организация / ИНН / КПП;
- телефоны организации;
- email / website;
- ответственное лицо + контакты;
- руководитель;
- адреса;
- source URLs.

## Future persistence

Хранилище добавляется через Application contracts, например:

- `IOrganizationReferenceStore`;
- `IOrganizationDetailsStore`;
- `ICrawlCheckpointStore`.

EF Core не должен добавлять атрибуты или зависимости в Domain.


## Runtime monitoring / telemetry

`Infrastructure` и `Application` не обращаются напрямую к WPF. Вместо этого они публикуют `ParserTelemetryEvent` через `IParserTelemetry`.

```text
Catalog / Details workers
        ↓
IParserTelemetry
        ↓
ParserTelemetryHub
     ↙       ↘
MainWindow   ParserMonitorWindow
status       logs / timings / ETA
```

Событие может содержать stage, operation, worker id, page/total pages, item/total items, organization/INN, duration, counters и error. Благодаря этому основной UI показывает только компактный pipeline/status, а отдельный монитор хранит подробный журнал и performance metrics.
