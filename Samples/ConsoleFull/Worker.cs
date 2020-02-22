﻿namespace ProtobufTcpHelpers.Sample.ConsoleInstanceTest
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using ProtobufTcpHelpers.Sample.Models;

    public class Worker : IWorker
    {
        public async Task<ICollection<PocTypeB>> GetTypeBsAsync() =>
            await Task.FromResult(
                Enumerable.Range(new Random().Next(1, 1000), new Random().Next(3, 30))
                          .Select(i =>
                                      new PocTypeB
                                      {
                                          Id = i,
                                          Message = Guid.NewGuid().ToString(),
                                          ExpireDate = i % 3 == 0 ? (DateTime?)null : DateTime.Now.AddDays(i)
                                      })
                          .ToList());

        public PocTypeA GetTypeA(int id) =>
            new PocTypeA
            {
                Id = id,
                Children = Enumerable.Range(id * 100, id % 100)
                                     .Select(i =>
                                                 new PocTypeAChild
                                                 {
                                                     Id = i,
                                                     Code = Guid.NewGuid().ToString()
                                                 })
                                     .ToList(),
                Name = Guid.NewGuid().ToString(),
                PostDate = DateTime.Today,
                Quazars = id % 5 == 0 ? id : (int?) null
            };

        public int SendTypeA(PocTypeA model) => model.Id;

        public int SendTypeB(PocTypeB model) => model.Id;
    }
}
