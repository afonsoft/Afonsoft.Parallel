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
            operationQueue.TaskStarted += (s, e) => { Console.WriteLine($"TaskStarted - {e.StartTimestamp} - {e.Context.ToString()}"); };
            operationQueue.TaskCompleted += (s, e) => { Console.WriteLine($"TaskCompleted - {e.CompletionTimestamp} - {e.Context.ToString()}"); };
            operationQueue.WorkerError += (s, e) => { Console.WriteLine($"WorkerError - {e.TaskException} - {e.Context.ToString()}"); };

            for (int i = 0; i <= 100; ++i)
                operationQueue.Enqueue(new WorkerContextTest() { Index = i.ToString() });

            operationQueue.Start();

            operationQueue.WaitForOperationQueueCompletion();
            Assert.IsTrue(true);
        }
    }


    public class WorkerTest : Worker<WorkerContextTest>
    {
        public override void Initialize()
        {
            Console.WriteLine($"{this.Context.Index} - Initialize");
        }

        public override void Task()
        {
            Thread.Sleep(new Random().Next(1000, 10000));
        }

        public override void Terminate()
        {
            Console.WriteLine($"{this.Context.Index} - Terminate");
        }
    }

    public class WorkerContextTest
    {
        public string Index { get; set; }

        public override string ToString()
        {
            return Index;
        }
    }
}