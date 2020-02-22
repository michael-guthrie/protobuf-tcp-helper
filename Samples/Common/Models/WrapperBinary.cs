namespace ProtobufTcpHelpers.Sample.Models
{
    using System;
    using System.IO;
    using System.Runtime.Serialization;
    using System.Runtime.Serialization.Formatters.Binary;
    using ProtoBuf;

    [Serializable]
    [DataContract]
    [ProtoContract]
    public class WrapperBinary
    {
        [DataMember]
        [ProtoMember(1)]
        public string Operation { get; protected set; }

        [DataMember]
        [ProtoMember(2)]
        public byte[] Body { get; protected set; }

        public static WrapperBinary SessionEnded { get; } = new WrapperBinary("SESSION_END");

        protected internal WrapperBinary()
        {

        }

        public WrapperBinary(string operation, object content = null)
        {
            Operation = operation;

            if (content != null)
            {
                using var ms = new MemoryStream();
                Serializer.Serialize(ms, content);
                Body = ms.ToArray();
            }
        }

        internal object[] ToMethodArguments()
        {
            if (!(Body?.Length > 0))
            {
                return new object[0];
            }

            using var stream = new MemoryStream(Body);
            return Serializer.Deserialize<object[]>(stream);
        }
    }
}
