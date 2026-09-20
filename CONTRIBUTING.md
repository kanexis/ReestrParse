# Contributing

## Правила архитектуры

1. `Domain` должен зависеть от Selenium, WPF, AngleSharp или Persistence.
2. WPF/ViewModels не должны иметь CSS/XPath selectors.
3. Selenium selectors и EIAS-specific behavior ссылаются на `Infrastructure.Selenium`.
4. Никогда не храни `IWebElement` в переходах между страницами или обратных вызовах DevExpress.
5. Один `IWebDriver` экземпляр должен иметь только 1 работника.
6. Не проводить `catalog → detail → Back()` навигацию.
7. Поля 4.1.1 формы должны быть сопоставлены по коду параметра (`7.1`, `9`, etc.), а не HTML-номер страницы.
8. Сбой в одной организации не должен останавливать обработку оставшейся очереди.

## Ветки

Используйте сфокусированные ветви, например:

```text
feat/excel-export
feat/checkpoints
fix/form-411-parser
```

## Прежде, чем приступить

```powershell
dotnet restore ReestrParse.slnx
dotnet build ReestrParse.slnx --configuration Release
dotnet test ReestrParse.slnx --configuration Release
```

Если изменение зависит от текущего стандарта EIAS DOM, приложите к PR минимально обработанный HTML-код или добавьте модульный тест синтаксического анализатора.
