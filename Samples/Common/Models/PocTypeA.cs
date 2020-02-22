namespace ProtobufTcpHelpers.Sample.Models
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.Serialization;
    using Newtonsoft.Json;
    using ProtoBuf;

    [Serializable]
    [DataContract]
    [ProtoContract]
    public class PocTypeA
    {
        [DataMember]
        [JsonProperty]
        [ProtoMember(1)]
        public int Id { get; set; }
        [DataMember]
        [JsonProperty]
        [ProtoMember(2)]
        public string Name { get; set; }
        [DataMember]
        [JsonProperty]
        [ProtoMember(3)]
        public int? Quazars { get; set; }
        [DataMember]
        [JsonProperty]
        [ProtoMember(4)]
        public DateTime PostDate { get; set; }
        [DataMember]
        [JsonProperty]
        [ProtoMember(5)]
        public ICollection<PocTypeAChild> Children { get; set; }
    }
}
