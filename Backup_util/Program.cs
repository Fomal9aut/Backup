using System.Text.Json;
using Quartz;
using Quartz.Impl;
using Quartz.Impl.Matchers;
using Microsoft.Extensions.Logging;
using Backup_util.Job;

namespace Backup_util
{
    internal class Program
    {
        private const string SemaphoreName = "Global\\MyUniqueSemaphore";
        private static Semaphore semaphore;
        static async Task Main(string[] args)
        {
            using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var logger = loggerFactory.CreateLogger<Program>();

            try
            {
                bool createdNew;
                semaphore = new Semaphore(0, 1, SemaphoreName, out createdNew);

                // Если семафор уже был создан другим экземпляром программы, завершаем работу
                if (!createdNew)
                {
                    logger.LogCritical("Another instance of the program is already running. Exiting...");
                    Console.ReadKey();
                    return;
                }

                string json = File.ReadAllText(@".\settings.json");
                JsonSettings settings = JsonSerializer.Deserialize<JsonSettings>(json);

                CopyingJobListener jobListener = new CopyingJobListener(new TaskState());
                StdSchedulerFactory factory = new StdSchedulerFactory();
                IScheduler scheduler = await factory.GetScheduler();

                scheduler.ListenerManager.AddJobListener(jobListener, GroupMatcher<JobKey>.AnyGroup());

                await scheduler.Start();

                // Передаваемые параметры
                JobDataMap jobDataMap = new JobDataMap();
                jobDataMap.Add("source", settings.SourcePath);
                jobDataMap.Add("destination", settings.DestinationPath);

                // Создание задачи с параметрами
                IJobDetail job = JobBuilder.Create<CopyingJob>()
                    .UsingJobData(jobDataMap)
                    .WithIdentity("CopyingJob", "CopyingGroup")
                    .Build();

                // Создание триггера для задачи
                ITrigger trigger = TriggerBuilder.Create()
                    .WithIdentity("CopyingJobTrigger", "CopyingGroup")
                    .StartNow()
                    .WithCronSchedule(settings.CronExp) // cron выражение
                    .Build();

                logger.LogInformation("Утилита начала работу");
                await scheduler.ScheduleJob(job, trigger);

                // Ожидание завершения работы
                Console.ReadKey();

                await scheduler.Shutdown();
                semaphore.Release();
                logger.LogInformation("Утилита завершила работу");
            }
            catch (FileNotFoundException ex)
            {
                logger.LogCritical($"Файл настроек не найден: {ex.Message}");
                return;
            }
            catch (JsonException ex)
            {
                logger.LogCritical($"Ошибка при десериализации JSON: {ex.Message}");
                return;
            }
            catch (FormatException ex)
            {
                logger.LogCritical($"Ошибка в cron-выражении: {ex.Message}");
                return;
            }
            catch (Exception ex)
            {
                logger.LogCritical($"Произошла непредвиденная ошибка: {ex.Message}");
                return;
            }
            finally
            {
                semaphore.Release();
            }
        }
    }
}
