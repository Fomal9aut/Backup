using Quartz;

namespace Backup_util.Job
{
    internal class CopyingJobListener : IJobListener
    {
        public string Name => "CopyingJobListener";

        private readonly TaskState _state;
        public CopyingJobListener(TaskState state)
        {
            _state = state;
        }
        public Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            if (_state.IsExecuting)
            {
                // Отмена выполнения задачи, если предыдущая еще выполняется
                cancellationToken.ThrowIfCancellationRequested();
            }
            else
            {
                // Установка флага в true перед выполнением задачи
                _state.SetExecuting(true);
            }
            return Task.CompletedTask;
        }
        public Task JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
        {
            _state.SetExecuting(false);
            return Task.CompletedTask;
        }
    }
}
