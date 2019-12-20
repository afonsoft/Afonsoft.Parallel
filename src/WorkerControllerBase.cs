using System;
using System.Threading;

namespace Afonsoft.Parallel
{
    internal abstract class WorkerControllerBase
    {
        protected WaitHandle[] controllerWaitObjects = (WaitHandle[])null;
        protected ThreadStart ts = (ThreadStart)null;
        protected Thread threadTask = (Thread)null;

        protected WorkerControllerBase()
        {
            this.WorkerIdleSignal = new ManualResetEvent(true);
            this.ManualStartSignal = new AutoResetEvent(false);
            this.ManualStopSignal = new AutoResetEvent(false);
        }

        public WorkerBase Worker { get; internal set; }

        protected abstract void TaskCapsule();

        internal void Run()
        {
            this.Worker.IsCanceled = false;
            this.ManualStopSignal.Reset();
            this.ManualStartSignal.Set();
            this.WorkerIdleSignal.Reset();
            if (null != this.threadTask)
                return;
            this.CreateWorkerThread();
        }

        protected void CreateWorkerThread()
        {
            this.controllerWaitObjects = new WaitHandle[3]
            {
        (WaitHandle) this.PoolStopSignal,
        (WaitHandle) this.ManualStopSignal,
        (WaitHandle) this.ManualStartSignal
            };
            this.ts = new ThreadStart(this.TaskCapsule);
            this.threadTask = new Thread(this.ts);
            this.threadTask.IsBackground = true;
            this.threadTask.Start();
        }

        public void Stop()
        {
            this.Worker.State = WorkerState.Stopping;
            this.Worker.IsCanceled = true;
            this.ManualStopSignal.Set();
        }

        internal ManualResetEvent PoolStartSignal { get; set; }

        internal ManualResetEvent PoolStopSignal { get; set; }

        internal AutoResetEvent ManualStartSignal { get; set; }

        internal AutoResetEvent ManualStopSignal { get; set; }

        public ManualResetEvent WorkerIdleSignal { get; internal set; }

        internal event EventHandler<TaskEventArgs> TaskStarted;

        protected void InvokeTaskStarted(TaskEventArgs eventArgs)
        {
            EventHandler<TaskEventArgs> taskStarted = this.TaskStarted;
            if (null == taskStarted)
                return;
            taskStarted((object)this, eventArgs);
        }

        internal event EventHandler<TaskEventArgs> TaskCompleted;

        protected void InvokeTaskCompleted(TaskEventArgs eventArgs)
        {
            EventHandler<TaskEventArgs> taskCompleted = this.TaskCompleted;
            if (null == taskCompleted)
                return;
            taskCompleted((object)this, eventArgs);
        }

        internal event EventHandler<TaskEventArgs> TaskStopped;

        protected void InvokeTaskStopped(TaskEventArgs eventArgs)
        {
            EventHandler<TaskEventArgs> taskStopped = this.TaskStopped;
            if (null == taskStopped)
                return;
            taskStopped((object)this, eventArgs);
        }

        internal event EventHandler<TaskEventArgs> TaskError;

        protected void InvokeTaskError(TaskEventArgs eventArgs)
        {
            EventHandler<TaskEventArgs> taskError = this.TaskError;
            if (null == taskError)
                return;
            taskError((object)this, eventArgs);
        }
    }
}
