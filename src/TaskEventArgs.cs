using System;

namespace Afonsoft.Parallel
{
    public class TaskEventArgs : WorkerEventArgs
    {
        public TaskEventArgs(
          string correlationState,
          object context,
          TaskNotification notificationOf,
          TimeSpan startTimestamp,
          TimeSpan completionTimestamp)
          : base(correlationState, context, startTimestamp, completionTimestamp)
        {
            this.NotificationOf = notificationOf;
        }

        public TaskEventArgs(
          string correlationState,
          object context,
          TaskNotification notificationOf,
          TimeSpan startTimestamp,
          TimeSpan completionTimestamp,
          Exception exception)
          : this(correlationState, context, notificationOf, startTimestamp, completionTimestamp)
        {
            this.TaskException = exception;
        }

        public TaskNotification NotificationOf { get; internal set; }

        public Exception TaskException { get; internal set; }
    }
}
