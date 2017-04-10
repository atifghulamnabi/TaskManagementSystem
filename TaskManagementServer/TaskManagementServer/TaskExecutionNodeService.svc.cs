using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.ComponentModel.Composition.Registration;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;

namespace TaskManagementServer
{
    [ServiceBehavior(ConcurrencyMode = ConcurrencyMode.Reentrant)]
    public class TaskExecutionNodeService : ITaskExecutionNode
    {
        public void Cancel(string Id)
        {
            TaskExecutionContext.CancelTask(Id);
        }

        public void Start(List<STask> stasks)
        {
            var callback = OperationContext.Current.GetCallbackChannel<ITaskUpdateCallback>();

            foreach (var t in stasks)
                TaskExecutionContext.Start(t, callback);
        }

        [Import(typeof(IRunnableTask))]
        public IRunnableTask runnableTask;
    }

    public static class TaskExecutionContext
    {
        private readonly static ConcurrentDictionary<string, CancellationTokenSource> _cancellationSources =
            new ConcurrentDictionary<string, CancellationTokenSource>();

        internal static void CancelTask(string Id)
        {
            CancellationTokenSource tknSrc;
            if (_cancellationSources.TryGetValue(Id, out tknSrc))
                tknSrc.Cancel();
        }

        internal static void Start(STask stask, ITaskUpdateCallback callback)
        {
            try
            {
                //// Step 1.a
                //var rtasks = CompositionUtil.ContainerInstance.GetExports<IRunnableTask>();
                //// Step 1.b
                //var rtask = from t in rtasks
                //            where t.Value.GetType().FullName == stask.STaskTypeName
                //            select t.Value;

                IRunnableTask rtask = null;
                switch (stask.STaskTypeName)
                {
                    case "NumberOfEvenCTask":
                        rtask = new NumberOfEvenCTask();
                        break;
                }

                if (rtask != null)
                {
                    // Step 2
                    var cs = new CancellationTokenSource();
                    var ct = cs.Token;
                    _cancellationSources.TryAdd(stask.Id, cs);
                    // Step 3 
                    Task<object>
                      .Run(rtask.Run(ct, stask.STaskParameters), ct)
                      .ContinueWith(tes => UpdateStatus(tes, stask, callback));
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }        

        private static Action<Task<object>, STask, ITaskUpdateCallback> UpdateStatus = (t, st, cb) =>
        {
            try
            {
                STaskStatus s;
                object r = null;
                switch (t.Status)
                {
                    case TaskStatus.Canceled:
                        s = STaskStatus.Canceled;
                        break;
                    case TaskStatus.Faulted:
                        s = STaskStatus.Faulted;
                        r = t.Exception.Flatten();
                        break;
                    case TaskStatus.RanToCompletion:
                        s = STaskStatus.Completed;
                        r = t.Result;
                        break;
                    default:
                        s = STaskStatus.Faulted;
                        r = new Exception("Invalid Status");
                        break;
                }
                CancellationTokenSource cs;
                TaskExecutionContext._cancellationSources.TryRemove(st.Id, out cs);
                cb.UpdateStatus(st.Id, s, r);
            }
            catch (Exception ex)
            {
                throw ex;
                // Error handling
            }
        };
    }
    
    internal static class CompositionUtil
    {
        private readonly static Lazy<CompositionContainer> _container =
          new Lazy<CompositionContainer>(() =>
          {
              var builder = new RegistrationBuilder();
              builder.ForTypesDerivedFrom<IRunnableTask>()
               .Export<IRunnableTask>()
               .SetCreationPolicy(CreationPolicy.Any);
              var cat = new DirectoryCatalog("Extensions", builder);
              return new CompositionContainer(cat, true);
          }
        , true);

        internal static CompositionContainer ContainerInstance
        {
            get { return _container.Value; }
        }        
    }

    public interface IRunnableTask
    {
        Func<object> Run(CancellationToken? ct, params string[] taskArgs);
    }

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