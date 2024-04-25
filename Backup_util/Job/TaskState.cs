namespace Backup_util.Job
{
    public class TaskState
    {
        private readonly object lockObj = new object();
        private bool isExecuting = false;
        public bool IsExecuting => isExecuting;
        public void SetExecuting(bool value)
        {
            lock (lockObj)
            {
                isExecuting = value;
            }
        }
    }
}
