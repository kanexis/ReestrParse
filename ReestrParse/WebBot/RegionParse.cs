using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ReestrParse.WebBot
{
    public class RegionParse
    {
        public async Task LoadDataAsync(ComboBox combo)
        {
            string url = "https://ri.eias.ru/Map.aspx";
            var optionsList = await GetSelectAsync(url);

            // Заполняем ComboBox данными
            combo.Invoke((MethodInvoker)(() =>
            {
                combo.Items.Clear();
                combo.Items.AddRange(optionsList.ToArray());
            }));
        }

        public async Task<List<string>> GetSelectAsync(string url)
        {
            return await Task.Run(() =>
            {
                var service = ChromeDriverService.CreateDefaultService();
                service.SuppressInitialDiagnosticInformation = true;
                service.HideCommandPromptWindow = true;

                var options = new ChromeOptions();
                options.AddArgument("--headless");
                options.AddArgument("--log-level=3");

                using (var driver = new ChromeDriver(service, options))
                {
                    driver.Navigate().GoToUrl(url);

                    var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(60));
                    wait.Until(d => ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").Equals("complete"));

                    var script = "return Array.from(document.querySelectorAll('#region-select option')).map(option => option.textContent.trim());";
                    var optionTexts = ((IJavaScriptExecutor)driver).ExecuteScript(script) as IReadOnlyCollection<object>;

                    var result = new List<string>();
                    foreach (var text in optionTexts)
                    {
                        string item = text.ToString();
                        if (!string.IsNullOrWhiteSpace(item))
                        {
                            result.Add(item);
                        }
                    }

                    return result;
                }
            });
        }
    }
}
