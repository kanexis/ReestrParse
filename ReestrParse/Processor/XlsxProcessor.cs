using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DocumentFormat.OpenXml.Drawing.Diagrams;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace ReestrParse.Processor
{
    public class XlsxProcessor
    {
        private readonly string _sourceFolderPath;
        private readonly string _destinationFilePath;
        public Logger logger = new Logger();
        private static readonly Dictionary<string, string> CategoryNames = new Dictionary<string, string>
            {
                { "Наименование системы теплоснабжения", "Теплоснабжение" },
                { "Наименование централизованной системы горячего водоснабжения", "Горячее водоснабжение" },
                { "Наименование централизованной системы водоотведения", "Водоотведение" },
                { "Наименование централизованной системы холодного водоснабжения", "Холодное водоснабжение" }
            };

        public XlsxProcessor(string sourceFolderPath, string destinationFilePath)
        {
            _sourceFolderPath = sourceFolderPath;
            _destinationFilePath = destinationFilePath;
        }

        public async Task ProcessFilesAsync(IProgress<int> progress)
        {
            try
            {
                logger.Log("Начало обработки файлов.");

                if (!Directory.Exists(_sourceFolderPath))
                {
                    logger.Log($"Папка {_sourceFolderPath} не существует.");
                    return;
                }

                var sourceFiles = Directory.GetFiles(_sourceFolderPath, "*.xlsx");
                if (sourceFiles.Length == 0)
                {
                    logger.Log("Файлы .xlsx не найдены в папке.");
                    return;
                }

                var categorizedData = CategoryNames.Values.ToDictionary(
                    categoryName => categoryName,
                    _ => new List<object[]>()
                );

                categorizedData["Неизвестная категория"] = new List<object[]>(); // Для неизвестных категорий
                var uniqueEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // Уникальные почты

                int currentRow = 2;
                for (int i = 0; i < sourceFiles.Length; i++)
                {
                    string sourceFilePath = sourceFiles[i];
                    logger.Log($"Обработка файла: {sourceFilePath}");

                    using (var sourcePackage = new ExcelPackage(new FileInfo(sourceFilePath)))
                    {
                        var sourceWorksheet = sourcePackage.Workbook.Worksheets[3]; // Лист 4 (индекс 3)
                        var (category, values) = ExtractData(sourceWorksheet, ref currentRow);

                        if (!string.IsNullOrWhiteSpace(category) && values.Length > 0)
                        {
                            string emailResponsible = values[4]?.ToString();
                            string emailOrg = values[5]?.ToString();

                            // Добавляем почты в HashSet
                            if (!string.IsNullOrWhiteSpace(emailResponsible) && uniqueEmails.Add(emailResponsible))
                            {
                                logger.Log($"Уникальная почта добавлена: {emailResponsible}");
                            }
                            else
                            {
                                logger.Log($"Повторяющаяся почта исключена: {emailResponsible}");
                                values[4] = "";
                            }

                            if (!string.IsNullOrWhiteSpace(emailOrg) && uniqueEmails.Add(emailOrg))
                            {
                                logger.Log($"Уникальная почта добавлена: {emailOrg}");
                            }
                            else
                            {
                                logger.Log($"Повторяющаяся почта исключена: {emailOrg}");
                                values[5] = "";
                            }

                            categorizedData[category].Add(values);
                        }
                        else
                        {
                            logger.Log($"Не удалось определить категорию или отсутствуют данные для строки в файле: {sourceFilePath}");
                        }
                    }

                    int progressPercentage = (int)((double)(i + 1) / sourceFiles.Length * 100);
                    progress.Report(progressPercentage);
                }

                using (var destinationPackage = new ExcelPackage())
                {
                    foreach (var category in categorizedData)
                    {
                        if (category.Value.Count == 0) continue;

                        var worksheet = destinationPackage.Workbook.Worksheets.Add(category.Key);
                        SetHeaders(worksheet);

                        int rowIndex = 2;
                        foreach (var values in category.Value)
                        {
                            for (int colIndex = 0; colIndex < values.Length; colIndex++)
                            {
                                worksheet.Cells[rowIndex, colIndex + 1].Value = values[colIndex];
                            }
                            rowIndex++;
                        }

                        FormatDestinationWorksheet(worksheet);
                    }

                    var destinationFile = new FileInfo(_destinationFilePath);
                    destinationPackage.SaveAs(destinationFile);
                    logger.Log($"Обработка завершена. Итоговый файл сохранен: {_destinationFilePath}");
                }
            }
            catch (Exception ex)
            {
                logger.Log($"Ошибка: {ex.Message}");
            }
        }

        private (string Category, object[] Values) ExtractData(ExcelWorksheet sourceWorksheet, ref int currentRow)
        {
            string contactInfo = string.Empty;
            string emailResponsible = string.Empty;
            string emailOrg = string.Empty;
            string additionalInfo = string.Empty;

            try
            {
                // Вспомогательная функция для поиска значений
                string FindValue(string fieldName, int maxRow)
                {
                    for (int row = sourceWorksheet.Dimension.Start.Row; row <= maxRow; row++)
                    {
                        var cellValue = sourceWorksheet.Cells[row, 5]?.Value?.ToString(); // Столбец E
                        if (!string.IsNullOrEmpty(cellValue) && cellValue.Trim().Equals(fieldName, StringComparison.OrdinalIgnoreCase))
                        {
                            return sourceWorksheet.Cells[row, 6]?.Value?.ToString()?.Trim(); // Столбец F
                        }
                    }
                    return null;
                }

                // Извлечение данных
                string lastName = FindValue("фамилия должностного лица", 100);
                string firstName = FindValue("имя должностного лица", 100);
                string middleName = FindValue("отчество должностного лица", 100);
                string position = FindValue("должность", 100);
                string phone = FindValue("контактный телефон", 100);

                contactInfo = $"{lastName} {firstName} {middleName}, {position}, {phone}";

                emailResponsible = FindValue("адрес электронной почты", 35) ?? "не найдено";
                emailOrg = FindValue("Адрес электронной почты регулируемой организации", 100);
                emailOrg = !string.IsNullOrWhiteSpace(emailOrg) && emailOrg.Contains("@") ? emailOrg : "не найдено";

                if (sourceWorksheet.Workbook.Worksheets.Count >= 5) // Проверяем наличие 5-го листа
                {
                    var additionalWorksheet = sourceWorksheet.Workbook.Worksheets[4]; // Лист 5 (индекс 4)
                    additionalInfo = additionalWorksheet.Cells[7, 5]?.Value?.ToString()?.Trim();
                }

                // Проверка на категорию
                if (CategoryNames.TryGetValue(additionalInfo ?? string.Empty, out var categoryName))
                {
                    var values = new object[]
                    {
                        currentRow - 1, // №
                        sourceWorksheet.Cells[12, 6]?.Value, // Организация
                        sourceWorksheet.Cells[13, 6]?.Value, // ИНН
                        contactInfo,
                        emailResponsible,
                        emailOrg,
                        additionalInfo
                    };
                    currentRow++;
                    return (categoryName, values);
                }
                else
                {
                    logger.Log($" это первая функция");
                    logger.Log($"Неизвестная категория: {additionalInfo ?? "пустое значение"}");
                    return ExtractDataALT(sourceWorksheet, ref currentRow); // Вызов альтернативного метода
                }
            }
            catch (Exception ex)
            {
                logger.Log($"Ошибка при извлечении данных: {ex.Message}");
                return ExtractDataALT(sourceWorksheet, ref currentRow); // Вызов альтернативного метода
            }
        }

        private (string Category, object[] Values) ExtractDataALT(ExcelWorksheet sourceWorksheet, ref int currentRow)
        {
            string additionalInfo = string.Empty;

            try
            {
                if (sourceWorksheet.Workbook.Worksheets.Count < 3) // Проверка наличия второго листа
                {
                    logger.Log("Второй лист отсутствует в книге Excel.");
                    return ("Ошибка", new object[0]);
                }

                var sheet2 = sourceWorksheet.Workbook.Worksheets[1]; // Лист 2 (индекс 1)

                string FindValue(string fieldName, int maxRow)
                {
                    for (int row = sheet2.Dimension.Start.Row; row <= maxRow; row++)
                    {
                        var cellValue = sheet2.Cells[row, 5]?.Value?.ToString(); // Столбец E
                        if (!string.IsNullOrEmpty(cellValue) && cellValue.Trim().Equals(fieldName, StringComparison.OrdinalIgnoreCase))
                        {
                            return sheet2.Cells[row, 6]?.Value?.ToString()?.Trim(); // Столбец F
                        }
                    }
                    return null;
                }

                string organizationName = FindValue("Наименование ЮЛ / ИП", 100) ?? "Не указано";
                string inn = FindValue("ИНН", 100) ?? "Не указано";
                string fullName = FindValue("Фамилия, имя, отчество", 100) ?? "Не указано";
                string position = FindValue("Должность", 100) ?? "Не указано";
                string phone = FindValue("Контактный телефон", 100) ?? "Не указано";
                string email = FindValue("E-mail", 100) ?? "Не указано";
                string emailOrg = FindValue("Адрес электронной почты регулируемой организации, ЕТО, ТО", 100) ?? "Не указано";

                var additionalWorksheet = sourceWorksheet.Workbook.Worksheets[7]; // Лист 5 (индекс 4)
                var cell = additionalWorksheet.Cells[8, 5];

                additionalInfo = string.IsNullOrEmpty(cell.Formula)
                    ? cell.Value?.ToString()?.Trim() // Если формулы нет, берем значение напрямую
                    : cell.Value?.ToString()?.Trim(); // Если формула есть, берем уже рассчитанное значение


                if (CategoryNames.TryGetValue(additionalInfo ?? string.Empty, out var categoryName))
                {
                    var values = new object[]
                    {
                        currentRow - 1, // №
                        organizationName,
                        inn,
                        $"{fullName}, {position}, {phone}",
                        email,
                        email,
                        additionalInfo
                    };

                    currentRow++;
                    return (categoryName, values);
                }
                else
                {
                    logger.Log($"Неизвестная категория: {additionalInfo ?? "пустое значение"}");
                    return ("Неизвестная категория", new object[0]);
                }
            }
            catch (Exception ex)
            {
                logger.Log($"Ошибка при извлечении данных со второго листа: {ex.Message}");
                return ("Ошибка", new object[0]);
            }
        }

        private void SetHeaders(ExcelWorksheet worksheet)
        {
            worksheet.Cells[1, 1].Value = "№";
            worksheet.Cells[1, 2].Value = "Организация";
            worksheet.Cells[1, 3].Value = "ИНН";
            worksheet.Cells[1, 4].Value = "ФИО и Тел ответственного";
            worksheet.Cells[1, 5].Value = "Эл. почта ответственного";
            worksheet.Cells[1, 6].Value = "Эл. почта организации";
            worksheet.Cells[1, 7].Value = "Дополнительная информация";

            for (int col = 1; col <= 7; col++)
            {
                var cell = worksheet.Cells[1, col];
                cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                cell.Style.Font.Bold = true;
            }
        }

        private void FormatDestinationWorksheet(ExcelWorksheet worksheet)
        {
            worksheet.Column(1).Width = 5;
            worksheet.Column(2).Width = 51;
            worksheet.Column(3).Width = 20;
            worksheet.Column(4).Width = 50;
            worksheet.Column(5).Width = 35;
            worksheet.Column(6).Width = 35;
            worksheet.Column(7).Width = 50;

            for (int col = 1; col <= 7; col++)
            {
                worksheet.Column(col).AutoFit();
            }
        }
    }
}