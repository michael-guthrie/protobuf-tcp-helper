namespace ProtobufTcpHelpers
{
    using System;
    using System.IO;
    using ProtoBuf;

    [ProtoContract]
    internal sealed class OperationWrapper
    {
        [ProtoMember(1)]
        public string Operation { get; private set; }

        [ProtoMember(2)]
        public byte[] Result { get; private set; }

        [ProtoMember(3)]
        public byte[][] Arguments { get; private set; }

        public static OperationWrapper SessionEnded { get; } = new OperationWrapper { Operation = "SESSION_END" };

        private OperationWrapper() { }

        public static OperationWrapper FromResult(string operation, object content)
        {
            var wrapper = new OperationWrapper
            {
                Operation = operation
            };

            if (content != null)
            {
                using var ms = new MemoryStream();
                Serializer.Serialize(ms, content);
                wrapper.Result = ms.ToArray();
            }

            return wrapper;
        }

        public static OperationWrapper ForRequest(string operation, object[] arguments = null)
        {
            var wrapper = new OperationWrapper
            {
                Operation = operation
            };

            if (arguments?.Length > 0)
            {
                wrapper.Arguments = new byte[arguments.Length][];
                for (int i = 0; i < arguments.Length; i++)
                {
                    using var ms = new MemoryStream();
                    Serializer.Serialize(ms, arguments[i]);
                    wrapper.Arguments[i] = ms.ToArray();
                }
            }

            return wrapper;
        }

        public T GetResultAs<T>()
        {
            if (Result == null)
            {
                if (default(T) != null)
                {
                    throw new InvalidOperationException($"Body of null is invalid for expected type {typeof(T).Name}.");
                }

                return default;
            }

            using var stream = new MemoryStream(Result);
            return Serializer.Deserialize<T>(stream);
        }
    }
}
