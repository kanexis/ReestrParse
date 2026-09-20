# Changelog

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
