namespace ProtobufTcpHelpers
{
    using System;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Net;
    using System.Net.Sockets;
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

        protected async Task<TResult> MakeRequestAsync<TResult>(params object[] requestParameters)
        {
            var targetStack = new System.Diagnostics.StackTrace(1).GetFrames().FirstOrDefault(f =>
            {
                var t = f.GetMethod().DeclaringType;
                return t != typeof(ClientBase) && t.IsInstanceOfType(this);
            });
            string invokingMethod = targetStack.GetMethod().Name;
            var request = OperationWrapper.ForRequest(invokingMethod, requestParameters);
            _lock.Wait();

            try
            {
                Socket socket = GetSocket();

                // Send the client request.
                await socket.SendWrapperRequestAsync(request);

                // Now read back the server response.
                return (await socket.GetWrapperResponseAsync()).GetResultAs<TResult>();
            }
            finally
            {
                _lock.Release();
            }
        }

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

        public TServiceContract Service { get; }

        public ClientBase(IPEndPoint server)
        {
            _server = server;

            // TODO: Somehow magically mock up a dynamic implementation of Service to pipe all method calls through the MakeRequest logic.
            //Service = null;
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

        public async Task<TResult> MakeRequestAsync<TResult>(Expression<Func<TServiceContract, Func<Task<TResult>>>> serviceMethod)
        {
            _lock.Wait();
            try
            {
                Socket socket = GetSocket();
                return await socket.RequestAsync(serviceMethod);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<TResult> MakeRequestAsync<TArgument, TResult>(
            Expression<Func<TServiceContract, Func<TArgument, Task<TResult>>>> serviceMethod, TArgument requestParameter)
        {
            _lock.Wait();
            try
            {
                Socket socket = GetSocket();
                return await socket.RequestAsync(serviceMethod, requestParameter);
            }
            finally
            {
                _lock.Release();
            }
        }

        public void Dispose()
        {
            _client?.Dispose();
            _lock?.Dispose();
        }
    }
}
