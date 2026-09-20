# Contributing

## Architecture rules

1. `Domain` must not depend on Selenium, WPF, AngleSharp or persistence.
2. WPF/ViewModels must not contain CSS/XPath selectors.
3. Selenium selectors and EIAS-specific behavior belong to `Infrastructure.Selenium`.
4. Never store `IWebElement` across page transitions or DevExpress callbacks.
5. One `IWebDriver` instance may be owned by only one worker at a time.
6. Do not reintroduce `catalog → detail → Back()` navigation.
7. Form 4.1.1 fields must be mapped by parameter code (`7.1`, `9`, etc.), not by HTML row number.
8. A failure for one organization must not stop processing the remaining queue.

## Branches

Use focused branches, for example:

```text
feat/excel-export
feat/checkpoints
fix/form-411-parser
```

## Before PR

```powershell
dotnet restore ReestrParse.slnx
dotnet build ReestrParse.slnx --configuration Release
dotnet test ReestrParse.slnx --configuration Release
```

If a change depends on current EIAS DOM, attach a minimal sanitized HTML fixture to the PR or add a parser unit test.
