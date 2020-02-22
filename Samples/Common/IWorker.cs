namespace ProtobufTcpHelpers.Sample
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using ProtobufTcpHelpers.Sample.Models;

    public interface IWorker
    {
        Task<ICollection<PocTypeB>> GetTypeBsAsync();
        PocTypeA GetTypeA(int id);
        int SendTypeA(PocTypeA model);
        int SendTypeB(PocTypeB model);
    }
}
