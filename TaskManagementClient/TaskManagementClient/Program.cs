using System;
using System.Threading;
using System.Threading.Tasks;
using TaskManagementServer;

namespace TaskManagementClient
{
    class Program
    {
        static void Main(string[] args)
        {
            using (var c = new RemoteTaskClient<int>(@"http://localhost:5283/TaskCoordinatorService.svc"))
            {
                var tokenSource2 = new CancellationTokenSource();
                CancellationToken ct = tokenSource2.Token;
                
                for (int i = 0; i < 5; i++)
                {
                    c.AddRequest("NumberOfEvenCTask", new string[] { "777" });
                    c.AddRequest("NumberOfEvenCTask", new string[] { "500" }, ct);
                }
                
                var ts = c.SubmitRequests();
                
                Task.WaitAll(ts); // Wait until all tasks are completed.
                //int index=Task.WaitAny(ts); //user can obtain the result of task anytime, once the task is finished

                int n = 0;
                foreach (var t in ts)
                    Console.WriteLine(t.Result.ToString() + " - " + (++n).ToString());
                Console.ReadLine();
            }
        }        
    }
}
