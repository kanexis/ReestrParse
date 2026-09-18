using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using SeleniumExtras.WaitHelpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Net.WebRequestMethods;

namespace ReestrParse.WebBot.Parsers
{
    public class LinkOrganizationDataParser
    {
        private IWebDriver _driver;
        private WebDriverWait _wait;
        private DataGridView _dataGridView;
        private Logger logger = new Logger();

        public LinkOrganizationDataParser(DataGridView dataGridView)
        {
            _dataGridView = dataGridView;
            InitializeDriver();
        }
        private void InitializeDriver()
        {
            _driver = new ChromeDriver();
            _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(10));
            var url = "https://ri.eias.ru/Discl/PublicDisclosureInfo.aspx?reg=2630&form=F_W_O_1;F_W_O_4_1_1;F_G_O_1;F_G_O_1_1_1;F_G_O_1_1_2;F_V_O_1_1;F_V_O_1_2;F_V_O_3_1_1;F_V_O_3_1_2;F_H_O_1;F_H_O_2_1_1;F_H_O_2_1_2;F_T_O_1;F_T_O_5_1_1;,F_W_O_2;F_W_O_4_1_2;&orgreg=false&razdel=null&sphere=WARM,GVS,VO,HVS&year=0&period=null&mo=&mr=";
            _driver.Navigate().GoToUrl(url);
        }

        public async Task AddLinksToOrganizationsAsync()
        {
            var linksDictionary = new Dictionary<string, string>();

            try
            {
                foreach (DataGridViewRow row in _dataGridView.Rows)
                {
                    if (row.IsNewRow)
                        continue;

                    var organization = row.Cells["Организация"].Value?.ToString();
                    var inn = row.Cells["ИНН"].Value?.ToString();
                    var kpp = row.Cells["КПП"].Value?.ToString();
                    var uniqueKey = $"{inn}_{kpp}";

                    bool linkFound = await SearchOrganizationAcrossPagesAsync(organization, inn, kpp);

                    if (linkFound && _driver.Url != null)
                    {
                        var link = _driver.Url;
                        linksDictionary[uniqueKey] = link;
                        logger.Log($"Ссылка для {organization} получена: {link}");

                        _driver.Navigate().Back();
                        _wait.Until(d => ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").Equals("complete"));
                    }
                    else
                    {
                        logger.Log($"Организация {organization} не найдена.");
                    }
                }

                UpdateDataGridView(linksDictionary);
            }
            catch (Exception ex)
            {
                logger.Log($"Ошибка: {ex.Message}");
            }
            finally
            {
                _dataGridView.Refresh();
                CloseDriver();
                logger.Log("Добавление ссылок завершено.");
            }
        }
        private async Task RetryMissingLinksAsync(Dictionary<string, string> linksDictionary)
        {
            try
            {
                foreach (DataGridViewRow row in _dataGridView.Rows)
                {
                    if (row.IsNewRow)
                        continue;

                    var currentLink = row.Cells["Ссылка"].Value?.ToString();
                    if (string.IsNullOrWhiteSpace(currentLink) || currentLink == "Ссылка не найдена")
                    {
                        var organization = row.Cells["Организация"].Value?.ToString();
                        var inn = row.Cells["ИНН"].Value?.ToString();
                        var kpp = row.Cells["КПП"].Value?.ToString();
                        var uniqueKey = $"{inn}_{kpp}";

                        // Попытка повторного поиска
                        bool linkFound = await SearchOrganizationAcrossPagesAsync(organization, inn, kpp);

                        if (linkFound && _driver.Url != null)
                        {
                            var link = _driver.Url;
                            linksDictionary[uniqueKey] = link;
                            logger.Log($"Повторно получена ссылка для {organization}: {link}");

                            // Возврат на основную страницу
                            _driver.Navigate().Back();
                            _wait.Until(d => ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").Equals("complete"));
                        }
                        else
                        {
                            logger.Log($"Организация {organization} повторно не найдена.");
                        }
                    }
                    else
                    {
                        // Сохраняем старую ссылку, если она уже есть
                        var inn = row.Cells["ИНН"].Value?.ToString();
                        var kpp = row.Cells["КПП"].Value?.ToString();
                        var uniqueKey = $"{inn}_{kpp}";

                        if (!linksDictionary.ContainsKey(uniqueKey))
                        {
                            linksDictionary[uniqueKey] = currentLink;
                        }
                    }
                }

                // Обновляем DataGridView после повторного поиска
                UpdateDataGridView(linksDictionary);
            }
            catch (Exception ex)
            {
                logger.Log($"Ошибка при повторном поиске: {ex.Message}");
            }
        }
        private void UpdateDataGridView(Dictionary<string, string> linksDictionary)
        {
            var missingLinks = new List<(string Inn, string Kpp, string Organization)>();

            _dataGridView.Invoke(new Action(() =>
            {
                foreach (DataGridViewRow row in _dataGridView.Rows)
                {
                    if (row.IsNewRow)
                        continue;

                    var inn = row.Cells["ИНН"].Value?.ToString();
                    var kpp = row.Cells["КПП"].Value?.ToString();
                    var organization = row.Cells["Организация"].Value?.ToString();
                    var uniqueKey = $"{inn}_{kpp}";

                    if (linksDictionary.ContainsKey(uniqueKey))
                    {
                        row.Cells["Ссылка"].Value = linksDictionary[uniqueKey];
                    }
                    else
                    {
                        row.Cells["Ссылка"].Value = "Ссылка не найдена";
                        missingLinks.Add((inn, kpp, organization));
                    }
                }
            }));

            // Запускаем повторный поиск для пропущенных ссылок
            if (missingLinks.Count > 0)
            {
                _ = RetryMissingLinksAsync(linksDictionary);
            }
        }


        private async Task<bool> SearchOrganizationAcrossPagesAsync(string organization, string inn, string kpp)
        {
            bool organizationFound = false;

            do
            {
                organizationFound = await FindOrganizationOnPageAsync(organization, inn, kpp);
                if (organizationFound)
                    return true;

            } while (await HandlePaginationAsync());

            return organizationFound;
        }

        private async Task<bool> HandlePaginationAsync()
        {
            int maxRetries = 3;
            int retryCount = 0;

            while (retryCount < maxRetries)
            {
                try
                {
                    // Ожидание исчезновения перекрывающего элемента
                    var overlay = _driver.FindElements(By.CssSelector("div[style*='z-index: 150']"));
                    int attempts = 0;
                    while (overlay.Count > 0 && overlay[0].Displayed && attempts < 5)
                    {
                        logger.Log("Ожидание исчезновения перекрывающего элемента перед переходом на следующую страницу.");
                        overlay = _driver.FindElements(By.CssSelector("div[style*='z-index: 150']"));
                        attempts++;
                    }

                    if (overlay.Count > 0 && overlay[0].Displayed)
                    {
                        logger.Log("Перекрывающий элемент не исчез после нескольких попыток.");
                        return false;
                    }

                    // Поиск и клик по кнопке "Следующая страница"
                    var nextPageButton = _wait.Until(d => d.FindElement(By.CssSelector(".dxWeb_pNext")));
                    ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].scrollIntoView(true);", nextPageButton);

                    if (nextPageButton.Displayed && nextPageButton.Enabled)
                    {
                        logger.Log("Переход на следующую страницу.");
                        nextPageButton.Click();
                        _wait.Until(d => ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").Equals("complete"));
                        return true;
                    }
                }
                catch (NoSuchElementException)
                {
                    logger.Log("Последняя страница достигнута.");
                    return false;
                }
                catch (Exception ex)
                {
                    retryCount++;
                    logger.Log($"Ошибка при переходе на следующую страницу: {ex.Message}. Попытка {retryCount} из {maxRetries}.");

                    if (retryCount < maxRetries)
                    {
                        logger.Log("Обновление текущей страницы и повтор попытки.");
                        _driver.Navigate().Refresh();
                        _wait.Until(d => ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").Equals("complete"));
                        await Task.Delay(300); // Дополнительная задержка для полной загрузки страницы
                    }
                    else
                    {
                        logger.Log("Достигнуто максимальное количество попыток обновления страницы.");
                    }
                }
            }

            return false;
        }


        private async Task<bool> FindOrganizationOnPageAsync(string organization, string inn, string kpp)
        {
            int retries = 3;

            for (int attempt = 0; attempt < retries; attempt++)
            {
                try
                {
                    var table = _wait.Until(d => d.FindElement(By.Id("ASPxGridView2_DXMainTable")));
                    var rows = table.FindElements(By.CssSelector("tr.dxgvDataRow"));

                    foreach (var row in rows)
                    {
                        var cells = row.FindElements(By.TagName("td"));
                        if (cells.Count >= 3)
                        {
                            var orgName = cells[0].Text;
                            var orgInn = cells[1].Text;
                            var orgKpp = cells[2].Text;

                            if (orgName == organization && orgInn == inn && orgKpp == kpp)
                            {
                                try
                                {
                                    _wait.Until(ExpectedConditions.ElementToBeClickable(cells[0])).Click();
                                    _wait.Until(d => ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").Equals("complete"));
                                    return true;
                                }
                                catch (Exception ex)
                                {
                                    logger.Log($"Ошибка при клике по организации {organization}: {ex.Message}");
                                }
                            }
                        }
                    }
                    break;
                }
                catch (StaleElementReferenceException)
                {
                    logger.Log($"Ошибка: элемент устарел, повтор попытки поиска ({attempt + 1} из {retries}).");
                }
                catch (Exception ex)
                {
                    logger.Log($"Ошибка при поиске: {ex.Message}");
                    break;
                }
            }

            return false;
        }

        private void CloseDriver()
        {
            _driver?.Quit();
            _driver = null;
        }
    }
}
