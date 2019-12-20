using System;

namespace Afonsoft.Parallel
{
    public abstract class WorkerEventArgs : EventArgs
    {
        protected WorkerEventArgs(
          string correlationState,
          object context,
          TimeSpan startTimestamp,
          TimeSpan completionTimestamp)
        {
            this.CorrelationState = correlationState;
            this.Context = context;
            this.StartTimestamp = startTimestamp;
            this.CompletionTimestamp = completionTimestamp;
        }

        public string CorrelationState { get; internal set; }

        public TimeSpan StartTimestamp { get; internal set; }

        public TimeSpan CompletionTimestamp { get; internal set; }

        public object Context { get; internal set; }
    }
}
