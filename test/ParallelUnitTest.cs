using System;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Afonsoft.Parallel.UnitTest
{
    [TestClass]
    public class ParallelUnitTest
    {
        [TestMethod]
        public void QueueTestMethod()
        {
            var operationQueue = new OperationQueue<WorkerTest, WorkerContextTest>("TestQuerue", 8);
            operationQueue.Logger += (s, e) =>
            {
                Console.WriteLine($"{e.LogLevel} - {e.Description}");
            };

            operationQueue.TaskCompleted += (s, e) => { Console.WriteLine($"TaskCompleted - {e.CompletionTimestamp}"); };
            operationQueue.WorkerError += (s, e) => { Console.WriteLine($"WorkerError - {e.TaskException}"); };

            for (int i = 0; i <= 20; ++i)
                operationQueue.Enqueue(new WorkerContextTest() { Index = i.ToString() });

            operationQueue.Start();

            operationQueue.WaitForOperationQueueCompletion();
            Assert.IsTrue(true);
        }
    }


    public class WorkerTest : Worker<WorkerContextTest>
    {
        private Guid guid;

        public override void Initialize()
        {
            guid = Guid.NewGuid();
            Console.WriteLine($"{this.Context.Index} - {guid.ToString()} - Initialize");
        }

        public override void Task()
        {
            Console.WriteLine($"{this.Context.Index} - {guid.ToString()} - Start");
            Thread.Sleep(new Random().Next(1000, 10000));
            Console.WriteLine($"{this.Context.Index} - {guid.ToString()} - End");
            Thread.Sleep(1000);
        }

        public override void Terminate()
        {
            Console.WriteLine($"{this.Context.Index} - {guid.ToString()} - Terminate");
        }
    }

    public class WorkerContextTest
    {
        public string Index { get; set; }
    }
}