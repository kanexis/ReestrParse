# Changelog

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
