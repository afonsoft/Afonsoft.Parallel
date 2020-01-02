using Afonsoft.Parallel.Logger;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Afonsoft.Parallel
{
    internal sealed class WorkerPool : IDisposable
    {
        private readonly Thread monitorThread = null;
        private readonly List<WaitHandle> workerCompleteSignals = null;
        private int workerStartedCount = 0;
        private bool isDisposed = false;
        private readonly ManualResetEvent poolStartSignal;
        private readonly ManualResetEvent poolStopSignal;
        private readonly ManualResetEvent poolAbortSignal;
        private readonly Dictionary<string, WorkerControllerBase> workers;

        public event EventHandler<LoggerEventArgs> Logger;

        private void WriteTraceRecord(LogLevel logLevel, string desc)
        {
            Logger?.Invoke(this, new LoggerEventArgs(logLevel, desc));
        }

        public WorkerPool(
          EventHandler<TaskEventArgs> taskNotificationHandler, int maxNumberOfWorkers, EventHandler<LoggerEventArgs> logger)
        {
            this.workers = new Dictionary<string, WorkerControllerBase>(maxNumberOfWorkers);
            this.workerCompleteSignals = new List<WaitHandle>(maxNumberOfWorkers);
            this.poolStartSignal = new ManualResetEvent(false);
            this.poolStopSignal = new ManualResetEvent(false);
            this.poolAbortSignal = new ManualResetEvent(false);
            this.TaskNotificationHandler = taskNotificationHandler;
            this.Logger = logger;

            this.monitorThread = new Thread(new ThreadStart(this.WorkersMonitor));
            this.monitorThread.IsBackground = true;
            this.monitorThread.Start();

        }

        public WorkerPool(int maxNumberOfWorkers)
          : this((EventHandler<TaskEventArgs>)null, maxNumberOfWorkers, (EventHandler<LoggerEventArgs>)null)
        {
        }
        public WorkerPool(int maxNumberOfWorkers, EventHandler<LoggerEventArgs> logger)
          : this((EventHandler<TaskEventArgs>)null, maxNumberOfWorkers, logger)
        {
        }

        public int WorkerCount
        {
            get
            {
                if (null != this.workers)
                    return this.workers.Values.Count;
                return -1;
            }
        }

        public WaitHandle[] WorkerFinishedSignals
        {
            get
            {
                WaitHandle[] array = new WaitHandle[this.workerCompleteSignals.Count];
                this.workerCompleteSignals.CopyTo(array);
                return array;
            }
        }

        private void WorkersMonitor()
        {

            var monitorWaitObjects = new WaitHandle[1]
                  {
                    (WaitHandle) this.poolAbortSignal
                    };
            int num;
            do
            {
                num = WaitHandle.WaitAny(monitorWaitObjects, 10000);
                if (num == 0)
                {
                    foreach (WorkerControllerBase workerControllerBase in this.workers.Values.ToArray<WorkerControllerBase>())
                        workerControllerBase.Stop();
                    foreach (WaitHandle[] waitHandles in this.WorkersIdleSignal)
                        foreach (WaitHandle waitHandle in waitHandles)
                            waitHandle.WaitOne();
                    this.Stop();
                }
                else
                    WriteTraceRecord(LogLevel.Info, string.Format("WorkerPool::WorkersMonitor -> Unhandled monitorWaitObject Index {0}", (object)num));
            }
            while (num != 0);
        }

        public void Start()
        {
            this.poolAbortSignal.Reset();
            this.poolStopSignal.Reset();
            this.poolStartSignal.Set();
        }

        public void Stop()
        {
            this.poolStopSignal.Set();
        }

        public void Abort()
        {
            this.poolAbortSignal.Set();
            this.monitorThread.Join();
        }
        public List<WaitHandle[]> WorkersIdleSignal
        {
            get
            {
                List<WaitHandle[]> waitHandleArrayList = new List<WaitHandle[]>(64);
                List<WaitHandle> waitHandleList = new List<WaitHandle>(this.workers.Values.Count);
                int num = 0;
                foreach (WorkerControllerBase workerControllerBase in this.workers.Values)
                {
                    waitHandleList.Add((WaitHandle)workerControllerBase.WorkerIdleSignal);
                    if (++num % 16 == 0)
                    {
                        waitHandleArrayList.Add(waitHandleList.ToArray());
                        waitHandleList = new List<WaitHandle>();
                    }
                }
                if (waitHandleList.Count > 0)
                    waitHandleArrayList.Add(waitHandleList.ToArray());
                return waitHandleArrayList;
            }
        }

        public EventHandler<TaskEventArgs> TaskNotificationHandler { get; internal set; }

        public void AddWorker(string correlationState, WorkerControllerBase workerController)
        {
            this.AddWorker<object>(correlationState, workerController, (object)null);
        }

        public void AddWorker<T>(string correlationState, WorkerControllerBase workerController, T context)
        {
            WriteTraceRecord(LogLevel.Info, string.Format("WorkerPool::AddWorker -> CorrelationState:{0}, Context:{1}", (object)correlationState, (object)context == null ? (object)"<null>" : (object)context.ToString()));
            this.workers.Add(correlationState, workerController);
            
            this.workers[correlationState].TaskStarted += new EventHandler<TaskEventArgs>(this.WorkerController_TaskStarted);
            if (null != this.TaskNotificationHandler)
                this.workers[correlationState].TaskStarted += this.TaskNotificationHandler;
            
            this.workers[correlationState].TaskCompleted += new EventHandler<TaskEventArgs>(this.WorkerController_TaskCompleted);
            if (null != this.TaskNotificationHandler)
                this.workers[correlationState].TaskCompleted += this.TaskNotificationHandler;
            
            this.workers[correlationState].TaskStopped += new EventHandler<TaskEventArgs>(this.WorkerController_TaskStopped);
            if (null != this.TaskNotificationHandler)
                this.workers[correlationState].TaskStopped += this.TaskNotificationHandler;
            
            this.workers[correlationState].TaskError += new EventHandler<TaskEventArgs>(this.WorkerPool_TaskError);
            if (null != this.TaskNotificationHandler)
                this.workers[correlationState].TaskError += this.TaskNotificationHandler;
            
            if (null != (object)context)
                ((Worker<T>)this.workers[correlationState].Worker).Context = context;
            this.workers[correlationState].PoolStopSignal = this.poolStopSignal;

            this.workers[correlationState].Worker.CorrelationState = correlationState;
        }

        internal void DeleteWorker(string correlationState)
        {
            WriteTraceRecord(LogLevel.Info, string.Format("WorkerPool::DeleteWorker -> CorrelationState:{0}", (object)correlationState));
            this.workers.Remove(correlationState);
        }

        public WorkerControllerBase this[string correlationState]
        {
            get
            {
                return this.workers[correlationState];
            }
        }

        private void WorkerController_TaskStopped(object sender, TaskEventArgs e)
        {
            WriteTraceRecord(LogLevel.Info, string.Format("WorkerPool::TaskStopped - CorrelationState:{0}", (object)e.CorrelationState));
        }

        private void WorkerController_TaskCompleted(object sender, TaskEventArgs e)
        {
            WriteTraceRecord(LogLevel.Info, string.Format("WorkerPool::TaskCompleted - CorrelationState:{0}", (object)e.CorrelationState));
        }

        private void WorkerController_TaskStarted(object sender, TaskEventArgs e)
        {
            WriteTraceRecord(LogLevel.Info, string.Format("WorkerPool::TaskStarted - CorrelationState:{0}", (object)e.CorrelationState));
                if (Interlocked.Increment(ref this.workerStartedCount) == this.workers.Count)
                {
                    this.poolStartSignal.Reset();
                    WriteTraceRecord(LogLevel.Info, "WorkerPool.PoolStartEvent Reseted!");

                }
            
            WriteTraceRecord(LogLevel.Info, string.Format("WorkerPool::WorkerStarted - CorrelationState:{0} - Count:{1}", (object)e.CorrelationState, (object)this.workerStartedCount));
        }

        private void WorkerPool_TaskError(object sender, TaskEventArgs e)
        {
            WriteTraceRecord(LogLevel.Error, string.Format("WorkerPool::TaskError - CorrelationState:{0}", (object)e.CorrelationState));
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            this.Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            this.isDisposed = disposing;
        }
    }
}