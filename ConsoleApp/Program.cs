using Aspose.Cells;
using System;
using System.IO;
using System.Linq;

namespace ConsoleApp
{
    class Program
    {
        private static Logger logger = new Logger();

        static void Main(string[] args)
        {
            try
            {
                // Получаем параметры: индекс текущего пакета, размер пакета, пути и тип файла
                int batchStartIndex = int.Parse(args[0]);
                int batchSize = int.Parse(args[1]);
                string sourceFolderPath = args[2];
                string destinationFolderPath = args[3];
                string fileType = args[4]; // ".xlsb" или ".xls"
                int startCounter = int.Parse(args[5]); // Начальный номер для имен файлов

                // Получаем список файлов указанного типа
                var files = Directory.GetFiles(sourceFolderPath, $"*{fileType}");

                if (!files.Any())
                {
                    logger.Log($"Файлы типа {fileType} не найдены в папке {sourceFolderPath}.");
                    return;
                }

                // Отбираем файлы для текущего пакета
                var batchFiles = files.Skip(batchStartIndex).Take(batchSize).ToList();

                if (!batchFiles.Any())
                {
                    logger.Log($"Нет файлов для обработки в текущем пакете (индекс {batchStartIndex}, размер пакета {batchSize}).");
                    return;
                }

                // Убедитесь, что папка назначения существует
                if (!Directory.Exists(destinationFolderPath))
                {
                    Directory.CreateDirectory(destinationFolderPath);
                }

                // Обрабатываем файлы пакета
                int counter = startCounter; // Используем переданный стартовый номер
                foreach (var file in batchFiles)
                {
                    try
                    {
                        // Загружаем исходный файл с использованием Aspose.Cells
                        Workbook workbook = new Workbook(file);

                        // Генерируем имя для нового файла как число
                        string destinationFileName = $"{counter}.xlsx";
                        string destinationFilePath = Path.Combine(destinationFolderPath, destinationFileName);

                        // Сохраняем файл в формате .xlsx
                        workbook.Save(destinationFilePath, SaveFormat.Xlsx);

                        logger.Log($"Файл {file} успешно конвертирован в {destinationFilePath}.");

                        counter++; // Увеличиваем номер для следующего файла
                    }
                    catch (Exception ex)
                    {
                        logger.Log($"Ошибка при обработке файла {file}: {ex.Message}");
                    }
                }

                logger.Log($"Пакет обработан: {batchFiles.Count} файлов.");
            }
            catch (Exception ex)
            {
                logger.Log($"Ошибка: {ex.Message}");
            }
        }
    }
}
