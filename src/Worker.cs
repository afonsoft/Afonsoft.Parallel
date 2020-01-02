namespace Afonsoft.Parallel
{
    /// <summary>
    /// Classe para o funcionamento do Workers (Processos)
    /// </summary>
    /// <typeparam name="T">Objeto de parametros para o Workers</typeparam>
    public abstract class Worker<T> : WorkerBase
    {
        /// <summary>
        /// O objeto Context é o objeto de parametro do Workers
        /// </summary>
        public T Context
        {
            get
            {
                return (T)this.InternalContext;
            }
            internal set
            {
                this.InternalContextType = typeof(T);
                this.InternalContext = (object)value;
            }
        }
    }

    /// <summary>
    /// Classe para o funcionamento do Workers (Processos)
    /// </summary>
    public abstract class Worker : Worker<string>
    {
        
    }

    internal class FakeWorker : Worker
    {
        public override void Initialize()
        {
            
        }

        public override void Task()
        {
            
        }

        public override void Terminate()
        {
            
        }
    }
}
