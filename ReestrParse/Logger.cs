using System;
using System.IO;

namespace ReestrParse
{
    public class Logger
    {
        private readonly string logDirectory;
        private readonly string logFilePath;

        public Logger(string logDirectoryName = "Logs")
        {
            // Директория для логов в папке с программой
            logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, logDirectoryName);

            // Создаем директорию, если ее нет
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            // Уникальное имя файла на основе даты и времени
            string logFileName = $"log_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt";
            logFilePath = Path.Combine(logDirectory, logFileName);
        }

        public void Log(string message)
        {
            // Формируем сообщение с текущей датой и временем
            string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: {message}";

            // Пишем сообщение в файл
            File.AppendAllText(logFilePath, logMessage + Environment.NewLine);

            // Дублируем сообщение в консоль
            Console.WriteLine(logMessage);
        }
    }
}