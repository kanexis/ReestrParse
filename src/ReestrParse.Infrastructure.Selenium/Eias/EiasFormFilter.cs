using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace ReestrParse.Infrastructure.Selenium.Eias;

/// <summary>
/// Работа с фильтром форм на странице раскрытия информации.
/// </summary>
internal static class EiasFormFilter
{
    public static void SelectGeneralOrganizationInfo(
        IWebDriver driver,
        WebDriverWait wait)
    {
        var checkbox = wait.Until(d =>
            d.FindElements(By.Id(EiasDomHints.GeneralOrganizationFormCheckboxId))
                .FirstOrDefault());

        if (checkbox is null)
        {
            throw new NoSuchElementException(
                "Не найден фильтр формы 'Общая информация об организации'.");
        }

        if (!checkbox.Selected)
        {
            // jQuery UI multiselect часто скрывает исходный checkbox, поэтому обычный
            // Selenium Click может дать ElementNotInteractableException. JS click при
            // этом вызывает штатные click/change handlers плагина.
            ((IJavaScriptExecutor)driver).ExecuteScript(
                "arguments[0].click();", checkbox);
        }

        wait.Until(d =>
            d.FindElements(By.Id(EiasDomHints.GeneralOrganizationFormCheckboxId))
                .FirstOrDefault()
                ?.Selected == true);
    }
}
