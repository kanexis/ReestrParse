# Contributing

1. Новая функциональность — отдельная ветка `feat/...`.
2. Selenium selectors не должны попадать в WPF/ViewModel.
3. Новые страницы ЕИАС оформляются отдельными Page Object / crawler-компонентами.
4. Не хранить `IWebElement` между переходами страниц — сохранять только DTO/ID/URL.
5. Не использовать `Thread.Sleep` как основной механизм ожидания; основной путь — `WebDriverWait`.
6. Перед PR: `dotnet build` и `dotnet test`.
