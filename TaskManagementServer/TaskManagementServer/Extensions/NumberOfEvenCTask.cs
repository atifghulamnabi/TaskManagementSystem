using System;
using System.ComponentModel.Composition;
using System.Threading;

namespace TaskManagementServer.Extensions
{
    [PartCreationPolicy(CreationPolicy.NonShared)]
    [Export(typeof(IRunnableTask))]
    public class NumberOfEvenCTask : IRunnableTask
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