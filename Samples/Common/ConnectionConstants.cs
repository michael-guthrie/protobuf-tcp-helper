namespace ProtobufTcpHelpers.Sample
{
    using System.Net;

    public static class ConnectionConstants
    {
        public static IPEndPoint Server { get; } = new IPEndPoint(IPAddress.Loopback, 34567);
    }
}
