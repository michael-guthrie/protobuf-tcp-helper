namespace ProtobufTcpHelpers
{
    using System;
    using System.IO;
    using ProtoBuf;

    [ProtoContract]
    internal sealed class OperationWrapper
    {
        [ProtoMember(1)]
        public string Operation { get; }

        [ProtoMember(2)]
        public byte[] Body { get; }

        public static OperationWrapper SessionEnded { get; } = new OperationWrapper("SESSION_END");

        private OperationWrapper() { }

        public OperationWrapper(string operation, object content = null)
        {
            Operation = operation;

            if (content != null)
            {
                using var ms = new MemoryStream();
                Serializer.Serialize(ms, content);
                Body = ms.ToArray();
            }
        }

        public T GetBodyAs<T>()
        {
            if (Body == null)
            {
                if (default(T) != null)
                {
                    throw new InvalidOperationException($"Body of null is invalid for expected type {typeof(T).Name}.");
                }

                return default;
            }

            using var stream = new MemoryStream(Body);
            return Serializer.Deserialize<T>(stream);
        }
    }
}
