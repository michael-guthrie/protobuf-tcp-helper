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

        private static int _currentOpenConnections;
        private static long _maxOpenConnections;

        static async Task Main(string[] args)
        {
            var stopwatch = new Stopwatch();
            var cancellationTokenSource = new CancellationTokenSource();

            var _ = StartListener(cancellationTokenSource.Token);
            Console.WriteLine("Started server...");

            // Perform a simple wake-up call so that timing isn't thrown off by initializing resources.
            await WakeupCall();

            //GC.Collect();
            //Console.WriteLine("Testing new client per request...");
            //stopwatch.Restart();
            //SendTypeBNewClient();
            //stopwatch.Stop();
            //Console.WriteLine(stopwatch.Elapsed);

            //GC.Collect();
            //Console.WriteLine("Testing shared client...");
            //stopwatch.Restart();
            //SendTypeBSharedClient();
            //stopwatch.Stop();
            //Console.WriteLine(stopwatch.Elapsed);

            //GC.Collect();
            //Console.WriteLine("Testing parallel client...");
            //stopwatch.Restart();
            //SendTypeBParallelClient();
            //stopwatch.Stop();
            //Console.WriteLine(stopwatch.Elapsed);

            //GC.Collect();
            //Console.WriteLine("Testing new socket per request...");
            //stopwatch.Restart();
            //SendTypeBNewSocket();
            //stopwatch.Stop();
            //Console.WriteLine(stopwatch.Elapsed);

            //GC.Collect();
            //Console.WriteLine("Testing shared socket...");
            //stopwatch.Restart();
            //SendTypeBSharedSocket();
            //stopwatch.Stop();
            //Console.WriteLine(stopwatch.Elapsed);

            //GC.Collect();
            //Console.WriteLine("Testing parallel socket...");
            //stopwatch.Restart();
            //SendTypeBParallelSocket();
            //stopwatch.Stop();
            //Console.WriteLine(stopwatch.Elapsed);

            Console.WriteLine("Testing loading open connections...");
            stopwatch.Restart();
            ConnectionLoadTest();
            while (_currentOpenConnections > 0)
            {
                await Task.Delay(50);
            }
            stopwatch.Stop();
            Console.WriteLine(stopwatch.Elapsed);

            _isOpen = false;
            cancellationTokenSource.Cancel();

            Console.WriteLine($"Total client connections: {_clientCount}");
            Console.WriteLine($"Max open connections: {_maxOpenConnections}");
            Console.WriteLine($"Current open connections: {_currentOpenConnections}");
            Console.WriteLine($"Total client requests: {_clientRequests}");
            Console.WriteLine($"Total server responses: {_serverResponses}");
            Console.WriteLine($"Total client exceptions: {_clientExceptions}");
            Console.WriteLine($"Total server exceptions: {_serverExceptions}");
        }

        private static readonly TaskFactory factory = new
            TaskFactory(CancellationToken.None,
                        TaskCreationOptions.None,
                        TaskContinuationOptions.None,
                        TaskScheduler.Default);
        private static T RunSync<T>(Func<Task<T>> op) => factory.StartNew(op).Unwrap().GetAwaiter().GetResult();

        private static void ClientTestOp(NetworkStream stream)
        {
            RunSync(async () => await stream.RequestAsync<IWorker, PocTypeA, int>(worker => worker.SendTypeAAsync, TestTypeA));
            //RunSync(async () => await stream.RequestAsync<IWorker, PocTypeB, int>(worker => worker.SendTypeBAsync, TestTypeB));
            Interlocked.Increment(ref _clientRequests);
        }

        private static void SocketTestOp(Socket socket)
        {
            RunSync(async () => await socket.RequestAsync<IWorker, PocTypeA, int>(worker => worker.SendTypeAAsync, TestTypeA));
            //RunSync(async () => await socket.RequestAsync<IWorker, PocTypeB, int>(worker => worker.SendTypeBAsync, TestTypeB));
            Interlocked.Increment(ref _clientRequests);
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

                        var _ = Worker.HandleClientAsync(client,
                                                         onSendingResponse: (op, result) => Interlocked.Increment(ref _serverResponses),
                                                         onError: ex => Interlocked.Increment(ref _serverExceptions))
                                      .ContinueWith(t => Interlocked.Decrement(ref _currentOpenConnections))
                                      .ConfigureAwait(false);
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
                        ++_clientCount;
                        _maxOpenConnections = Math.Max(_maxOpenConnections, Interlocked.Increment(ref _currentOpenConnections));

                        if (cancellationToken.IsCancellationRequested)
                        {
                            return;
                        }

                        var _ = Worker.HandleClientAsync(client,
                                                         onSendingResponse: (op, result) => Interlocked.Increment(ref _serverResponses),
                                                         onError: ex => Interlocked.Increment(ref _serverExceptions))
                                      .ContinueWith(t => Interlocked.Decrement(ref _currentOpenConnections))
                                      .ConfigureAwait(false);
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
            ++_clientRequests;
        }

        public static void SendTypeBParallelClient()
        {
            Parallel.For(0, TestSize,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Min(TestSize, 2_000)
                },
                i =>
                {
                    try
                    {
                        using var client = new TcpClient();
                        client.Connect(ConnectionConstants.Server);
                        using var stream = client.GetStream();
                        ClientTestOp(stream);
                    }
                    catch
                    {
                        Interlocked.Increment(ref _clientExceptions);
                    }
                }
            );
        }

        public static void SendTypeBNewClient()
        {
            try
            {
                for (int i = 0; i < TestSize; i++)
                {
                    using var client = new TcpClient();
                    client.Connect(ConnectionConstants.Server);
                    using var stream = client.GetStream();
                    ClientTestOp(stream);
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
                    ClientTestOp(stream);
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
                    SocketTestOp(client);
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
                    SocketTestOp(client);
                }
            }
            catch
            {
                ++_clientExceptions;
            }
        }

        public static void SendTypeBParallelSocket()
        {
            Parallel.For(0, TestSize,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Min(TestSize, 2_000)
                },
                i =>
                {
                    try
                    {
                        using var client = new Socket(SocketType.Stream, ProtocolType.Tcp);
                        client.Connect(ConnectionConstants.Server);
                        SocketTestOp(client);
                    }
                    catch
                    {
                        Interlocked.Increment(ref _clientExceptions);
                    }
                }
            );
        }

        public static void ConnectionLoadTest()
        {
            try
            {
                for (int i = 0; i < TestSize * 10; i++)
                {
                    var client = new Socket(SocketType.Stream, ProtocolType.Tcp);
                    client.Connect(ConnectionConstants.Server);
                    Task.Run(async () =>
                    {
                        await client.RequestAsync<IWorker, PocTypeA, int>(worker => worker.SendTypeAAsync, TestTypeA);
                        client.Close();
                    });
                }
            }
            catch
            {
                ++_clientExceptions;
            }
        }
    }
}
