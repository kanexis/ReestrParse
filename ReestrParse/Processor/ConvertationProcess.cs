using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ReestrParse.Processor
{
    public class ConvertationProcess
    {
        private readonly string _sourceFolderPath;
        private readonly string _destinationFolderPath;
        private readonly Logger _logger = new Logger(); // Добавлено поле для логирования
        private const int BatchSize = 100;

        public ConvertationProcess(string sourceFolderPath, string destinationFolderPath)
        {
            _sourceFolderPath = sourceFolderPath;
            _destinationFolderPath = destinationFolderPath;
        }

        public async Task ProcessFilesInBatchesAsync(IProgress<int> progress)
        {
            int totalProgress = 0;

            try
            {
                var xlsbFiles = Directory.GetFiles(_sourceFolderPath, "*.xlsb");
                int totalXlsbFiles = xlsbFiles.Length;
                int totalFiles = totalXlsbFiles + Directory.GetFiles(_sourceFolderPath, "*.xls").Length;

                if (totalXlsbFiles > 0)
                {
                    totalProgress = await ProcessFilesBatchAsync(xlsbFiles, progress, totalProgress, totalFiles, ".xlsb");
                }

                var xlsFiles = Directory.GetFiles(_sourceFolderPath, "*.xls");
                int totalXlsFiles = xlsFiles.Length;

                if (totalXlsFiles > 0)
                {
                    totalProgress = await ProcessFilesBatchAsync(xlsFiles, progress, totalProgress, totalFiles, ".xls");
                }

                _logger.Log("Обработка всех файлов завершена.");
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка обработки: {ex.Message}");
            }
        }

        private async Task<int> ProcessFilesBatchAsync(string[] files, IProgress<int> progress, int totalProgress, int totalFiles, string fileType)
        {
            int batchStartIndex = 0;

            while (batchStartIndex < files.Length)
            {
                try
                {
                    int batchSize = Math.Min(BatchSize, files.Length - batchStartIndex);

                    await RunConsoleAppAsync(batchStartIndex, batchSize, fileType);

                    batchStartIndex += batchSize;

                    totalProgress += batchSize;
                    int progressPercentage = (int)((double)totalProgress / totalFiles * 100);
                    progress.Report(progressPercentage);
                }
                catch (Exception ex)
                {
                    _logger.Log($"Ошибка обработки пакета: {ex.Message}");
                }
            }

            return totalProgress;
        }

        private async Task RunConsoleAppAsync(int batchStartIndex, int batchSize, string fileType)
        {
            try
            {
                string currentDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string parentDirectory = Directory.GetParent(currentDirectory)?.FullName;

                var processInfo = new ProcessStartInfo
                {
                    FileName = "C:\\Users\\Администратор\\source\\repos\\ReestrParse\\ConsoleApp\\bin\\Debug\\ConsoleApp.exe",
                    Arguments = $"{batchStartIndex} {batchSize} \"{_sourceFolderPath}\" \"{_destinationFolderPath}\" {fileType} {batchStartIndex + 1}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = new Process { StartInfo = processInfo })
                {
                    process.Start();

                    Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                    Task<string> errorTask = process.StandardError.ReadToEndAsync();

                    await Task.WhenAll(outputTask, errorTask);

                    string output = await outputTask;
                    string error = await errorTask;

                    if (!string.IsNullOrEmpty(output))
                        _logger.Log(output);

                    if (!string.IsNullOrEmpty(error))
                        _logger.Log($"Ошибка: {error}");

                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        _logger.Log($"Консольное приложение завершилось с ошибкой. Код выхода: {process.ExitCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка запуска консольного приложения: {ex.Message}");
            }
        }
    }
}
