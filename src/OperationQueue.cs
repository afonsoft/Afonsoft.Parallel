using Afonsoft.Parallel.Logger;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Afonsoft.Parallel
{
    /// <summary>
    /// Gerenciamento de Workers paralelo com Fila de controle
    /// </summary>
    /// <typeparam name="TWorker">Classe que implementa : Worker<TContext></typeparam>
    /// <typeparam name="TContext">Classe que será os parametros para a Worker</typeparam>
    public class OperationQueue<TWorker, TContext> where TWorker : Worker<TContext>, new()
    {
        private WorkerPool pool = (WorkerPool)null;
        private Queue<string> roundRobinQueue = (Queue<string>)null;
        private Queue<TContext> contextQueue = (Queue<TContext>)null;
        private bool isQueueStarted = false;
        private bool isQueueStopping = false;
        private object queueSyncObject;

        /// <summary>
        /// Initialize
        /// MaxNumberOfWorkers = Environment.ProcessorCount
        /// </summary>
        public OperationQueue()
        {
            this.QueueName = "Anonymous";
            this.NumberOfWorkers = 1;
            this.MaxNumberOfWorkers = Environment.ProcessorCount;
            this.Initialize();
        }

        /// <summary>
        /// Initialize
        /// </summary>
        /// <param name="maxNumberOfWorkers">Numero máximo de workers paralelos</param>
        public OperationQueue(int maxNumberOfWorkers)
        {
            this.QueueName = "Anonymous";
            this.NumberOfWorkers = 1;
            this.MaxNumberOfWorkers = maxNumberOfWorkers;
            this.Initialize();
        }

        /// <summary>
        /// Initialize
        /// </summary>
        /// <param name="queueName">Nome da Fila</param>
        /// <param name="maxNumberOfWorkers">Numero máximo de workers paralelos</param>
        public OperationQueue(string queueName, int maxNumberOfWorkers)
        {
            this.QueueName = queueName;
            this.NumberOfWorkers = 1;
            this.MaxNumberOfWorkers = maxNumberOfWorkers;
            this.Initialize();
        }     

        /// <summary>
        /// Nome da Fila
        /// </summary>
        public string QueueName { get; internal set; }

        /// <summary>
        /// Recomendação de numero de processos paralelos
        /// </summary>
        public int SuggestedNumberOfWorkers { get; internal set; }

        /// <summary>
        /// Total de processos
        /// </summary>
        public int NumberOfWorkers { get; internal set; }

        /// <summary>
        /// Maximo de processos paralelos
        /// </summary>
        public int MaxNumberOfWorkers { get; internal set; }

        /// <summary>
        /// Reset Event where not have more contextQueue
        /// </summary>
        public ManualResetEvent QueueEmptySignal { get; internal set; }

        /// <summary>
        /// Reset Event where not have more roundRobinQueue
        /// </summary>
        public ManualResetEvent FinishedSignal { get; internal set; }

        private void Initialize()
        {
            this.SuggestedNumberOfWorkers = Environment.ProcessorCount;
            this.QueueEmptySignal = new ManualResetEvent(true);
            this.FinishedSignal = new ManualResetEvent(false);
            this.pool = new WorkerPool(new EventHandler<TaskEventArgs>(this.WorkerPool_Pump), MaxNumberOfWorkers, new EventHandler<LoggerEventArgs>(this.EventLogger));
            this.contextQueue = new Queue<TContext>(1024);
            this.roundRobinQueue = new Queue<string>(256);
            for (int index = 0; index < this.MaxNumberOfWorkers; ++index)
            {
                this.pool.AddWorker(index.ToString(), (WorkerControllerBase)new WorkerController<TWorker>(new EventHandler<LoggerEventArgs>(this.EventLogger)));
                this.roundRobinQueue.Enqueue(index.ToString());
                WriteTraceRecord(LogLevel.Info, string.Format("OperationQueue::Initialize - RoundRobinQueue.Enqueue - {0}", index));
            }
            this.roundRobinQueue.TrimExcess();
            this.queueSyncObject = new object();
        }

        /// <summary>
        /// Enfileirar, adicionar na fila para processar
        /// </summary>
        /// <param name="context">Classe que será os paramentros</param>
        /// <returns></returns>
        public bool Enqueue(TContext context)
        {
            if (this.isQueueStopping)
            {
                WriteTraceRecord(LogLevel.Info, "OperationQueue::Enqueue() - Queue is Stopping Operation NOT Added to Execution Queue.");
                return false;
            }
            lock (this.queueSyncObject)
            {
                this.contextQueue.Enqueue(context);
                this.QueueEmptySignal.Reset();
            }
            WriteTraceRecord(LogLevel.Info, "OperationQueue::Enqueue() - Operation Added to Execution Queue.");
            if (this.isQueueStarted)
                this.PumpOperation();
            return true;
        }

        /// <summary>
        /// Iniciar os processos
        /// </summary>
        public void Start()
        {
            this.pool.Start();
            this.FinishedSignal.Reset();
            this.isQueueStopping = false;
            this.isQueueStarted = true;
            while (this.roundRobinQueue.Count > 0 && this.contextQueue.Count > 0)
                this.PumpOperation();
        }

        /// <summary>
        /// Abort
        /// </summary>
        public void Abort()
        {
            WriteTraceRecord(LogLevel.Error, "Abort requested, waiting Context Queue empty signal.");
            this.isQueueStopping = true;
            this.isQueueStarted = false;
            this.pool.Abort();
            this.isQueueStopping = false;
            Stop(true);
        }

        /// <summary>
        /// Stop
        /// </summary>
        public void Stop()
        {
            WriteTraceRecord(LogLevel.Info, "Stop requested, ignoring pending context queue items.");
            this.Stop(false);
        }

        /// <summary>
        /// Stop
        /// </summary>
        /// <param name="waitQueueCompletion">Esperar finalizar todos os processos</param>
        public void Stop(bool waitQueueCompletion)
        {
            this.isQueueStopping = true;
            if (waitQueueCompletion)
            {
                WriteTraceRecord(LogLevel.Info, "Stop requested, waiting Context Queue empty signal.");
                this.QueueEmptySignal.WaitOne();
            }
            this.isQueueStopping = false;
            this.isQueueStarted = false;

            foreach (WaitHandle[] waitHandles in this.pool.WorkersIdleSignal)
                foreach (WaitHandle waitHandle in waitHandles)
                    waitHandle.WaitOne();

            this.pool.Stop();
        }

        /// <summary>
        /// Esperar o processo ser completado
        /// </summary>
        public void WaitForOperationQueueCompletion()
        {
            this.QueueEmptySignal.WaitOne();
            this.FinishedSignal.WaitOne();
            foreach (WaitHandle[] waitHandles in this.pool.WorkersIdleSignal)
                foreach (WaitHandle waitHandle in waitHandles)
                    waitHandle.WaitOne();

            this.isQueueStopping = false;
            this.isQueueStarted = false;
        }

        private void PumpOperation()
        {
            lock (this.queueSyncObject)
            {
                if (this.contextQueue.Count > 0 && this.roundRobinQueue.Count > 0 && this.isQueueStarted)
                {
                    TContext context = default(TContext);
                    string index = string.Empty;
                    try
                    {
                        NumberOfWorkers = this.contextQueue.Count;
                        context = this.contextQueue.Dequeue();
                        index = this.roundRobinQueue.Dequeue();
                        WriteTraceRecord(LogLevel.Info, string.Format("OperationQueue::PumpOperation - RoundRobinQueue.Dequeue - {0}", index));
                        ((TWorker)this.pool[index].Worker).Context = context;
                        WriteTraceRecord(LogLevel.Info, string.Format("OperationQueue::PumpOperation() - Operation associated to Worker -> {0}", index));
                        this.pool[index].Run();
                        if (this.contextQueue.Count == 0)
                            this.QueueEmptySignal.Set();
                    }
                    catch (InvalidOperationException ex)
                    {
                        lock (this.queueSyncObject)
                        {
                            WriteTraceRecord(LogLevel.Error, string.Format("OperationQueue::PumpOperation() - Invalid Operation Exception: Context Queue Count = {0}, Exception Details: {1}", this.contextQueue.Count, ex.Message), ex);
                            this.contextQueue.Enqueue(context);
                            this.roundRobinQueue.Enqueue(index);
                            WriteTraceRecord(LogLevel.Error, string.Format("OperationQueue::PumpOperation - RoundRobinQueue.Enqueue - {0}", index));
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (this.queueSyncObject)
                        {
                            WriteTraceRecord(LogLevel.Error, string.Format("OperationQueue::PumpOperation() - Exception: Context Queue Count = {0}, Exception Details: {1}", this.contextQueue.Count, ex.Message), ex);
                            this.contextQueue.Enqueue(context);
                            this.roundRobinQueue.Enqueue(index);
                            WriteTraceRecord(LogLevel.Error, string.Format("OperationQueue::PumpOperation - RoundRobinQueue.Enqueue - {0}", index));
                        }
                    }
                }
                else if (this.contextQueue.Count == 0)
                    WriteTraceRecord(LogLevel.Info, "OperationQueue::PumpOperation() - Context Queue is Empty");
                else if (this.roundRobinQueue.Count == 0)
                    WriteTraceRecord(LogLevel.Info, "OperationQueue::PumpOperation() - Round Robin Queue is Empty");
                else if (!this.isQueueStarted)
                    WriteTraceRecord(LogLevel.Info, "OperationQueue::PumpOperation() - The Operation Pump is not enabled.");

                if (this.contextQueue.Count == 0 && this.roundRobinQueue.Count == MaxNumberOfWorkers)
                    this.FinishedSignal.Set();

            }
        }

        private void EventLogger(object sender, LoggerEventArgs e)
        {
            this.Logger?.Invoke(sender, e);
        }

        private void WorkerPool_Pump(object sender, TaskEventArgs e)
        {
            try
            {
                switch (e.NotificationOf)
                {
                    case TaskNotification.Started:
                        this.FireEvent<TaskEventArgs>(this.TaskStarted, e);
                        break;
                    case TaskNotification.Stopped:
                        this.FireEvent<TaskEventArgs>(this.TaskStopped, e);
                        break;
                    case TaskNotification.Completed:
                        WriteTraceRecord(LogLevel.Info, string.Format("OperationQueue::WorkerPool_Pump - RoundRobinQueue.Enqueue - {0}", e.CorrelationState));
                        this.FireEvent<TaskEventArgs>(this.TaskCompleted, e);
                        lock (this.queueSyncObject)
                            this.roundRobinQueue.Enqueue(e.CorrelationState);
                        this.PumpOperation();
                        break;
                    case TaskNotification.WorkerError:
                        if (null != e.TaskException)
                            WriteTraceRecord(LogLevel.Error, string.Format("OperationQueue::WorkerPool_Pump -> TaskNotification.WorkerError - Exception: {0}", e.TaskException.Message), e.TaskException);
                        WriteTraceRecord(LogLevel.Error, string.Format("OperationQueue::WorkerPool_Pump - RoundRobinQueue.Enqueue - {0}", e.CorrelationState));
                        this.FireEvent<TaskEventArgs>(this.WorkerError, e);
                        lock (this.queueSyncObject)
                        {
                            this.pool.DeleteWorker(e.CorrelationState);
                            this.pool.AddWorker(e.CorrelationState, (WorkerControllerBase)new WorkerController<TWorker>(new EventHandler<LoggerEventArgs>(this.EventLogger)));
                            this.roundRobinQueue.Enqueue(e.CorrelationState);
                        }
                        this.PumpOperation();
                        break;
                }
                WriteTraceRecord(LogLevel.Info, string.Format("OperationQueue::WorkerPool_Pump() -> RoundRobinQueue.Count: {0}, ContextQueue.Count: {1}, WorkerCount: {2}", this.roundRobinQueue.Count, this.contextQueue.Count, this.pool.WorkerCount));
            }
            catch (Exception ex)
            {
                WriteTraceRecord(LogLevel.Error, string.Format("Worker Pool Pump Error - CorrelationState: {0} -> Error: {1}", e.CorrelationState, ex.Message), ex);
            }
        }

        /// <summary>
        /// Evento de quando uma das Task é Iniciada
        /// </summary>
        public event EventHandler<TaskEventArgs> TaskStarted;

        /// <summary>
        /// Evento de quando uma das Task é Parada
        /// </summary>
        public event EventHandler<TaskEventArgs> TaskStopped;

        /// <summary>
        /// Evento de quando uma das Task é completada
        /// </summary>
        public event EventHandler<TaskEventArgs> TaskCompleted;

        /// <summary>
        /// Evento de quando uma das Task ocorre erro
        /// </summary>
        public event EventHandler<TaskEventArgs> WorkerError;

        /// <summary>
        /// Evento de logger de informações
        /// </summary>
        public event EventHandler<LoggerEventArgs> Logger;

        private void WriteTraceRecord(LogLevel logLevel, string desc)
        {
            Logger?.Invoke(this, new LoggerEventArgs(logLevel, desc));
        }
        private void WriteTraceRecord(LogLevel logLevel, string desc, Exception ex)
        {
            Logger?.Invoke(this, new LoggerEventArgs(logLevel, desc, ex));
        }

        private void FireEvent<T>(EventHandler<T> fireEvent, T eventArgs) where T : EventArgs
        {
            if (null == fireEvent)
                return;
            fireEvent(this, eventArgs);
        }
    }

    /// <summary>
    /// Gerenciamento de Workers paralelo com Fila de controle
    /// </summary>
    public class OperationQueue 
    {
        private WorkerPool pool = (WorkerPool)null;
        private Queue<string> roundRobinQueue = (Queue<string>)null;
        private Queue<Worker> contextQueue = (Queue<Worker>)null;
        private bool isQueueStarted = false;
        private bool isQueueStopping = false;
        private object queueSyncObject;

        /// <summary>
        /// Initialize
        /// MaxNumberOfWorkers = Environment.ProcessorCount
        /// </summary>
        public OperationQueue()
        {
            this.QueueName = "Anonymous";
            this.NumberOfWorkers = 1;
            this.MaxNumberOfWorkers = Environment.ProcessorCount;
            this.Initialize();
        }

        /// <summary>
        /// Initialize
        /// </summary>
        /// <param name="maxNumberOfWorkers">Numero máximo de workers paralelos</param>
        public OperationQueue(int maxNumberOfWorkers)
        {
            this.QueueName = "Anonymous";
            this.NumberOfWorkers = 1;
            this.MaxNumberOfWorkers = maxNumberOfWorkers;
            this.Initialize();
        }

        /// <summary>
        /// Initialize
        /// </summary>
        /// <param name="queueName">Nome da Fila</param>
        /// <param name="maxNumberOfWorkers">Numero máximo de workers paralelos</param>
        public OperationQueue(string queueName, int maxNumberOfWorkers)
        {
            this.QueueName = queueName;
            this.NumberOfWorkers = 1;
            this.MaxNumberOfWorkers = maxNumberOfWorkers;
            this.Initialize();
        }

        /// <summary>
        /// Nome da Fila
        /// </summary>
        public string QueueName { get; internal set; }

        /// <summary>
        /// Recomendação de numero de processos paralelos
        /// </summary>
        public int SuggestedNumberOfWorkers { get; internal set; }

        /// <summary>
        /// Total de processos
        /// </summary>
        public int NumberOfWorkers { get; internal set; }

        /// <summary>
        /// Maximo de processos paralelos
        /// </summary>
        public int MaxNumberOfWorkers { get; internal set; }
        /// <summary>
        /// Reset Event where not have more contextQueue
        /// </summary>
        public ManualResetEvent QueueEmptySignal { get; internal set; }

        /// <summary>
        /// Reset Event where not have more roundRobinQueue
        /// </summary>
        public ManualResetEvent FinishedSignal { get; internal set; }

        private void Initialize()
        {
            this.SuggestedNumberOfWorkers = Environment.ProcessorCount;
            this.QueueEmptySignal = new ManualResetEvent(true);
            this.FinishedSignal = new ManualResetEvent(false);
            this.pool = new WorkerPool(new EventHandler<TaskEventArgs>(this.WorkerPool_Pump), MaxNumberOfWorkers, new EventHandler<LoggerEventArgs>(this.EventLogger));
            this.contextQueue = new Queue<Worker>(1024);
            this.roundRobinQueue = new Queue<string>(256);
            for (int index = 0; index < this.MaxNumberOfWorkers; ++index)
            {
                this.pool.AddWorker(index.ToString(), (WorkerControllerBase)new WorkerController(new EventHandler<LoggerEventArgs>(this.EventLogger)));
                this.roundRobinQueue.Enqueue(index.ToString());
                WriteTraceRecord(LogLevel.Info, string.Format("OperationQueue::Initialize - RoundRobinQueue.Enqueue - {0}", index));
            }
            this.roundRobinQueue.TrimExcess();
            this.queueSyncObject = new object();
        }

        /// <summary>
        /// Enfileirar, adicionar na fila para processar
        /// </summary>
        /// <param name="worker">Classe que será o worker</param>
        /// <returns></returns>
        public bool Enqueue(Worker worker)
        {
            if (this.isQueueStopping)
            {
                WriteTraceRecord(LogLevel.Info, "OperationQueue::Enqueue() - Queue is Stopping Operation NOT Added to Execution Queue.");
                return false;
            }
            lock (this.queueSyncObject)
            {
                this.contextQueue.Enqueue(worker);
                this.QueueEmptySignal.Reset();
            }
            WriteTraceRecord(LogLevel.Info, "OperationQueue::Enqueue() - Operation Added to Execution Queue.");
            if (this.isQueueStarted)
                this.PumpOperation();
            return true;
        }

        /// <summary>
        /// Iniciar os processos
        /// </summary>
        public void Start()
        {
            this.pool.Start();
            this.FinishedSignal.Reset();
            this.isQueueStopping = false;
            this.isQueueStarted = true;
            while (this.roundRobinQueue.Count > 0 && this.contextQueue.Count > 0)
                this.PumpOperation();
        }

        /// <summary>
        /// Abort
        /// </summary>
        public void Abort()
        {
            WriteTraceRecord(LogLevel.Error, "Abort requested, waiting Context Queue empty signal.");
            this.isQueueStopping = true;
            this.isQueueStarted = false;
            this.pool.Abort();
            this.isQueueStopping = false;
            Stop(true);
        }

        /// <summary>
        /// Stop
        /// </summary>
        public void Stop()
        {
            WriteTraceRecord(LogLevel.Error, "Stop requested, ignoring pending context queue items.");
            this.Stop(false);
        }

        /// <summary>
        /// Stop
        /// </summary>
        /// <param name="waitQueueCompletion">Esperar finalizar todos processos</param>
        public void Stop(bool waitQueueCompletion)
        {
            this.isQueueStopping = true;
            if (waitQueueCompletion)
            {
                WriteTraceRecord(LogLevel.Error, "Stop requested, waiting Context Queue empty signal.");
                this.QueueEmptySignal.WaitOne();
            }
            this.isQueueStopping = false;
            this.isQueueStarted = false;

            foreach (WaitHandle[] waitHandles in this.pool.WorkersIdleSignal)
                foreach (WaitHandle waitHandle in waitHandles)
                    waitHandle.WaitOne();

            this.pool.Stop();
        }

        /// <summary>
        /// Esperar o processo ser completado
        /// </summary>
        public void WaitForOperationQueueCompletion()
        {
            this.QueueEmptySignal.WaitOne();
            this.FinishedSignal.WaitOne();
            foreach (WaitHandle[] waitHandles in this.pool.WorkersIdleSignal)
                foreach (WaitHandle waitHandle in waitHandles)
                    waitHandle.WaitOne();

            this.isQueueStopping = false;
            this.isQueueStarted = false;
        }

        private void PumpOperation()
        {
            lock (this.queueSyncObject)
            {
                if (this.contextQueue.Count > 0 && this.roundRobinQueue.Count > 0 && this.isQueueStarted)
                {
                    Worker context = default(Worker);
                    string index = string.Empty;
                    try
                    {
                        NumberOfWorkers = this.contextQueue.Count;
                        context = this.contextQueue.Dequeue();
                        index = this.roundRobinQueue.Dequeue();
                        WriteTraceRecord(LogLevel.Error, string.Format("OperationQueue::PumpOperation - RoundRobinQueue.Dequeue - {0}", index));
                        ((Worker)this.pool[index].Worker).Context = index.ToString();
                        this.pool[index].Worker = context;
                        WriteTraceRecord(LogLevel.Info, string.Format("OperationQueue::PumpOperation() - Operation associated to Worker -> {0}", index));
                        this.pool[index].Run();
                        if (this.contextQueue.Count == 0)
                            this.QueueEmptySignal.Set();
                    }
                    catch (InvalidOperationException ex)
                    {
                        lock (this.queueSyncObject)
                        {
                            WriteTraceRecord(LogLevel.Error, string.Format("OperationQueue::PumpOperation() - Invalid Operation Exception: Context Queue Count = {0}, Exception Details: {1}", this.contextQueue.Count, ex.Message), ex);
                            this.contextQueue.Enqueue(context);
                            this.roundRobinQueue.Enqueue(index);
                            WriteTraceRecord(LogLevel.Error, string.Format("OperationQueue::PumpOperation - RoundRobinQueue.Enqueue - {0}", index));
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (this.queueSyncObject)
                        {
                            WriteTraceRecord(LogLevel.Error, string.Format("OperationQueue::PumpOperation() - Exception: Context Queue Count = {0}, Exception Details: {1}", this.contextQueue.Count, ex.Message), ex);
                            this.contextQueue.Enqueue(context);
                            this.roundRobinQueue.Enqueue(index);
                            WriteTraceRecord(LogLevel.Error, string.Format("OperationQueue::PumpOperation - RoundRobinQueue.Enqueue - {0}", index));
                        }
                    }
                }
                else if (this.contextQueue.Count == 0)
                    WriteTraceRecord(LogLevel.Info, "OperationQueue::PumpOperation() - Context Queue is Empty");
                else if (this.roundRobinQueue.Count == 0)
                    WriteTraceRecord(LogLevel.Info, "OperationQueue::PumpOperation() - Round Robin Queue is Empty");
                else if (!this.isQueueStarted)
                    WriteTraceRecord(LogLevel.Info, "OperationQueue::PumpOperation() - The Operation Pump is not enabled.");

                if (this.contextQueue.Count == 0 && this.roundRobinQueue.Count == MaxNumberOfWorkers)
                    this.FinishedSignal.Set();

            }
        }

        private void EventLogger(object sender, LoggerEventArgs e)
        {
            this.Logger?.Invoke(sender, e);
        }

        private void WorkerPool_Pump(object sender, TaskEventArgs e)
        {
            try
            {
                switch (e.NotificationOf)
                {
                    case TaskNotification.Started:
                        this.FireEvent<TaskEventArgs>(this.TaskStarted, e);
                        break;
                    case TaskNotification.Stopped:
                        this.FireEvent<TaskEventArgs>(this.TaskStopped, e);
                        break;
                    case TaskNotification.Completed:
                        WriteTraceRecord(LogLevel.Info, string.Format("OperationQueue::WorkerPool_Pump - RoundRobinQueue.Enqueue - {0}", e.CorrelationState));
                        this.FireEvent<TaskEventArgs>(this.TaskCompleted, e);
                        lock (this.queueSyncObject)
                            this.roundRobinQueue.Enqueue(e.CorrelationState);
                        this.PumpOperation();
                        break;
                    case TaskNotification.WorkerError:
                        if (null != e.TaskException)
                            WriteTraceRecord(LogLevel.Error, string.Format("OperationQueue::WorkerPool_Pump -> TaskNotification.WorkerError - Exception: {0}", e.TaskException.Message), e.TaskException);
                        WriteTraceRecord(LogLevel.Error, string.Format("OperationQueue::WorkerPool_Pump - RoundRobinQueue.Enqueue - {0}", e.CorrelationState));
                        this.FireEvent<TaskEventArgs>(this.WorkerError, e);
                        lock (this.queueSyncObject)
                        {
                            Worker worker = (Worker)this.pool[e.CorrelationState].Worker;
                            this.pool.DeleteWorker(e.CorrelationState);
                            this.pool.AddWorker(e.CorrelationState, (WorkerControllerBase)new WorkerController(worker, new EventHandler<LoggerEventArgs>(this.EventLogger)));
                            this.roundRobinQueue.Enqueue(e.CorrelationState);
                        }
                        this.PumpOperation();
                        break;
                }
                WriteTraceRecord(LogLevel.Info, string.Format("OperationQueue::WorkerPool_Pump() -> RoundRobinQueue.Count: {0}, ContextQueue.Count: {1}, WorkerCount: {2}", this.roundRobinQueue.Count, this.contextQueue.Count, this.pool.WorkerCount));
            }
            catch (Exception ex)
            {
                WriteTraceRecord(LogLevel.Error, string.Format("Worker Pool Pump Error - CorrelationState: {0} -> Error: {1}", e.CorrelationState, ex.Message), ex);
            }
        }

        /// <summary>
        /// Evento de quando uma das Task é Iniciada
        /// </summary>
        public event EventHandler<TaskEventArgs> TaskStarted;

        /// <summary>
        /// Evento de quando uma das Task é Parada
        /// </summary>
        public event EventHandler<TaskEventArgs> TaskStopped;

        /// <summary>
        /// Evento de quando uma das Task é completada
        /// </summary>
        public event EventHandler<TaskEventArgs> TaskCompleted;

        /// <summary>
        /// Evento de quando uma das Task ocorre erro
        /// </summary>
        public event EventHandler<TaskEventArgs> WorkerError;

        /// <summary>
        /// Evento de logger de informações
        /// </summary>
        public event EventHandler<LoggerEventArgs> Logger;

        private void WriteTraceRecord(LogLevel logLevel, string desc)
        {
            Logger?.Invoke(this, new LoggerEventArgs(logLevel, desc));
        }
        private void WriteTraceRecord(LogLevel logLevel, string desc, Exception ex)
        {
            Logger?.Invoke(this, new LoggerEventArgs(logLevel, desc, ex));
        }

        private void FireEvent<T>(EventHandler<T> fireEvent, T eventArgs) where T : EventArgs
        {
            if (null == fireEvent)
                return;
            fireEvent(this, eventArgs);
        }
    }

}
