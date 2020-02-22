namespace ProtobufTcpHelpers.Sample.ConsoleInstanceTest
{
    using ProtobufTcpHelpers;
    using System;
    using System.Diagnostics;
    using System.Linq;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using ProtobufTcpHelpers.Sample.Models;

    class Program
    {
        private const int TestSize = 1_000;
        private static readonly IWorker Worker = new Worker();

        private static readonly PocTypeA TestTypeA = new PocTypeA
        {
            Id = 101,
            Name = Guid.NewGuid().ToString(),
            PostDate = DateTime.Now,
            Children = Enumerable.Repeat(new PocTypeAChild { Code = "Test child", Id = 101 }, 2000).ToList()
        };
        private static readonly PocTypeB TestTypeB = new PocTypeB
        {
            Id = 101,
            Message = string.Concat(Enumerable.Repeat("BACON", 1000)),
            ExpireDate = DateTime.Now
        };
        private static bool _isOpen = true;

        private static int _clientCount;
        private static int _clientRequests;
        private static int _serverResponses;

        private static int _clientExceptions;
        private static int _serverExceptions;

        static async Task Main(string[] args)
        {
            var stopwatch = new Stopwatch();
            var cancellationTokenSource = new CancellationTokenSource();

            var _ = StartListener(cancellationTokenSource.Token);
            Console.WriteLine("Started server...");

            // Perform a simple wake-up call so that timing isn't thrown off by initializing resources.
            await WakeupCall();

            Console.WriteLine("Testing new client per request...");
            stopwatch.Restart();
            SendTypeBNewClient();
            stopwatch.Stop();
            Console.WriteLine(stopwatch.Elapsed);

            Console.WriteLine("Testing shared client...");
            stopwatch.Restart();
            SendTypeBSharedClient();
            stopwatch.Stop();
            Console.WriteLine(stopwatch.Elapsed);

            //Console.WriteLine("Testing parallel client...");
            //stopwatch.Restart();
            //SendTypeBParallel();
            //stopwatch.Stop();
            //Console.WriteLine(stopwatch.Elapsed);

            Console.WriteLine("Testing new socket per request...");
            stopwatch.Restart();
            SendTypeBNewSocket();
            stopwatch.Stop();
            Console.WriteLine(stopwatch.Elapsed);

            Console.WriteLine("Testing shared socket...");
            stopwatch.Restart();
            SendTypeBSharedSocket();
            stopwatch.Stop();
            Console.WriteLine(stopwatch.Elapsed);

            _isOpen = false;
            cancellationTokenSource.Cancel();

            Console.WriteLine($"Total client connections: {_clientCount}");
            Console.WriteLine($"Total client requests: {_clientRequests}");
            Console.WriteLine($"Total server responses: {_serverResponses}");
            Console.WriteLine($"Total client exceptions: {_clientExceptions}");
            Console.WriteLine($"Total server exceptions: {_serverExceptions}");
        }

        private static void StartListener()
        {
            Task.Run(async () =>
            {
                var server = new TcpListener(ConnectionConstants.Server);
                try
                {
                    server.Start();
                    while (_isOpen)
                    {
                        Socket client = await server.AcceptSocketAsync().ConfigureAwait(false);
                        //TcpClient client = await server.AcceptTcpClientAsync();
                        ++_clientCount;
                        var _ = Worker.HandleClientAsync(client, onSendingResponse: (op, result) => ++_serverResponses,
                                                    onError: ex => ++_serverExceptions).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.ToString());
                }
                finally
                {
                    server.Stop();
                }
            });
        }

        public static async Task StartListener(CancellationToken cancellationToken)
        {
            await Task.Run(async () =>
            {
                var server = new TcpListener(ConnectionConstants.Server);
                try
                {
                    server.Start();
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        Socket client = await server.AcceptSocketAsync().ConfigureAwait(false);
                        //TcpClient client = await server.AcceptTcpClientAsync();

                        if (cancellationToken.IsCancellationRequested)
                        {
                            return;
                        }

                        var _ = Worker.HandleClientAsync(client, onSendingResponse: (op, result) => ++_serverResponses,
                                                    onError: ex => ++_serverExceptions).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.ToString());
                }
                finally
                {
                    server.Stop();
                }
            });
        }

        public static async Task WakeupCall()
        {
            using var client = new WorkerClient();
            await client.GetTypeBsAsync();
        }

        public static void SendTypeBParallel()
        {
            using var client = new WorkerClient();
            Parallel.For(0, TestSize,
                         i =>
                         {
                             try
                             {
                                 client.SendTypeB(TestTypeB);
                                 ++_clientRequests;
                             }
                             catch
                             {
                                 ++_clientExceptions;
                             }
                         }
            );
        }

        public static void SendTypeBNewClient()
        {
            try
            {
                using var client = new WorkerClient();
                for (int i = 0; i < TestSize; i++)
                {
                    client.SendTypeA(TestTypeA);
                    ++_clientRequests;
                    //client.SendTypeB(TestTypeB);
                    //++_clientRequests;
                    //client.GetTypeBs();
                    //++_clientRequests;
                }
            }
            catch
            {
                ++_clientExceptions;
            }
        }

        public static void SendTypeBSharedClient()
        {
            try
            {
                using var client = new TcpClient();
                client.Connect(ConnectionConstants.Server);
                using var stream = client.GetStream();

                for (int i = 0; i < TestSize; i++)
                {
                    // Send the client request.
                    var _ = stream.RequestAsync<IWorker, PocTypeA, int>(worker => worker.SendTypeA, TestTypeA).Result;
                    ++_clientRequests;

                    //// Send the client request.
                    //stream.Request<PocTypeB, int>(worker => worker.SendTypeB, TestTypeB);
                    //++_clientRequests;

                    //// Send the client request.
                    //stream.Request<ICollection<PocTypeB>>(worker => worker.GetTypeBs);
                    //++_clientRequests;
                }
            }
            catch
            {
                ++_clientExceptions;
            }
        }

        public static void SendTypeBNewSocket()
        {
            try
            {
                for (int i = 0; i < TestSize; i++)
                {
                    using var client = new Socket(SocketType.Stream, ProtocolType.Tcp);
                    client.Connect(ConnectionConstants.Server);
                    client.Request<IWorker, PocTypeB, int>(worker => worker.SendTypeB, TestTypeB);
                    ++_clientRequests;
                }
            }
            catch
            {
                ++_clientExceptions;
            }
        }

        public static void SendTypeBSharedSocket()
        {
            try
            {
                using var client = new Socket(SocketType.Stream, ProtocolType.Tcp);
                client.Connect(ConnectionConstants.Server);
                for (int i = 0; i < TestSize; i++)
                {
                    client.Request<IWorker, PocTypeB, int>(worker => worker.SendTypeB, TestTypeB);
                    ++_clientRequests;
                }
            }
            catch
            {
                ++_clientExceptions;
            }
        }
    }
}
