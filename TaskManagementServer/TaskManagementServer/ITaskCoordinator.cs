using System.Collections.Generic;
using System.Runtime.Serialization;
using System.ServiceModel;

namespace TaskManagementServer
{
    [ServiceContract(CallbackContract = typeof(ITaskUpdateCallback))]
    public interface ITaskCoordinator
    {
        [OperationContract(IsOneWay = true)]
        void SubmitRequest(List<STask> stask);
        [OperationContract]
        bool CancelTask(string Id);
    }
    public interface ITaskUpdateCallback
    {
        [OperationContract(IsOneWay = true)]
        void UpdateStatus(string id, STaskStatus status, object result);
    }
    
    [DataContract]
    public class STask
    {
        [DataMember]
        public string Id { get; set; }
        [DataMember]
        public string STaskTypeName { get; set; }
        [DataMember]
        public string[] STaskParameters { get; set; }
    }
    public class STaskInfo
    {
        public string ExecutionRequestId { get; set; }
        public STask ClientRequest { get; set; }
        public ITaskUpdateCallback CallbackChannel { get; private set; }
        public ITaskExecutionNode ExecutionRequestChannel { get; set; }
        public STaskInfo(ITaskUpdateCallback callback)
        {
            CallbackChannel = callback;
        }
    }
}
