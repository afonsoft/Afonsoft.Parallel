using System;

namespace Afonsoft.Parallel
{
    public abstract class WorkerBase
    {
        internal object InternalContext { get; set; }

        internal Type InternalContextType { get; set; }

        public string CorrelationState { get; internal set; }

        public bool IsCanceled { get; internal set; }

        public TimeSpan LastStartTimestamp { get; internal set; }

        public TimeSpan LastCompletionTimestamp { get; internal set; }

        public TimeSpan ProcessingTime
        {
            get
            {
                if (this.LastCompletionTimestamp < this.LastStartTimestamp)
                    return TimeSpan.FromTicks(DateTime.Now.Ticks) - this.LastStartTimestamp;
                return this.LastCompletionTimestamp - this.LastStartTimestamp;
            }
        }

        public WorkerState State { get; internal set; }

        /// <summary>
        /// Metodo com a Tarefa a ser executada pelo Workers
        /// </summary>
        public abstract void Task();

        /// <summary>
        /// Metodo de Initialize chamando antes da TASK
        /// </summary>
        public abstract void Initialize();

        /// <summary>
        /// Metodo de Terminate chamando apos da TASK
        /// </summary>
        public abstract void Terminate();
    }
}
