namespace ProtobufTcpHelpers.Sample
{
    using ProtobufTcpHelpers;
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Threading.Tasks;
    using ProtobufTcpHelpers.Sample.Models;

    public class WorkerClient : ClientBase, IWorker, IDisposable
    {
        public WorkerClient() : this(ConnectionConstants.Server)
        {
        }

        public WorkerClient(IPEndPoint server) : base(server)
        {
        }

        public async Task<ICollection<PocTypeB>> GetTypeBsAsync() => await MakeRequestAsync<ICollection<PocTypeB>>();

        public PocTypeA GetTypeA(int id) => MakeRequest<int, PocTypeA>(id);

        public int SendTypeA(PocTypeA model) => MakeRequest<PocTypeA, int>(model);

        public int SendTypeB(PocTypeB model) => MakeRequest<PocTypeB, int>(model);
    }
}
