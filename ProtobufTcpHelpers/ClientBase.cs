namespace ProtobufTcpHelpers
{
    using System;
    using System.Linq.Expressions;
    using System.Net;
    using System.Net.Sockets;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;

    public abstract class ClientBase : IDisposable
    {
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1);
        private readonly IPEndPoint _server;
        private Socket _client;

        protected ClientBase(IPEndPoint server)
        {
            _server = server;
        }

        private Socket GetSocket()
        {
            if (_client?.Connected != true)
            {
                _client?.Close();
                _client = new Socket(SocketType.Stream, ProtocolType.Tcp);
                _client.Connect(_server);
            }

            return _client;
        }

        protected TResult MakeRequest<TResult>([CallerMemberName] string invokingMethod = "") =>
            MakeRequest<object, TResult>(null, invokingMethod);

        protected TResult MakeRequest<TArgument, TResult>(TArgument requestParameter,
                                                          [CallerMemberName] string invokingMethod = "")
        {
            var request = new OperationWrapper(invokingMethod, requestParameter);
            _lock.Wait();

            try
            {
                Socket socket = GetSocket();

                // Send the client request.
                socket.SendWrapperRequest(request);

                // Now read back the server response.
                return socket.GetWrapperResponse().GetBodyAs<TResult>();
            }
            finally
            {
                _lock.Release();
            }
        }

        protected async Task<TResult> MakeRequestAsync<TResult>([CallerMemberName] string invokingMethod = "") =>
            await Task.Run(() => MakeRequest<object, TResult>(null, invokingMethod));

        protected async Task<TResult> MakeRequestAsync<TArgument, TResult>(
            TArgument requestParameter, [CallerMemberName] string invokingMethod = "") =>
            await Task.Run(() => MakeRequest<TArgument, TResult>(requestParameter, invokingMethod));

        public void Dispose()
        {
            _client?.Dispose();
            _lock?.Dispose();
        }
    }

    public class ClientBase<TServiceContract> : IDisposable
    {
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1);
        private readonly IPEndPoint _server;
        private Socket _client;

        public ClientBase(IPEndPoint server)
        {
            _server = server;
        }

        private Socket GetSocket()
        {
            if (_client?.Connected != true)
            {
                _client?.Close();
                _client = new Socket(SocketType.Stream, ProtocolType.Tcp);
                _client.Connect(_server);
            }

            return _client;
        }

        public TResult MakeRequest<TResult>(Expression<Func<TServiceContract, Func<TResult>>> serviceMethod)
        {
            _lock.Wait();
            try
            {
                Socket socket = GetSocket();
                return socket.Request(serviceMethod);
            }
            finally
            {
                _lock.Release();
            }
        }

        public TResult MakeRequest<TArgument, TResult>(
            Expression<Func<TServiceContract, Func<TArgument, TResult>>> serviceMethod, TArgument requestParameter)
        {
            _lock.Wait();
            try
            {
                Socket socket = GetSocket();
                return socket.Request(serviceMethod, requestParameter);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<TResult> MakeRequestAsync<TResult>(
            Expression<Func<TServiceContract, Func<TResult>>> serviceMethod) =>
            await Task.Run(() => MakeRequest(serviceMethod));

        public async Task<TResult> MakeRequestAsync<TArgument, TResult>(
            Expression<Func<TServiceContract, Func<TArgument, TResult>>> serviceMethod, TArgument requestParameter) =>
            await Task.Run(() => MakeRequest(serviceMethod, requestParameter));

        public void Dispose()
        {
            _client?.Dispose();
            _lock?.Dispose();
        }
    }
}
