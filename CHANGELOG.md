# Changelog

## 0.9.8 — classic UI + reporting integration

- Возвращён визуальный язык v0.9.6: тёмная левая sidebar-панель, pipeline, единый рабочий блок, большая таблица и нижняя полоса прогресса.
- Функции v0.9.7 не удалены: XLSX, два режима отчёта, выбор папки, пакетная обработка регионов, пауза/продолжение/пропуск и компактный логгер сохранены.
- Настройки Excel встроены вторым рядом в старую панель параметров вместо отдельного набора карточек.
- Ручные действия `Каталог` / `Карточки`, полный текущий регион и все регионы доступны из одной классической панели управления.
- Главная таблица освобождена от технических колонок: `В отчёт`, `Наименование`, `ИНН`, `Email`; диагностика остаётся в RowDetails выбранной строки.
- Email в UI и XLSX по-прежнему выбирается по приоритету: руководитель → организация → ответственное/прочее контактное лицо.
- Нижняя классическая полоса прогресса расширена кнопками пакетного режима и путём последнего отчёта, не меняя основной компоновки.
- Окно мониторинга возвращено к компоновке v0.9.6; добавлены режим `Основные события / Подробный лог` и компактные счётчики warning/error/retry.

## 0.9.7 — Excel registry + reporting UI + sequential regions

- Добавлен итоговый XLSX-отчёт без внешней зависимости от Excel/Office.
- Итоговая таблица содержит только `Наименование`, `ИНН`, `Email`.
- Приоритет email: руководитель → организация → ответственное лицо.
- Добавлено чтение email руководителя по смысловой подписи, если поле присутствует в форме.
- Имя отдельного файла: `Реестр_<регион>_Теплоснабжение_<yyyy-MM-dd>.xlsx`.
- Глобальный режим: `Реестр_Теплоснабжение_<yyyy-MM-dd>.xlsx`, каждый регион на отдельном листе.
- В интерфейсе можно выбрать папку сохранения и простые настройки отчёта: строки без email, ошибочные строки, автосохранение, открытие результата.
- В таблице можно снять `В отчёт` с отдельной организации — она не попадёт в XLSX.
- Добавлен последовательный режим обработки всех регионов: один регион за раз, workers работают только внутри текущего региона.
- Добавлены безопасная пауза/продолжение между этапами, пропуск текущего региона и продолжение после ошибки региона.
- Для общего Excel при автосохранении файл обновляется после каждого завершённого региона.
- Главный UI упрощён: итоговая таблица показывает только полезные для реестра данные и статус.
- Мониторинг по умолчанию показывает только ключевые события; подробный telemetry остаётся доступен переключателем и в CSV.
- В мониторинг добавлены агрегаты warnings/errors/retry и события пакетной обработки/Excel.

## 0.9.6 — stable sequential catalog pager

- Исправлен регресс v0.9.5, из-за которого основной catalog crawler мог зависать даже на обычном переходе `3 -> 4`.
- Основной catalog phase больше не использует worker-навигацию `visible hops -> GotoPage -> sequential client navigation`.
- Для последовательного обхода каталога добавлен отдельный путь `GoToNextCatalogPage`: штатный DevExpress `PBN` (`Следующая`) -> видимая кнопка `Следующая` -> client `NextPage`.
- Каждый шаг подтверждается фактическим изменением pager и наличием строк; при сбое выполняется максимум 3 локальные попытки без 5-кратного worker recovery.
- `visible pager hops` сохранены только для details-workers, где нужен переход к далёкой `SourcePage` (например `1 -> 7 -> 8`).
- Основной catalog crawler и details-worker navigation теперь изолированы, чтобы изменение стратегии worker-переходов не ломало стабильный последовательный сбор каталога.

## 0.9.5 — visible pager hops

- Исправлена причина зависания на page 8+: Selenium больше не пытается кликнуть numeric-link страницы, которой ещё нет в DOM.
- Основная навигация теперь читает только реально видимые `a.dxp-num`/`b.dxp-num` и выбирает ближайшую видимую страницу в сторону цели.
- Пример: если нужна page 8, а с page 1 видны только 1..7, Worker кликает 7; после DevExpress callback перечитывает pager и уже кликает появившуюся 8.
- После каждого hop pager и IWebElement получаются заново; stale DOM между callback-ами не переиспользуется.
- `ASPxGridView2.GotoPage` и последовательный client `NextPage/PrevPage` сохранены как резервные пути.
- Добавлены unit-тесты выбора ближайшей видимой страницы вперёд/назад и прямого target, когда он уже виден.

## 0.9.4 — DevExpress client navigation + global backoff

- Pager больше не зависит от наличия далёкой numeric-link в DOM: основной путь использует `ASPxGridView2.GotoPage(pageIndex)`.
- Если дальний callback не подтверждён, включается последовательный fallback `1 -> 2 -> ... -> target` через `NextPage`/`PrevPage`/`GotoPage`.
- Старый Selenium DOM-click сохранён только как последний резерв для реально видимой ссылки страницы.
- Добавлен общий для details-workers backoff: 3 navigation failure за 10 секунд включают 12-секундную паузу перед новыми catalog-попытками.
- Во время backoff ошибки уже запущенных callback-ов не продлевают паузу бесконечно.
- Добавлена telemetry `CATALOG_GLOBAL_BACKOFF`; существующие 5 card-open attempts сохранены.
- Добавлены unit-тесты последовательного page path (вперёд/назад/та же страница).

## 0.9.3 — worker card-open retry

- Fallback-открытие карточки организации выполняется до 5 полных попыток.
- Каждая повторная попытка заново открывает catalog URL, применяет WARM + «Общая информация об организации», ждёт стабильный DevExpress grid, переходит на нужную страницу, заново ищет строку и кликает её.
- После зависшего клика старый DOM не переиспользуется; выполняется best-effort возврат в каталог.
- Ожидание перехода в карточку ограничено 20 сек на попытку; после таймаута выполняется следующая полная попытка.
- Добавлена телеметрия `CATALOG_ORG_OPEN_ATTEMPT_FAILED`, `CATALOG_ORG_OPEN_RETRY`, `CATALOG_ORG_OPENED`.

## v0.9.2 — semantic value cells + resilient DevExpress pager

- Исправлено смещение данных в Canvas/Spread: snapshot сохраняет пустые ячейки вокруг кода параметра.
- Formula-backed поле с пустым результатом теперь трактуется как действительно отсутствующее; parser больше не прыгает к следующей непустой подписи/инструкции.
- Value-column определяется по фактическим колонкам workbook, а не по порядковому номеру непустых значений.
- HTML-table reader ищет код параметра внутри строки и берёт value относительно найденного кода, а не абсолютного индекса строки.
- Явный номер опубликованной формы имеет приоритет над эвристикой: `1 — Общая информация об организации` больше не может повторно классифицироваться как 4.1.1 из-за пересекающихся кодов.
- Для формы 1 и 4.1.1 добавлены semantic-label fallback и проверки типа значения (телефон/e-mail/site); отсутствующее поле остаётся пустым и не подменяется соседней подписью.
- Телефоны формы 1 поддерживают корневой код `9` и дочерние `9.1`, `9.2`, ... .
- DevExpress pager: до 3 попыток перехода (Actions, native Click, JS/href), по 20 секунд; убрана избыточная проверка изменения первой строки.
- Если callback страницы всё же завис, Worker один раз полностью восстанавливает каталог, повторно применяет фильтры и повторяет переход (`CATALOG_PAGE_RETRY`).

## v0.9.1 — stable filters + modern Form 1 / Spread Canvas

- Исправлена гонка первой страницы каталога: после `#searchBtn` старый DevExpress grid больше не считается готовым; ждём фактическое изменение/перезагрузку и 3 стабильных poll подряд.
- Worker перед поиском каждой организации явно выставляет `WARM` + `Общая информация об организации`, нажимает `НАЙТИ` и ждёт стабилизированный grid.
- `PublicDisclosureInfo.aspx` и `PublicDisclosureInfoOrg.aspx` принимаются как допустимые маршруты карточки после реального клика.
- Форма `1 — Общая информация об организации` больше не ошибочно классифицируется как 4.1.1/1.0.1.
- Добавлен runtime reader GrapeCity/Wijmo Spread для Canvas TemplatePrinter (`gcSpread` / `gcWorksheetCanvas`).
- Современная форма 1 теплоснабжения читается по кодам 1, 6.1–6.3, 7–11; 4.1.1 и 1.0.1 сохранены.
- Worker pool расширен до 10; UI default — 6. `SeleniumWorkerBrowser.Dispose()` идемпотентен и всегда выполняет `Quit()` + `Dispose()` из `finally`.
- Добавлена telemetry: `WORKER_FILTERS_SUBMITTED`, `FORM_1_RUNTIME_SPREAD`, `FORM_1_PARSED`, `TEMPLATE_FORM1_FOUND`.

## v0.8 — observability, live metrics & portfolio architecture

### Added

- Корневой `ARCHITECTURE.md` на русском языке с Mermaid-схемами, описанием слоёв, catalog/details pipeline, worker pool, async/parallelism, cancellation, fault isolation и observability.
- Отдельная колонка `Стр.` в журнале monitor window: для каждой details-операции видно, с какой страницы исходного каталога пришла организация.
- Непрерывное обновление elapsed/ETA через `DispatcherTimer`, даже если между telemetry events есть длинная загрузка страницы.
- README-раздел документации со ссылкой на архитектурный документ.

### Changed

- Версия проекта повышена до `0.8.0`.
- GitHub metadata расширена акцентами на observability, bounded parallelism и portfolio documentation.

## v0.7 — parser monitoring & pipeline UX

### Added

- Отдельное WPF-окно `ParserMonitorWindow`, чтобы подробные runtime-данные не раздували основную таблицу организаций.
- Единый `IParserTelemetry` / `ParserTelemetryHub` для событий каталога и details workers.
- Стадии pipeline: регионы, навигация/фильтры/страницы каталога, очередь workers, карточка организации, форма 4.1.1, завершение/ошибка/отмена.
- Замеры времени для каждой страницы каталога и каждой организации.
- Детальные timings внутри организации: получение карточки, fallback click, извлечение ссылки 4.1.1, загрузка TemplatePrinter и HTML parse.
- Общий progress, `processed/total`, `OK/ERR`, active workers, elapsed time, ETA, среднее время на организацию и throughput `орг/мин`.
- Фильтруемый журнал по уровню и тексту, последняя ошибка и экспорт логов в CSV/TXT.
- Кнопка `Мониторинг / логи` в главном окне.

### Changed

- Sidebar pipeline главного окна теперь реагирует на реальные telemetry stages, а не только на нажатие пользовательских команд.
- Details workers публикуют worker id, исходную страницу каталога, организацию, ИНН, длительность и итоговые счётчики.
- Версия проекта повышена до `0.7.0`; repository description обновлено под monitoring/telemetry pipeline.

## 0.5.0 — contacts pipeline

### Added

- `OrganizationContactDetails` domain model.
- `IEiasOrganizationDetailsService` application contract.
- Parallel details crawler with 1–6 independent Selenium workers.
- Direct extraction of the form 4.1.1 `TemplatePrinter.aspx` URL from `openTemplateDialog(...)`.
- HTML parser for form 4.1.1 based on stable parameter codes instead of physical row numbers.
- Support for multiple organization phones (`7.1`).
- WPF columns for organization/responsible-person contacts and per-row status/error.
- Worker count and headless details mode controls in WPF.
- Unit tests for 4.1.1 link extraction and contact mapping.
- GitHub repository metadata guide and updated architecture/details documentation.

### Changed

- Catalog reader now attempts to read DevExpress row keys and populate `OrganizationId` / `DetailUrl` while pagination is already being traversed.
- The details phase no longer uses catalog navigation, `Back()`, jQuery dialog interaction, iframe switching, or clicks on workbook tabs.
- Browser creation centralized in `SeleniumDriverFactory`.
- Project metadata now identifies the repository and version `0.5.0`.

### Performance

- Catalog remains sequential because DevExpress pagination is stateful.
- Organization details are independent and processed by a long-lived browser worker pool.
- Default parallelism: 3 workers; UI limit: 6.

## 0.4.x — catalog stabilization

- WPF/MVVM migration.
- Dynamic region loading.
- Exact `#searchBtn` stage after filter selection.
- `ASPxGridView2` full pagination.
- Live pager handling to avoid stale/hidden DevExpress pager state.
- HTML snapshots parsed with AngleSharp instead of retaining grid `IWebElement` instances.

## v0.6 — catalog click fallback

- Исправлена критическая проблема details phase: отсутствие `orgId` больше не блокирует организацию.
- Возвращён проверенный механизм старой WinForms-версии: поиск строки по `Организация + ИНН + КПП` и клик первой ячейки.
- Fallback выполняется только в отдельной Selenium worker-session; основной каталог WPF и его пагинация не затрагиваются.
- Worker открывает отфильтрованный каталог напрямую, переходит на `SourcePage`, кликает организацию и получает фактический `PublicDisclosureInfoOrg.aspx?...orgId=...`.
- После успешного клика `orgId` извлекается из реального URL карточки и передаётся в модель контактов.
- Fast path по прямому `DetailUrl/orgId` сохранён; fallback используется только когда DevExpress row key недоступен.

## v0.9 — multi-form resilience & data quality

- Форма 4.1.1 остаётся главным источником контактных данных.
- Добавлен парсер формы 1.0.1 как дополнительного/fallback источника контекста.
- Карточка организации теперь возвращает список TemplatePrinter-кандидатов, а не один жёсткий URL 4.1.1.
- Worker проверяет несколько опубликованных workbook и объединяет найденные листы.
- Если 4.1.1 отсутствует, но 1.0.1 найдена, организация получает статус `Частично` вместо общей ошибки.
- Каталог fallback использует `SourcePage` как hint и умеет искать строку на соседних страницах.
- Исправлена фактически неработавшая повторная попытка поиска строки организации.
- Email 4.1.1 валидируется; URL/служебные значения уходят в data-quality warnings.
- Telemetry дополнена `Code`, `DataSource` и счётчиком partial-result.
- Monitor UI показывает OK / PART / ERR и экспортирует структурированные коды/источники в CSV.
- Основная таблица получила раскрываемые детали выбранной организации без добавления десятков колонок.
