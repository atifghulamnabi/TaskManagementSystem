using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RemoteTaskClient
{
    class Program
    {
        static void Main(string[] args)
        {
            using (var c = new RemoteTaskClient<int>(@"http://localhost:5283/TaskCoordinatorService.svc"))
            {
                var tokenSource2 = new CancellationTokenSource();
                CancellationToken ct = tokenSource2.Token;
                c.AddRequest("MySimpleCTask", new string[] { "1000" });
                c.AddRequest("MySimpleCTask", new string[] { "1000" }, ct);
                var ts = c.SubmitRequests();
                Task.WaitAll(ts);
                foreach (var t in ts)
                    Console.WriteLine(t.Result);
            }
        }

        public interface IRunnableTask
        {
            Func<object> Run(CancellationToken? ct, params string[] taskArgs);
        }

        public class MySimpleCTask : IRunnableTask
        {
            public Func<object> Run(CancellationToken? ct, params string[] taskArgs)
            {
                var j = int.Parse(taskArgs[0]);
                var z = 0;
                return (() =>
                {
                    for (int i = 0; i < j; i++)
                    {
                        if (i % 2 != 0)
                        {
                            z++;
                            ct.Value.ThrowIfCancellationRequested();
                        }
                    }
                    return z;
                });
            }
        }
    }
}
