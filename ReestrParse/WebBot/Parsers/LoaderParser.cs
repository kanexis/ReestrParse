using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ReestrParse.WebBot.Parsers
{
    public class LoaderParser : IDisposable
    {
        private IWebDriver _driver;
        private WebDriverWait _wait;
        private readonly Logger _logger = new Logger();
        private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(20);

        public LoaderParser(string downloadFolder)
        {
            var options = new ChromeOptions();
            options.AddArgument("--disable-blink-features=PaintHolding");
            options.AddUserProfilePreference("download.default_directory", downloadFolder);
            _driver = new ChromeDriver(options);
            _wait = new WebDriverWait(_driver, _defaultTimeout);
        }

        private async Task DownloadTemplateAsync(string organizationPageUrl, string downloadFolder, int fileNumber)
        {
            try
            {
                NavigateToUrl(organizationPageUrl);

                // Нахождение и клик по элементу "get_template"
                var templateLink = FindElement(By.CssSelector(".get_template"), "Кнопка загрузки шаблона не найдена.");
                templateLink.Click();

                // Переход в iframe
                var iframe = FindElement(By.CssSelector("iframe[src*='TemplatePrinter.aspx']"), "Iframe для скачивания шаблона не найден.");
                _driver.SwitchTo().Frame(iframe);

                // Найти кнопку для скачивания и кликнуть по ней
                var downloadButton = FindElement(By.CssSelector("#dwnl_src"), "Кнопка для скачивания не найдена."); // Замените селектор на соответствующий
                downloadButton.Click();

                // Ожидание 5 секунд
                await Task.Delay(5000);
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка при скачивании шаблона: {ex.Message}");
            }
        }

        public async Task DownloadFilesFromDataGridView(DataGridView dataGridView, string downloadFolder)
        {
            int fileNumber = GetNextFileNumber(downloadFolder);

            foreach (DataGridViewRow row in dataGridView.Rows)
            {
                if (row.IsNewRow) continue;

                string organizationPageUrl = row.Cells["Ссылка"].Value?.ToString();
                if (string.IsNullOrEmpty(organizationPageUrl) || organizationPageUrl == "Ссылка не найдена")
                {
                    _logger.Log($"Пропуск строки {row.Index + 1}: Ссылка отсутствует.");
                    continue;
                }

                try
                {
                    _logger.Log($"Начало скачивания для {organizationPageUrl}");
                    await DownloadTemplateAsync(organizationPageUrl, downloadFolder, fileNumber);
                    fileNumber++;
                }
                catch (Exception ex)
                {
                    _logger.Log($"Ошибка для строки {row.Index + 1}: {ex.Message}");
                }
            }

            _logger.Log("Скачивание завершено.");
        }

        private int GetNextFileNumber(string downloadFolder)
        {
            if (!Directory.Exists(downloadFolder))
            {
                Directory.CreateDirectory(downloadFolder);
                return 1;
            }

            var existingFiles = Directory.GetFiles(downloadFolder)
                                         .Select(Path.GetFileNameWithoutExtension)
                                         .Where(name => int.TryParse(name, out _))
                                         .Select(int.Parse);

            return existingFiles.Any() ? existingFiles.Max() + 1 : 1;
        }

        private void NavigateToUrl(string url)
        {
            try
            {
                _driver.Navigate().GoToUrl(url);
                _wait.Until(d => ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").Equals("complete"));
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка при переходе по URL {url}: {ex.Message}");
            }
        }

        private IWebElement FindElement(By by, string errorMessage)
        {
            try
            {
                return _wait.Until(d => d.FindElement(by));
            }
            catch (WebDriverTimeoutException)
            {
                throw new Exception(errorMessage);
            }
        }

        public void Dispose()
        {
            _driver?.Quit();
        }
    }
}
