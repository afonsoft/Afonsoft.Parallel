using Afonsoft.Parallel.Logger;
using System;
using System.Threading;

namespace Afonsoft.Parallel
{
    internal class WorkerController<T> : WorkerControllerBase where T : WorkerBase, new()
    {
        private readonly T worker = default(T);
        public event EventHandler<LoggerEventArgs> Logger;

        private void WriteTraceRecord(LogLevel logLevel, string desc)
        {
            Logger?.Invoke(this, new LoggerEventArgs(logLevel, desc));
        }

        public WorkerController()
        {
            this.worker = new T();
            this.Worker = (WorkerBase)this.worker;
         }

        protected override void TaskCapsule()
        {
            WriteTraceRecord(LogLevel.Info, string.Format("WorkerController::TaskCapsule -> Correlation:{0} - Context:{1}", (object)this.worker.CorrelationState, this.worker.InternalContext));
            int num;
            do
            {
                num = WaitHandle.WaitAny(this.controllerWaitObjects);
                if (num >= 2)
                {
                    this.Worker.LastStartTimestamp = TimeSpan.FromTicks(DateTime.Now.Ticks);
                    WriteTraceRecord(LogLevel.Info, string.Format("WorkerController::Initialize -> Correlation:{0}", (object)this.worker.CorrelationState));
                    this.WorkerIdleSignal.Reset();
                    this.worker.State = WorkerState.Starting;
                    this.worker.Initialize();
                    this.worker.State = WorkerState.Started;
                    this.InvokeTaskStarted(new TaskEventArgs(this.worker.CorrelationState, this.worker.InternalContext, TaskNotification.Started, this.worker.LastStartTimestamp, this.worker.LastCompletionTimestamp));
                    try
                    {
                        this.worker.Task();
                        this.worker.LastCompletionTimestamp = TimeSpan.FromTicks(DateTime.Now.Ticks);
                        if (!this.worker.IsCanceled)
                            WriteTraceRecord(LogLevel.Info, string.Format("WorkerController::Task Perfomed Successfuly -> Correlation:{0}", (object)this.worker.CorrelationState));
                        else
                            WriteTraceRecord(LogLevel.Error, string.Format("WorkerController::Task Canceled due to Abort request -> Correlation:{0}", (object)this.worker.CorrelationState));
                        this.worker.State = WorkerState.Finishing;
                        WriteTraceRecord(LogLevel.Info, string.Format("WorkerController::Begin Terminate -> Correlation:{0}", (object)this.worker.CorrelationState));
                        this.Worker.Terminate();
                        WriteTraceRecord(LogLevel.Info, string.Format("WorkerController::End Terminate -> Correlation:{0}", (object)this.worker.CorrelationState));
                        this.worker.State = WorkerState.Finished;
                        if (!this.worker.IsCanceled)
                            this.InvokeTaskCompleted(new TaskEventArgs(this.worker.CorrelationState, this.worker.InternalContext, TaskNotification.Completed, this.worker.LastStartTimestamp, this.worker.LastCompletionTimestamp));
                    }
                    catch (Exception ex1)
                    {
                        try
                        {
                            WriteTraceRecord(LogLevel.Error, string.Format("WorkerController::WorkerError -> Correlation:{0}, Task Exception:{1}, Starting gracefully termination request.", (object)this.worker.CorrelationState, (object)ex1.ToString()));
                            this.worker.State = WorkerState.Finishing;
                            WriteTraceRecord(LogLevel.Info, string.Format("WorkerController::Begin Terminate -> Correlation:{0}", (object)this.worker.CorrelationState));
                            this.Worker.Terminate();
                            WriteTraceRecord(LogLevel.Info, string.Format("WorkerController::End Terminate -> Correlation:{0}", (object)this.worker.CorrelationState));
                            this.worker.State = WorkerState.Finished;
                        }
                        catch (Exception ex2)
                        {
                            WriteTraceRecord(LogLevel.Error, string.Format("WorkerController::WorkerError -> Correlation:{0}, gracefully termination attempt has an error {1}", (object)this.worker.CorrelationState, (object)ex2.ToString()));
                        }
                        this.worker.LastCompletionTimestamp = TimeSpan.FromTicks(DateTime.Now.Ticks);
                        this.InvokeTaskCompleted(new TaskEventArgs(this.worker.CorrelationState, this.worker.InternalContext, TaskNotification.WorkerError, this.worker.LastStartTimestamp, this.worker.LastCompletionTimestamp, ex1));
                        break;
                    }
                    finally
                    {
                        this.WorkerIdleSignal.Set();
                    }
                }
                else
                    break;
            }
            while (num > 1);
            this.WorkerIdleSignal.Set();
            this.worker.State = WorkerState.Stopped;
            this.InvokeTaskStopped(new TaskEventArgs(this.worker.CorrelationState, this.worker.InternalContext, TaskNotification.Stopped, this.worker.LastStartTimestamp, this.worker.LastCompletionTimestamp));
            this.threadTask = (Thread)null;
        }
    }
}