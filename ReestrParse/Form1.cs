using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using ReestrParse.Processor;
using ReestrParse.WebBot;
using ReestrParse.WebBot.ChooseStrategy;
using ReestrParse.WebBot.Parsers;
using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Action = System.Action;
using DataTable = System.Data.DataTable;

namespace ReestrParse
{
    public partial class Form1 : Form
    {
        private RegionParse regionParse;
        private IWebDriver driver;
        private string selectedFolderPath;
        private string selectedRegion;
        private Logger logger = new Logger();
        public Form1()
        {
            InitializeComponent();
            driver = InitializeWebDriver();
            this.Load += new System.EventHandler(this.Form1_Load);
        }
        private IWebDriver InitializeWebDriver()
        {
            // Инициализация драйвера Chrome при создании формы
            var service = ChromeDriverService.CreateDefaultService();
            service.SuppressInitialDiagnosticInformation = true;
            service.HideCommandPromptWindow = true;

            var options = new ChromeOptions();
            options.AddArgument("--headless");
            options.AddArgument("--log-level=3");

            return new ChromeDriver(service, options);
        }

        private void selectFolderButton_Click(object sender, EventArgs e)
        {
            // Создаем диалог для выбора папки
            using (FolderBrowserDialog folderDialog = new FolderBrowserDialog())
            {
                // Показываем диалог и проверяем, была ли выбрана папка
                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    selectedFolderPath = folderDialog.SelectedPath; // Сохраняем путь в переменную
                    string newPath = Path.Combine(selectedFolderPath, $"{selectedRegion}");
                    // Проверяем, существует ли папка назначения и создаем, если нет
                    if (!Directory.Exists(newPath))
                    {
                        Directory.CreateDirectory(newPath);
                    }
                    selectedFolderPath = newPath;
                    MessageBox.Show($"Путь выбран: {newPath}");
                }
                else
                {
                    MessageBox.Show("Выберите папку для скачивания.");
                }
            }
        }
        private async void makeFileButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (!Directory.Exists(selectedFolderPath))
                {
                    Directory.CreateDirectory(selectedFolderPath);
                }

                string destinationFolderPath = Path.Combine(selectedFolderPath, "converted");
                if (!Directory.Exists(destinationFolderPath))
                {
                    Directory.CreateDirectory(destinationFolderPath);
                }

                string instanceFolderPath = Path.Combine(selectedFolderPath, "instance");
                if (!Directory.Exists(instanceFolderPath))
                {
                    Directory.CreateDirectory(instanceFolderPath);
                }

                string instance = Path.Combine(instanceFolderPath, $"Реестр_{selectedRegion}.xlsx");

                using (var progressForm = new ProgressForm())
                {
                    progressForm.Show();
                    IProgress<int> progress = new Progress<int>(value =>
                    {
                        progressForm.UpdateProgress(value);
                    });

                    // Initialize ConvertationProcess with the source and destination paths
                    var convertationProcess = new ConvertationProcess(selectedFolderPath, destinationFolderPath);

                    // Track total progress by monitoring batch completion
                    await Task.Run(async () =>
                    {
                        // Run the conversion in batches
                        await convertationProcess.ProcessFilesInBatchesAsync(progress);

                        // Update progress to 100% when done
                        progress.Report(100);
                    });

                    // Process the files into a single .xlsx file after conversion is complete
                    var processor = new XlsxProcessor(destinationFolderPath,instance);
                    await Task.Run(async() => processor.ProcessFilesAsync(progress));

                    MessageBox.Show("Все файлы успешно обработаны и сохранены в итоговом файле.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}");
            }
        }

        private async void downloadButton_Click(object sender, EventArgs e)
        {
            // Показываем форму прогресса
            using (var progressForm = new ProgressForm())
            {
                progressForm.Show();

                // Прогресс-объект для обновления прогресс-бара
                IProgress<int> progress = new Progress<int>(value =>
                {
                    progressForm.UpdateProgress(value); // Обновляем прогресс-бар
                });

                try
                {
                    // Проверка пути для сохранения
                    if (string.IsNullOrEmpty(selectedFolderPath))
                    {
                        MessageBox.Show("Пожалуйста, выберите папку для скачивания.");
                        return;
                    }

                    if (!Directory.Exists(selectedFolderPath))
                    {
                        MessageBox.Show("Папка для скачивания не существует. Пожалуйста, выберите правильную папку.");
                        return;
                    }

                    // Создаем экземпляр для скачивания
                    using (var downloader = new LoaderParser(selectedFolderPath))
                    {
                        int counter = 0;
                        int totalLinks = dataGridView1.Rows
                            .Cast<DataGridViewRow>()
                            .Count(row => !row.IsNewRow && row.Cells["Ссылка"].Value != null && row.Cells["Ссылка"].Value.ToString() != "Ссылка не найдена"); // Считаем строки с данными

                        if (totalLinks == 0)
                        {
                            MessageBox.Show("Нет ссылок для скачивания.");
                            return;
                        }

                        logger.Log($"Найдено {totalLinks} ссылок");
                        try
                        {
                            // Логируем процесс скачивания
                            logger.Log($"Начало скачивания");

                            // Скачиваем файл
                            await downloader.DownloadFilesFromDataGridView(dataGridView1, selectedFolderPath);

                            counter++;
                            int progressValue = (int)((double)counter / totalLinks * 100);
                            progress.Report(progressValue); // Обновляем прогресс
                        }
                        catch (Exception downloadEx)
                        {
                            // Логируем ошибку для конкретной ссылки
                            logger.Log($"Ошибка при скачивании: {downloadEx.Message}");
                        }


                        logger.Log("Все доступные файлы успешно загружены.");
                    }
                }
                catch (Exception ex)
                {
                    logger.Log($"Общая ошибка процесса скачивания: {ex.Message}");
                }
                finally
                {
                    // Закрываем форму прогресса
                    progressForm.Close();
                }
            }
        }


        private async void additionalDataButton_Click(object sender, EventArgs e)
        {
            using (var loadingForm = new LoadingForm())
            {
                loadingForm.Show();

                // Ожидаем завершения асинхронной операции, чтобы не блокировать интерфейс
                var linkParser = new LinkOrganizationDataParser(dataGridView1);
                await Task.Run(async() => linkParser.AddLinksToOrganizationsAsync());  // Вызываем асинхронный метод для получения ссылок
                loadingForm.Close();
            }
        }

        private async void comboBox2_SelectedIndexChanged(object sender, EventArgs e)
        {
            dataGridView1.DataSource = null;

            selectedRegion = comboBox2.SelectedItem?.ToString();
            if (selectedRegion != null)
            {
                // Создаем форму загрузки
                using (var loadingForm = new LoadingForm())
                {
                    loadingForm.Show(); // Показываем форму загрузки

                    // Запускаем задачу для выполнения загрузки данных в фоновом потоке
                    DataTable organizations = await Task.Run(async () =>
                    {
                        // Определяем стратегию и переходим на нужную страницу
                        IRegionNavigationStrategy strategy = new RegionNavigationStrategyFactory().GetStrategy(selectedRegion);
                        strategy.Navigate(driver);

                        var parser = new OrganizationDataParser();
                        // Загружаем данные без ссылок
                        var dataTable = await parser.ParseOrganizationDataAsync(driver);  // Обратите внимание на асинхронность здесь

                        return dataTable; // Возвращаем данные в основной поток
                    });

                    // Закрываем форму загрузки
                    loadingForm.Close();

                    // Заполняем DataGridView на основном потоке
                    dataGridView1.Invoke(new Action(() =>
                    {
                        dataGridView1.DataSource = organizations;
                    }));
                }
            }
        }
        private async void Form1_Load(object sender, EventArgs e)
        {
            using (var loadingForm = new LoadingForm())
            {
                await Task.Delay(500);
                loadingForm.Show(); // Показываем форму загрузки

                // Запускаем загрузку данных асинхронно
                regionParse = new RegionParse();

                // Ждем завершения загрузки данных
                await Task.WhenAll(regionParse.LoadDataAsync(comboBox2));

                // Закрываем форму загрузки
                loadingForm.Close();
            }
        }
    }
}
