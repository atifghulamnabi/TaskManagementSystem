using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ServiceModel;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.Remoting.Channels;

namespace TaskManagementServer
{
    [ServiceBehavior(ConcurrencyMode = ConcurrencyMode.Reentrant)]
    public class TaskCoordinatorService : ITaskCoordinator
    {
        public bool CancelTask(string Id)
        {
            return CoordinatorContext.CancelTask(Id);
        }

        public void SubmitRequest(List<STask> stasks)
        {
            CoordinatorContext.EnqueueRequestsInRequestQ(stasks);
        }
    }

    [ServiceBehavior(ConcurrencyMode = ConcurrencyMode.Reentrant)]
    public class CallbackHandler : ITaskUpdateCallback
    {
        public void UpdateStatus(string id, STaskStatus status, object result)
        {
            CoordinatorContext.HandleClientUpdate(id, status, result);
        }
    }

    public static class CoordinatorContext
    {
        private static readonly ConcurrentDictionary<string, STaskInfo> _submissionTracker = new ConcurrentDictionary<string, STaskInfo>();
        private static readonly ConcurrentQueue<STaskInfo> _requestQ = new ConcurrentQueue<STaskInfo>();

        static CoordinatorContext()
        {
            Submitter(1000);
        }

        internal static bool CancelTask(string Id)
        {
            STaskInfo info;
            if (_submissionTracker.TryGetValue(Id, out info) && info.ExecutionRequestChannel != null)
            {
                info.ExecutionRequestChannel.Cancel(Id);
                return true;
            }
            return false;
        }

        internal static List<STaskInfo> GetTasksFromRequestQ()
        {
            var ret = new List<STaskInfo>();
            var maxSTasksPerRequest = int.Parse(Properties.Resources.MaxSTasksPerRequest); //From a configuration            
            var maxNumberOfTasks = int.Parse(Properties.Resources.MaxNumberOfTasks); //From a configuration
            
            var countByType = (from t in _submissionTracker.Values
                               group t by t.ClientRequest.STaskTypeName into g
                               select new
                               {
                                   TypeName = g.Key,
                                   Count = g.Count()
                               });
            var count = countByType.Sum(c => c.Count); // Count of submitted or executing tasks
            
            for (int i = 0; i < maxSTasksPerRequest; i++)
            {
                STaskInfo info;
                if (count + i == maxNumberOfTasks || !_requestQ.TryDequeue(out info))
                    return ret;

                var countTT = (from tt in countByType
                               where tt.TypeName == info.ClientRequest.STaskTypeName
                               select tt.Count).SingleOrDefault() +
                   ret.Where((rt) => rt.ClientRequest.STaskTypeName ==
                   info.ClientRequest.STaskTypeName)
                   .Count(); // Count of submitted or executing tasks of
                             // the type of the current item

                if (countTT == GetMaxNumberOfTasksByType(info.ClientRequest.STaskTypeName))
                {
                    _requestQ.Enqueue(info);
                }
                else ret.Add(info);
            }
            return ret;
        }
    
        private static int GetMaxNumberOfTasksByType(string taskTypeName)
        {
            // Logic to read from a configuration repository the value by task type name
            return int.Parse(Properties.Resources.MaxNumberOfTasksByType);
        }

        private static async void Submitter(int interval)
        {
            while (true)
            {
                await Task.Delay(interval);
                SendTaskRequestToTaskExecutionNode(GetTasksFromRequestQ());
            }
        }

        internal static void EnqueueRequestsInRequestQ(List<STask> stasks)
        {
            var callback = OperationContext.Current.GetCallbackChannel<ITaskUpdateCallback>();
            foreach (var stask in stasks)
                _requestQ.Enqueue(new STaskInfo(callback) { ClientRequest = stask });
        }

        internal static void SendTaskRequestToTaskExecutionNode(List<STaskInfo> staskInfos)
        {
            if (staskInfos.Count() == 0)
                return;

            var channel = new DuplexChannelFactory<ITaskExecutionNode>(
                          new InstanceContext(new CallbackHandler()),
                          new WSDualHttpBinding(), new EndpointAddress("http://localhost:5283/TaskExecutionNodeService.svc"))
                          .CreateChannel();
            try
            {
                var requestId = Guid.NewGuid().ToString();
                var reqs = staskInfos.Select(s => AddRequestToTracker(requestId, s, channel)).Where(s => s != null);
                ((IClientChannel)channel).Open();
                channel.Start(reqs.ToList<STask>());                
            }
            catch (CommunicationException ex)
            {
                foreach (var stask in staskInfos)
                    HandleClientUpdate(stask.ClientRequest.Id, STaskStatus.Faulted, ex);
            }
        }

        private static STask AddRequestToTracker(string requestId, STaskInfo info, ITaskExecutionNode channel)
        {
            info.ExecutionRequestId = requestId;
            info.ExecutionRequestChannel = channel;
            if (_submissionTracker.TryAdd(info.ClientRequest.Id, info))
                return info.ClientRequest;
            HandleClientUpdate(info.ClientRequest.Id, STaskStatus.Faulted,
              new Exception("Failed to add"));
            return null;
        }

        internal async static void HandleClientUpdate(string staskId, STaskStatus status, object result)
        {
            STaskInfo info;
            if (!_submissionTracker.TryGetValue(staskId, out info))
                throw new Exception("Could not get task from the tracker");
            try
            {
                await Task.Run(() =>
                  info.CallbackChannel.UpdateStatus(info.ClientRequest.Id, status, result));
                RemoveComplete(info.ClientRequest.Id);
            }
            catch (AggregateException ex)
            {
                throw ex;
            }
        }
        private static void RemoveComplete(string staskId)
        {
            STaskInfo info;
            if (!_submissionTracker.TryRemove(staskId, out info))
                throw new Exception("Failed to be removed from the tracking collection");

            if (_submissionTracker.Values.Where((t) => t.ExecutionRequestId == info.ExecutionRequestId).Count() == 0)
                CloseTaskRequestChannel((IClientChannel)info.ExecutionRequestChannel);
        }
        private static void CloseTaskRequestChannel(IClientChannel channel)
        {
            if (channel != null && channel.State != CommunicationState.Faulted)
                channel.Close();
        }
    }
    
    public enum STaskStatus
    {
        Completed,
        Canceled,
        Faulted
    }

    internal class ClientRequestInfo<TResult>
    {
        internal STask TaskExecutionRequest { get; set; }

        internal TaskCompletionSource<TResult> CompletionSource { get; set; }

        internal ClientRequestInfo(string typeName, string[] args)
        {
            TaskExecutionRequest = new STask()
            {
                Id = Guid.NewGuid().ToString(),
                STaskTypeName = typeName,
                STaskParameters = args
            };
            CompletionSource = new TaskCompletionSource<TResult>();
        }
    }
}
