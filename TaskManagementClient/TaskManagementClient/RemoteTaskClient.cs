using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using TaskManagementServer;

namespace TaskManagementClient
{
    public class RemoteTaskClient<TResult> : IDisposable
    {
        ITaskCoordinator _client;
        List<ClientRequestInfo<TResult>> _buffer = new List<ClientRequestInfo<TResult>>();
        
        ConcurrentDictionary<string, ClientRequestInfo<TResult>> _requests = new ConcurrentDictionary<string, ClientRequestInfo<TResult>>();

        public RemoteTaskClient(string taskCoordinatorEndpointAddress)
        {
            var factory = new DuplexChannelFactory<ITaskCoordinator>
               (new InstanceContext(new CallbackHandler<TResult>(_requests)),
               new WSDualHttpBinding(),
               new EndpointAddress(taskCoordinatorEndpointAddress));
            _client = factory.CreateChannel();
            ((IClientChannel)_client).Open();
        }

        public void AddRequest(string typeName, string[] parameters, CancellationToken tk)
        {
            var info = new ClientRequestInfo<TResult>(typeName, parameters);
            _buffer.Add(info);
            tk.Register(() => _client.CancelTask(info.TaskExecutionRequest.Id));
        }

        public void AddRequest(string typeName, string[] parameters)
        {
            _buffer.Add(new ClientRequestInfo<TResult>(typeName, parameters));
        }

        public Task<TResult>[] SubmitRequests()
        {
            if (_buffer.Count == 0)
                return null;
            var req = _buffer.Select((r) =>
            {
                _requests.TryAdd(r.TaskExecutionRequest.Id, r);
                return r.TaskExecutionRequest;
            });
            _client.SubmitRequest(req.ToList<STask>().ToArray());
            var ret = _buffer.Select(r =>
             r.CompletionSource.Task).ToArray<Task<TResult>>();
            _buffer.Clear();
            return ret;
        }

        public void Dispose()
        {}
    }

    [ServiceBehavior(ConcurrencyMode = ConcurrencyMode.Reentrant)]
    public class CallbackHandler<TResult> : ITaskCoordinatorCallback
    {
        ConcurrentDictionary<string, ClientRequestInfo<TResult>> _requests;
        public void UpdateStatus(string id, STaskStatus status, object result)
        {
            ClientRequestInfo<TResult> info;
            if (_requests.TryRemove(id, out info))
            {
                switch (status)
                {
                    case STaskStatus.Completed:
                        info.CompletionSource.SetResult((TResult)result);
                        break;
                    case STaskStatus.Canceled:
                        info.CompletionSource.SetCanceled();
                        break;
                    case STaskStatus.Faulted:
                        info.CompletionSource.SetException((Exception)result);
                        break;
                }
            }
        }
        internal CallbackHandler(ConcurrentDictionary<string, ClientRequestInfo<TResult>> requests)
        {
            _requests = requests;
        }
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
