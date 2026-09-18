using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static System.Net.WebRequestMethods;

namespace ReestrParse.WebBot.Parsers
{
    public class OrganizationDataParser
    {
        private Logger logger = new Logger();

        public async Task<DataTable> ParseOrganizationDataAsync(IWebDriver driver)
        {
            return await Task.Run(() =>
            {
                var dataTable = new DataTable();
                dataTable.Columns.Add("№");
                dataTable.Columns.Add("Организация");
                dataTable.Columns.Add("ИНН");
                dataTable.Columns.Add("КПП");
                dataTable.Columns.Add("Ссылка");

                int number = 1;
                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
                var url = "https://ri.eias.ru/Discl/PublicDisclosureInfo.aspx?reg=2630&form=F_W_O_1;F_W_O_4_1_1;F_G_O_1;F_G_O_1_1_1;F_G_O_1_1_2;F_V_O_1_1;F_V_O_1_2;F_V_O_3_1_1;F_V_O_3_1_2;F_H_O_1;F_H_O_2_1_1;F_H_O_2_1_2;F_T_O_1;F_T_O_5_1_1;,F_W_O_2;F_W_O_4_1_2;&orgreg=false&razdel=null&sphere=WARM,GVS,VO,HVS&year=0&period=null&mo=&mr=";
                driver.Navigate().GoToUrl(url);
                logger.Log($"Открытие страницы: {url}");

                bool hasNextPage;

                do
                {
                    try
                    {
                        wait.Until(d => ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").Equals("complete"));
                        logger.Log("Страница загружена.");

                        var retryCount = 3;
                        while (retryCount > 0)
                        {
                            try
                            {
                                string tablePageUrl = driver.Url;

                                var table = wait.Until(d => d.FindElement(By.Id("ASPxGridView2_DXMainTable")));
                                var rows = table.FindElements(By.CssSelector("tr.dxgvDataRow"));
                                logger.Log($"Найдено {rows.Count} строк на странице {tablePageUrl}");

                                foreach (var row in rows)
                                {
                                    var cells = row.FindElements(By.TagName("td"));
                                    if (cells.Count >= 3)
                                    {
                                        var organization = cells[0].Text;
                                        var inn = cells[1].Text;
                                        var kpp = cells[2].Text;

                                        if (!dataTable.AsEnumerable().Any(r => r.Field<string>("Организация") == organization && r.Field<string>("ИНН") == inn && r.Field<string>("КПП") == kpp))
                                        {
                                            logger.Log($"Обработка организации: {organization} (ИНН: {inn}, КПП: {kpp})");

                                            // Добавляем только 3 столбца (Организация, ИНН, КПП)
                                            dataTable.Rows.Add(number++, organization, inn, kpp);
                                            logger.Log($"Данные добавлены: Организация: {organization}, ИНН: {inn}, КПП: {kpp}");
                                        }
                                    }
                                }
                                retryCount = 0;
                            }
                            catch (StaleElementReferenceException)
                            {
                                logger.Log("StaleElementReferenceException: Попытка обработать строку снова.");
                                retryCount--;
                                if (retryCount == 0)
                                    logger.Log("Превышено количество попыток при обработке строки.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Log($"Ошибка при обработке страницы: {ex.Message}");
                    }

                    // Handle pagination
                    hasNextPage = false;
                    try
                    {
                        var nextPageButton = driver.FindElement(By.CssSelector(".dxWeb_pNext"));
                        hasNextPage = nextPageButton.Displayed && nextPageButton.Enabled;

                        if (hasNextPage)
                        {
                            logger.Log("Переход на следующую страницу.");
                            nextPageButton.Click();

                            // Wait for the new page to load
                            wait.Until(d => ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").Equals("complete"));
                            wait.Until(d => d.FindElement(By.CssSelector("tr.dxgvDataRow")));  // Ensure rows are loaded

                            ((IJavaScriptExecutor)driver).ExecuteScript("window.scrollTo(0, 0);");
                            logger.Log("Страница прокручена наверх.");
                        }
                    }
                    catch (NoSuchElementException)
                    {
                        logger.Log("Кнопка 'Следующая страница' не найдена, конец страниц.");
                        hasNextPage = false;
                    }
                    catch (StaleElementReferenceException)
                    {
                        logger.Log("StaleElementReferenceException: Попытка обновить кнопку 'Следующая страница'.");
                        continue;
                    }

                } while (hasNextPage);

                logger.Log("Парсинг завершен.");
                driver.Quit();

                return dataTable;
            });
        }

    }
}
