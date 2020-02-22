namespace ProtobufTcpHelpers.Sample.Models
{
    using System;
    using System.Runtime.Serialization;
    using Newtonsoft.Json;
    using ProtoBuf;

    [Serializable]
    [DataContract]
    [ProtoContract]
    public class PocTypeB
    {
        [DataMember]
        [JsonProperty]
        [ProtoMember(1)]
        public int Id { get; set; }
        [DataMember]
        [JsonProperty]
        [ProtoMember(2)]
        public string Message { get; set; }
        [DataMember]
        [JsonProperty]
        [ProtoMember(3)]
        public DateTime? ExpireDate { get; set; }
    }
}
