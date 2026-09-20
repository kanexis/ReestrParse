# Details pipeline: карточка организации и формы 4.1.1 / 1.0.1

## Цель

После catalog phase каждая `OrganizationReference` обрабатывается независимо. Основная Selenium-сессия каталога больше не используется как history/navigation stack.

## 1. Открытие карточки

### Fast path

Если каталог отдал `OrganizationId`/`DetailUrl`, worker сразу открывает `PublicDisclosureInfoOrg.aspx`.

### Catalog-click fallback

Если row key недоступен, отдельный worker-browser открывает отфильтрованный каталог. `SourcePage` используется как **подсказка**, а не как абсолютная истина: worker проверяет ожидаемую страницу и соседние страницы, ждёт появление строки и сопоставляет её по:

1. `Name + INN + KPP`;
2. `INN + KPP`;
3. уникальному `INN`.

Основной catalog browser при этом не теряет пагинацию.

## 2. Discovery опубликованных форм

`EiasOrganizationPageReader` не кликает jQuery dialog. Из `ASPxGridViewDet` извлекаются все ссылки вида:

```text
openTemplateDialog('https://ri-loader.eias.ru/TemplatePrinter.aspx?...', ...)
```

Кандидаты сортируются по приоритету:

1. `4.1.1 — Общая информация об организации`;
2. `1.0.1 — Основные параметры раскрываемой информации`;
3. остальные опубликованные workbook.

Это важно: один TemplatePrinter workbook может содержать сразу несколько Excel-листов, поэтому отсутствие отдельной строки 4.1.1 в карточке больше не означает, что лист 4.1.1 физически отсутствует в workbook.

## 3. Workbook parsing

`EiasTemplateWorkbookParser` получает HTML TemplatePrinter и одним проходом ищет:

- `Форма 4.1.1` — контакты, руководитель, адреса, сайт;
- `Форма 1.0.1` — дата раскрытия, инфраструктурная система, вид деятельности, территория.

Переключать вкладки workbook в Selenium не нужно: HTML листов уже присутствует в документе.

## 4. Partial result

Если найден только 1.0.1:

```text
OrganizationDetailsResult.IsSuccess = true
OrganizationDetailsResult.IsPartial = true
UI status = "Частично"
```

Это лучше, чем терять всю организацию из-за отсутствующей контактной формы.

## 5. Data quality

Парсер не доверяет значениям вслепую. Например, код `9` формы 4.1.1 должен быть email. Если там URL или служебное значение, поле `Email` остаётся пустым, а в `Warnings` добавляется диагностическое сообщение.

## 6. Parallel workers

Catalog phase последовательна. Details phase использует независимые ChromeDriver-сессии:

```text
OrganizationReference[]
        ↓
ConcurrentQueue
   ↙    ↓    ↘
W1     W2     W3
Chrome Chrome Chrome
```

Один `IWebDriver` не делится между потоками. Worker создаёт browser один раз и переиспользует его для нескольких организаций.

## 7. Telemetry

Каждый подэтап публикует telemetry:

- navigation/fallback;
- form discovery;
- TemplatePrinter load;
- parse 4.1.1;
- parse 1.0.1;
- data-quality warning;
- completion.

События имеют `Code` и `DataSource`, поэтому ошибки можно агрегировать по причине, а не только читать текстом.
