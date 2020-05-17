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

        public async Task<PocTypeA> GetTypeAAsync(int id) => await MakeRequestAsync<int, PocTypeA>(id);

        public async Task<int> SendTypeAAsync(PocTypeA model) => await MakeRequestAsync<PocTypeA, int>(model);

        public async Task<int> SendTypeBAsync(PocTypeB model) => await MakeRequestAsync<PocTypeB, int>(model);
    }
}
