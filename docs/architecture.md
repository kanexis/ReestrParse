# Архитектура

## Правила зависимостей

`Domain` не зависит ни от WPF, ни от Selenium, ни от хранения данных.

`Application` описывает сценарии:
- загрузить регионы;
- загрузить организации региона;
- в будущем собрать детали организации;
- экспортировать результат.

`Infrastructure.Selenium` реализует эти сценарии через Selenium WebDriver.

`Wpf` взаимодействует только с Application-контрактами.

## Расширение без переписывания

Будущая БД:
- `IOrganizationReferenceStore`
- `IOrganizationDetailsStore`
- `ICrawlCheckpointStore`

Сначала можно сделать InMemory/Null реализации, затем EF Core реализации.
Use Cases при этом не меняются.

## Page Object layer

Все Selenium selectors находятся только в `Infrastructure.Selenium/Eias`.
Изменение DOM ЕИАС не должно приводить к изменениям ViewModel или Domain.
