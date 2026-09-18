# ReestrParse

Desktop-приложение для сбора реестров регулируемых организаций из ФГИС ЕИАС.

Первая цель проекта: открыть `https://ri.eias.ru/Map.aspx`, выбрать регион РФ и сферу
**«Теплоснабжение»**, пройти пагинацию списка организаций и показать найденные организации
в WPF-интерфейсе.

## Почему Selenium

ЕИАС — динамический сайт с состоянием страницы, поэтому WebDriver остаётся основным
механизмом навигации. При этом Selenium изолирован в отдельном Infrastructure-проекте:
Domain, Application и WPF не знают о `IWebDriver`.

## Главная оптимизация

Старый алгоритм:

`страница списка -> организация -> форма -> Назад -> страница 1 -> восстановить страницу N`

Новый алгоритм двухфазный:

1. **Catalog phase** — один раз пройти все страницы пагинации и собрать
   `OrganizationReference` (название, ID, URL карточки).
2. **Details phase** — позже обходить карточки напрямую по сохранённым URL/ID.

Если сайт не отдаёт прямой URL карточки, список остаётся в первой вкладке, а карточка
открывается во второй вкладке и закрывается после чтения. Позиция пагинации не теряется.

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

- **Domain** — Region, Sphere, OrganizationReference.
- **Application** — сценарии и интерфейсы.
- **Infrastructure.Selenium** — ChromeDriver, Page Objects, ожидания, пагинация.
- **Wpf** — MVVM и интерфейс оператора.

Будущие Excel/EF Core модули подключаются через новые реализации Application-контрактов,
не меняя crawler.

## Запуск

```powershell
dotnet restore ReestrParse.slnx
dotnet build ReestrParse.slnx
dotnet run --project .\src\ReestrParse.Wpf
```

Chrome должен быть установлен. Selenium Manager сам подберёт совместимый драйвер.

## Первые milestones

- [x] WPF shell
- [x] Selenium browser session
- [x] Загрузка регионов с Map.aspx
- [x] Сценарий загрузки организаций по региону и теплоснабжению
- [x] Пагинация без открытия карточек
- [ ] Уточнить selectors под актуальную разметку ЕИАС
- [ ] Карточка организации
- [ ] Форма 4.1.1
- [ ] Excel
- [ ] Checkpoint / EF Core
