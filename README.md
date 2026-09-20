# ReestrParse

> WPF-приложение для сбора реестра регулируемых организаций ФГИС ЕИАС, контактных данных из **4.1.1** и дополнительного контекста из **1.0.1**, с параллельными Selenium-workers и live observability.

![.NET](https://img.shields.io/badge/.NET-10.0_LTS-512BD4)
![WPF](https://img.shields.io/badge/UI-WPF-0C54C2)
![Selenium](https://img.shields.io/badge/Selenium-4.49.0-43B02A)
![AngleSharp](https://img.shields.io/badge/AngleSharp-1.8.1-6C63FF)
![Architecture](https://img.shields.io/badge/architecture-layered-111827)

> **v0.9:** multi-form/data-quality update: 4.1.1 остаётся основным источником контактов, форма 1.0.1 используется как дополнительный/fallback источник; каталог fallback теперь ищет организацию по соседним страницам, telemetry получил коды ошибок и источник данных, а UI показывает partial-result и расширенные детали выбранной строки.

> **v0.8:** observability/UX update: монитор парсера получил непрерывный elapsed/ETA, отдельную привязку исходной страницы к каждой операции, более подробный журнал, а в корень репозитория добавлен портфолио-документ [`ARCHITECTURE.md`](ARCHITECTURE.md) с диаграммами и разбором асинхронности/worker pool.

> **v0.7:** добавлен отдельный монитор парсера: live pipeline stages, общий progress, page/item timings, worker activity, ETA, throughput, фильтруемый журнал ошибок/событий и CSV export логов.

> **v0.6:** для каталогов, где DevExpress не отдаёт `orgId`, добавлен fallback через реальный клик строки в отдельной Selenium worker-session.


## Документация

- [`ARCHITECTURE.md`](ARCHITECTURE.md) — подробный русскоязычный разбор архитектуры для разработки и портфолио: Mermaid-схемы, pipeline, Selenium/AngleSharp, async vs parallelism, worker pool, telemetry, cancellation и fault isolation.
- [`docs/pagination-strategy.md`](docs/pagination-strategy.md) — стратегия обхода DevExpress-пагинации.
- [`docs/details-pipeline.md`](docs/details-pipeline.md) — обработка карточек и multi-form pipeline 4.1.1/1.0.1.
- [`docs/github-repository.md`](docs/github-repository.md) — рекомендуемое описание, topics и stack для GitHub.

## Что умеет проект

Текущий pipeline:

1. Открывает `https://ri.eias.ru/Map.aspx` через Selenium WebDriver.
2. Загружает реальные регионы из динамического Bootstrap-select.
3. Выбирает регион и сферу **«Теплоснабжение»**.
4. Выбирает фильтр **«Общая информация об организации»** и отдельно нажимает `#searchBtn` (`НАЙТИ`).
5. Проходит актуальную DevExpress-пагинацию `ASPxGridView2` и собирает весь каталог:
   - организация;
   - ИНН;
   - КПП;
   - номер исходной страницы;
   - внутренний `orgId`/прямой URL карточки, если его отдаёт DevExpress.
6. Параллельно несколькими независимыми ChromeDriver-сессиями открывает карточки организаций:
   - fast path — прямой `DetailUrl/orgId`, если DevExpress его отдал;
   - fallback — worker открывает отфильтрованный каталог, переходит на `SourcePage` и кликает первую ячейку нужной строки по `Название + ИНН + КПП`.
7. После fallback реальный `orgId` извлекается из URL открывшейся карточки.
8. На странице организации собирает все опубликованные `TemplatePrinter.aspx` URL и приоритизирует **4.1.1**, затем **1.0.1** и остальные опубликованные workbook-кандидаты.
9. Не кликает модальное окно/iframe: прямой URL извлекается из `openTemplateDialog(...)`.
10. Один HTML workbook разбирается сразу по нескольким листам: **4.1.1** даёт контакты, **1.0.1** — систему, вид деятельности, регион/район/муниципальное образование и дату обновления.
11. Если 4.1.1 отсутствует, но 1.0.1 доступна, результат помечается **«Частично»**, а не превращается в общую ошибку.
12. Telemetry фиксирует источник данных, код ошибки/предупреждения и timings каждого подэтапа.

## Какие данные извлекаются

### Форма 4.1.1 — основной источник контактов

Используются стабильные коды параметров, а не номера HTML-строк:

| Код | Данные |
| --- | --- |
| `2.1` | Наименование организации |
| `2.2` | ИНН |
| `2.3` | КПП |
| `3.1.1–3.1.3` | ФИО ответственного лица |
| `3.2` | Должность ответственного лица |
| `3.3` | Телефон ответственного лица |
| `3.4` | Email ответственного лица |
| `4.1–4.3` | ФИО руководителя |
| `5` | Почтовый адрес |
| `6` | Адрес местонахождения |
| `7.1` | Контактные телефоны организации; поддерживается несколько строк |
| `8` | Официальный сайт |
| `9` | Email организации |

Email дополнительно валидируется: URL/служебная ссылка больше не попадёт в колонку почты; такое значение будет записано в предупреждение качества данных.

### Форма 1.0.1 — дополнительный контекст

| Код | Данные |
| --- | --- |
| `1` | Дата заполнения/внесения изменений |
| `2.1` | Централизованная система коммунальной инфраструктуры |
| `3.1` | Регулируемый вид деятельности |
| `4.1.1` | Субъект РФ |
| `4.1.1.1` | Муниципальный район |
| `4.1.1.1.1` | Муниципальное образование |

## Почему новая схема быстрее старой

Старая версия работала так:

```text
каталог page N
→ открыть организацию
→ прочитать форму
→ Back
→ ЕИАС возвращает page 1
→ снова пройти 1..N
```

На больших реестрах время росло всё сильнее с каждой страницей.

Новая архитектура разделяет работу на две независимые фазы:

```text
Catalog phase — 1 Selenium session
Map.aspx → filters → search → page 1 → page 2 → ... → OrganizationReference[]

Details phase — N Selenium workers
OrganizationReference queue
      ├─ Chrome #1 → detail → TemplatePrinter → 4.1.1 + 1.0.1
      ├─ Chrome #2 → detail → TemplatePrinter → 4.1.1 + 1.0.1
      └─ Chrome #3 → detail → TemplatePrinter → 4.1.1 + 1.0.1
```

Основная browser-session каталога больше не используется как навигационный стек. Если прямой URL недоступен, fallback выполняется в **отдельном worker-браузере**: он открывает каталог заново, переходит сразу на сохранённую страницу и кликает организацию. Поэтому пагинация основного списка WPF не сбрасывается.

## Архитектура

```text
ReestrParse.Domain
        ↑
ReestrParse.Application
        ↑
ReestrParse.Infrastructure.Selenium
        ↑
ReestrParse.Wpf
```

### Domain

Чистые модели предметной области:

- `RegionOption`;
- `SphereOption`;
- `OrganizationReference`;
- `OrganizationContactDetails`.

Не зависит от Selenium, WPF и хранения данных.

### Application

Контракты use-case'ов:

- `IEiasCatalogService`;
- `IEiasOrganizationDetailsService`;
- progress/result/options DTO.

### Infrastructure.Selenium

Работа с ФГИС ЕИАС:

- Selenium browser session;
- выбор региона и фильтров;
- DevExpress pagination;
- чтение live DOM;
- получение `orgId`/DetailUrl;
- click fallback по `Название + ИНН + КПП` для строк без row key;
- worker pool для карточек;
- discovery всех опубликованных TemplatePrinter-кандидатов;
- приоритет 4.1.1 + fallback/дополнение 1.0.1;
- HTML parsing через AngleSharp;
- validation/quality warnings и structured telemetry.

### WPF

MVVM-интерфейс оператора:

- выбор региона/сферы;
- загрузка полного каталога;
- выбор числа parallel workers `1..6`;
- фоновые Chrome для details phase;
- live-progress;
- поиск по организации, ИНН, КПП, телефону, email и ответственному лицу;
- контактные данные прямо в таблице;
- дополнительные сведения выбранной организации через `RowDetails` без раздувания основной таблицы;
- статусы `Готово / Частично / Ошибка / Предупреждения`;
- отдельное окно **«Мониторинг / логи»**, не расширяющее основной DataGrid;
- live progress по страницам и организациям;
- elapsed / ETA / среднее время на организацию / throughput;
- состояние workers, текущая организация и страница каталога;
- подробные step timings: карточка → discovery форм → TemplatePrinter → 4.1.1/1.0.1 → data quality;
- фильтрация журнала по уровню и тексту;
- экспорт runtime-лога в CSV/TXT.

## Технологии

| Технология | Назначение |
| --- | --- |
| .NET 10 | LTS runtime / SDK |
| WPF | Desktop UI |
| CommunityToolkit.Mvvm 8.4.2 | MVVM, ObservableObject, RelayCommand |
| Selenium.WebDriver 4.49.0 | Навигация по динамическому ЕИАС |
| Selenium.Support 4.49.0 | WebDriverWait и support API |
| AngleSharp 1.8.1 | Парсинг стабильных HTML snapshot'ов |
| Microsoft.Extensions.Hosting 10.0.12 | DI и lifecycle приложения |
| xUnit | Unit tests парсеров |
| GitHub Actions | Windows CI: restore → build → test |
| `System.Diagnostics.Stopwatch` + in-process telemetry hub | Замеры времени операций, ETA и производительности без внешнего logging framework |

Версии NuGet централизованы в `Directory.Packages.props`.

## Производительность

Каталог намеренно остаётся **однопоточным**: DevExpress pagination — stateful последовательность одной браузерной сессии.

Контактные данные обрабатываются параллельно. По умолчанию UI использует **3 worker'а**. Рекомендуемый диапазон — `2–4`; максимальное значение в интерфейсе ограничено `6`, чтобы не создавать чрезмерную нагрузку на ЕИАС и локальную машину.

Каждый worker создаёт Chrome один раз и обрабатывает несколько организаций из общей очереди. Новый браузер на каждую организацию не запускается.

## Запуск

Требования:

- Windows;
- .NET 10 SDK;
- Google Chrome.

```powershell
dotnet restore ReestrParse.slnx
dotnet build ReestrParse.slnx
dotnet test ReestrParse.slnx
dotnet run --project .\src\ReestrParse.Wpf
```

Selenium Manager подбирает совместимый ChromeDriver автоматически.

## Структура

```text
src/
├── ReestrParse.Domain/
├── ReestrParse.Application/
├── ReestrParse.Infrastructure.Selenium/
│   ├── Browser/
│   └── Eias/
│       └── Details/
└── ReestrParse.Wpf/

tests/
├── ReestrParse.Application.Tests/
└── ReestrParse.Infrastructure.Selenium.Tests/

docs/
├── architecture.md
├── pagination-strategy.md
├── details-pipeline.md
└── github-repository.md
```

## Roadmap

- [x] WPF + MVVM shell
- [x] Динамическая загрузка регионов
- [x] Теплоснабжение + форма «Общая информация об организации»
- [x] DevExpress pagination каталога
- [x] Каталог `Организация / ИНН / КПП`
- [x] Гибридный details pipeline: direct URL + catalog click fallback без разрушения основной пагинации
- [x] Приоритетный поиск формы 4.1.1 + fallback на опубликованные workbook
- [x] Дополнительный парсинг формы 1.0.1
- [x] Валидация email и предупреждения качества данных
- [x] Парсинг TemplatePrinter без кликов по Excel-вкладкам
- [x] Parallel Selenium worker pool для контактных данных
- [x] Отдельное окно мониторинга pipeline / timings / errors / workers
- [x] CSV export runtime-логов
- [ ] Excel export из текущей модели данных
- [ ] Checkpoint/resume
- [ ] EF Core persistence
- [ ] Повторный запуск только для ошибочных организаций
- [ ] Опциональный HTTP fast-path для `TemplatePrinter`, если он стабильно работает без browser session

## Ограничения

ФГИС ЕИАС — внешний динамический сервис. DOM, DevExpress callbacks и параметры страниц могут изменяться. Поэтому Selenium selectors и HTML parsing изолированы в Infrastructure, а Domain/Application не зависят от конкретной разметки сайта.

Если DevExpress не отдаёт внутренний row key, это больше не ошибка: отдельный worker открывает нужную `SourcePage` каталога и использует проверенный клик по первой ячейке организации. Такой путь медленнее direct URL, но сохраняет работоспособность на каталогах без client-side keys.
