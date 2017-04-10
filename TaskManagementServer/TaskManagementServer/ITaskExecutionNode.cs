using System;
using System.Collections.Generic;
using System.ServiceModel;
using System.Threading;

namespace TaskManagementServer
{

    [ServiceContract(CallbackContract = typeof(ITaskUpdateCallback))]
    public interface ITaskExecutionNode
    {
        [OperationContract]
        void Start(List<STask> stask);
        [OperationContract]
        void Cancel(string Id);
    }
}