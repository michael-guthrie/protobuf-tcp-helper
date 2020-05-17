namespace ProtobufTcpHelpers.Sample
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using ProtobufTcpHelpers.Sample.Models;

    public interface IWorker
    {
        Task<ICollection<PocTypeB>> GetTypeBsAsync();
        Task<PocTypeA> GetTypeAAsync(int id);
        Task<int> SendTypeAAsync(PocTypeA model);
        Task<int> SendTypeBAsync(PocTypeB model);
    }
}
