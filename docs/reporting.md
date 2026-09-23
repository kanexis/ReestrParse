# Excel reporting and multi-region mode

## Output schema

Public XLSX output intentionally contains only three columns:

1. `Наименование`
2. `ИНН`
3. `Email`

Email priority is: **manager → organization → responsible/other contact**. The parser keeps all diagnostic contact fields internally, but the public registry does not expose them as extra columns.

## File modes

### One file per region

`Реестр_<регион>_Теплоснабжение_<yyyy-MM-dd>.xlsx`

Each completed region is written immediately. Re-running on the same day creates a suffix (`_2`, `_3`, ...), so an existing report is not overwritten accidentally.

### One global workbook

`Реестр_Теплоснабжение_<yyyy-MM-dd>.xlsx`

Each region is a separate worksheet. Worksheet names are sanitized and limited to the Excel 31-character limit. With autosave enabled, the same workbook is rebuilt after each successfully completed region.

## Row filtering

Before export the operator can:

- exclude a specific organization using `В отчёт`;
- exclude organizations with no email;
- exclude failed organization details;
- keep or remove these filters independently.

Rows are de-duplicated by `ИНН + Наименование` and sorted by organization name.

## Multi-region execution

Regions are processed sequentially. Selenium parallelism is only used inside the current region:

`region catalog → organization details → XLSX checkpoint → next region`

This avoids multiplying catalog traffic by the number of regions.

Controls:

- **Pause / resume** — pause is applied at the nearest safe point between heavy Selenium phases;
- **Skip current region** — cancels only the current region and continues the queue;
- **Stop** — cancels the whole batch;
- **Continue after region error** — controls whether one bad region aborts the queue.

The current implementation resumes within the same application session. Persistent resume after application restart is intentionally not implemented yet; that can be added later with a small checkpoint file containing completed/skipped region IDs and the active report path.

## Logger modes

`Основные события` is the default view and keeps only high-value information: warnings/errors, retry/backoff, batch/report events and stage completions.

`Подробный лог` exposes the full UI telemetry stream. CSV export uses a separate backing buffer (up to 100,000 events), so reducing the visible table does not remove diagnostic data.
