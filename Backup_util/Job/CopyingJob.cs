using Microsoft.Extensions.Logging;
using Quartz;
using System.Security.Cryptography;


namespace Backup_util.Job
{
    internal class CopyingJob : IJob
    {
        private readonly ILogger _logger = LoggerFactory.Create(builder =>
            builder.AddConsole())
            .CreateLogger("CopyingJob");
        public Task Execute(IJobExecutionContext context)
        {
            try
            {
                JobDataMap dataMap = context.JobDetail.JobDataMap;

                string sourceFolderPath = dataMap.GetString("source");

                string destination = dataMap.GetString("destination");

                string destinationBaseFolder = Path.Combine(destination, "base");

                if (!Directory.Exists(destinationBaseFolder))
                {
                    _logger.LogInformation("Начато первичное копирование");

                    Directory.CreateDirectory(destinationBaseFolder);
                    CopyDirectory(sourceFolderPath, destinationBaseFolder);

                    _logger.LogInformation("Первичное копирование завершено");
                }
                else
                {
                    _logger.LogInformation("Начато инкрементальное копирование");
                    // собираем все файлы
                    var sourceFolderFiles = Directory.GetFiles(sourceFolderPath, "*", SearchOption.AllDirectories);

                    var destinationFoldersNames = Directory.GetDirectories(destination, "*", SearchOption.TopDirectoryOnly);

                    // очередь файлов на создание их копии
                    var targetFilesPaths = new Stack<string>();

                    foreach (var fileSourcePath in sourceFolderFiles)
                    {
                        string newestCopy = FindNewestCopy(destinationFoldersNames, fileSourcePath, sourceFolderPath);

                        // если ни одной копии нет, значит файл новый
                        if (newestCopy == "")
                        {
                            targetFilesPaths.Push(fileSourcePath);
                            _logger.LogInformation($"Файл был создан: {fileSourcePath}");
                        }
                        else
                        {
                            // если файл отличается от своей новейшей копии, значит был изменен
                            if (!CompareFilesMD5(fileSourcePath, newestCopy))
                            {
                                targetFilesPaths.Push(fileSourcePath);
                                _logger.LogInformation($"Файл был изменен: {fileSourcePath}");
                            }
                        }
                    }

                    if (targetFilesPaths.Count > 0)
                    {
                        // создали папку с инкрементом и по этому пути копируем новые и измененные файлы
                        var incFolderPath = CreateIncFolder(destination);
                        CopyModifiedFiles(targetFilesPaths, incFolderPath, sourceFolderPath);
                    }

                    _logger.LogInformation("Инкрементальное копирование завершено успешно");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex.ToString());
            }
            return Task.CompletedTask;
        }


        private void CopyModifiedFiles(Stack<string> targetFiles, string incFolderPath, string source)
        {
            while (targetFiles.Count > 0)
            {
                var fullSourcePath = targetFiles.Pop();

                var fullIncFolderPath = fullSourcePath.Replace(source, incFolderPath);

                var fullIncFolderPathNoFile = fullIncFolderPath.Replace($"\\{new FileInfo(fullIncFolderPath).Name}", "");

                if (!Directory.Exists(fullIncFolderPathNoFile))
                    Directory.CreateDirectory(fullIncFolderPathNoFile);

                try
                {
                    File.Copy(fullSourcePath, fullIncFolderPath, true);
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.LogWarning($"нет разрешений на копирование файла: {fullSourcePath}");
                    continue;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"произошла непредвиденная ошибка: {ex.Message}");
                    continue;
                }
                _logger.LogInformation($"Файл скопирован успешно: {fullSourcePath}");
            }
        }

        private string FindNewestCopy(string[] destFoldersNames, string fileSourcePath, string source)
        {
            try
            {
                List<string> targetCopiesPaths = new List<string>();
                foreach (var folder in destFoldersNames)
                {
                    string fileRelativePath = fileSourcePath.Replace(source, folder);

                    if (File.Exists(fileRelativePath))
                    {
                        targetCopiesPaths.Add(fileRelativePath);
                    }
                }

                if (targetCopiesPaths.Count != 0)
                {
                    return targetCopiesPaths
                        .OrderBy(x => new FileInfo(x).CreationTime)
                        .Last();
                }
                return "";
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Ошибка при определении новейшей копии файла: {ex.Message}");
            }
            return "";
        }


        // будем вычислять изменения в файлах через сравнение хэшей
        private bool CompareFilesMD5(string source, string dest)
        {
            string sourceHash = ComputeFileHash(source);
            string destHash = ComputeFileHash(dest);

            return sourceHash == destHash;
        }

        // вычисляем хэш файла 
        private string ComputeFileHash(string filePath)
        {
            using (var stream = File.OpenRead(filePath))
            {
                using (var md5 = MD5.Create())
                {
                    byte[] hashBytes = md5.ComputeHash(stream);
                    return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                }
            }
        }

        // создаем папку инкрементальной копии
        private string CreateIncFolder(string destination)
        {
            DateTime date = DateTime.Now;
            // поправить название папок
            string incFolderName = $"inc_{date.Year}_{date.Month}_{date.Day}_{date.Hour}_{date.Minute}_{date.Second}";
            destination = Path.Combine(destination, incFolderName);
            Directory.CreateDirectory(destination);

            _logger.LogInformation($"Инкрементальная папка успешно создана: {incFolderName}");
            return destination;
        }

        //рекурсивно копируем папки с файлами
        private void CopyDirectory(string sourceDir, string destinationDir)
        {
            try
            {
                if (!Directory.Exists(destinationDir))
                {
                    Directory.CreateDirectory(destinationDir);
                }

                string[] files = Directory.GetFiles(sourceDir);
                foreach (string file in files)
                {
                    string fileName = Path.GetFileName(file);
                    string destFile = Path.Combine(destinationDir, fileName);

                    File.Copy(file, destFile, true);
                    _logger.LogInformation($"Файл успешно скопирован в первичную копию: {file}");
                }

                string[] dirs = Directory.GetDirectories(sourceDir);
                foreach (string dir in dirs)
                {
                    string dirName = Path.GetFileName(dir);
                    string destDir = Path.Combine(destinationDir, dirName);

                    CopyDirectory(dir, destDir);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"ошибка при создании первичной копии {ex.Message}");
            }
        }
    }
}
