namespace ProtobufTcpHelpers
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Net.Sockets;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using ProtoBuf;

    public static class Extensions
    {
        private static async Task<object> InvokeRequestAsync<T>(this T worker, OperationWrapper request)
        {
            MethodInfo method = typeof(T).GetMethod(request.Operation);
            if (method == null)
            {
                throw new ArgumentException("Request operation was invalid.", nameof(request));
            }

            object[] arguments = null;
            ParameterInfo[] methodParameters = method.GetParameters();
            if (request.Arguments?.Length > 0)
            {
                if (request.Arguments.Length != methodParameters.Length)
                {
                    throw new ArgumentException("Given request parameters did not match parameters for the operation.");
                }
                arguments = new object[methodParameters.Length];
                for (int i = 0; i < methodParameters.Length; i++)
                {
                    if (request.Arguments[i] == null)
                    {
                        arguments[i] = null;
                        continue;
                    }
                    await using var stream = new MemoryStream(request.Arguments[i]);
                    arguments[i] = Serializer.Deserialize(methodParameters[i].ParameterType, stream);
                }
            }

            bool isAwaitable = method.ReturnType.GetMethod(nameof(Task.GetAwaiter)) != null;
            if (!isAwaitable)
            {
                return method.Invoke(worker, arguments);
            }

            var task = (Task) method.Invoke(worker, arguments);
            await task.ConfigureAwait(false);
            PropertyInfo resultProperty = task.GetType().GetProperty("Result") ??
                                          throw new InvalidOperationException(
                                              $"Method {request.Operation} must return a result.");
            return resultProperty.GetValue(task);
        }

        private static async Task<OperationWrapper> GetWrapperResponseAsync(this NetworkStream stream, CancellationToken cancellationToken)
        {
            // Read the size header.
            var sizeHeader = new byte[10];
            await stream.ReadAsync(sizeHeader, 0, sizeHeader.Length, cancellationToken).ConfigureAwait(false);

            // Validate the size header.
            if (sizeHeader[0] != byte.MaxValue ||
                sizeHeader[1] != byte.MinValue ||
                sizeHeader[2] != byte.MaxValue ||
                sizeHeader[7] != byte.MinValue ||
                sizeHeader[8] != byte.MaxValue ||
                sizeHeader[9] != byte.MinValue)
            {
                if (sizeHeader.All(b => b == byte.MinValue) && !stream.DataAvailable)
                {
                    return OperationWrapper.SessionEnded;
                }

                throw new InvalidOperationException(
                    "Unexpected message received. Message did not represent a valid size header.");
            }

            // Now parse the size.
            int messageSize = 0;
            for (int i = 0; i < sizeof(int); i++)
            {
                messageSize = (sizeHeader[i + 3] << (i * 8)) | messageSize;
            }

            using var ms = new MemoryStream();
            var buffer = new byte[1024];
            int readLength = 0;
            while (readLength < messageSize)
            {
                int blockSize = Math.Min(messageSize - readLength, buffer.Length);
                readLength += await stream.ReadAsync(buffer, 0, blockSize, cancellationToken);
                await ms.WriteAsync(buffer, 0, blockSize, cancellationToken).ConfigureAwait(false);
            }

            ms.Position = 0;
            return Serializer.Deserialize<OperationWrapper>(ms);
        }

        private static async Task SendWrapperRequestAsync(this NetworkStream stream, OperationWrapper request, CancellationToken cancellationToken)
        {
            // Serialize in memory so we can get the size before sending to the network buffer.
            await using var ms = new MemoryStream();
            Serializer.Serialize(ms, request);

            // Send the size header.
            byte[] sizeHeader = new byte[10];
            sizeHeader[0] = sizeHeader[2] = sizeHeader[8] = byte.MaxValue;
            for (int i = 0; i < sizeof(int); i++)
            {
                sizeHeader[i + 3] = (byte) ((ms.Length >> (i * 8)) & byte.MaxValue);
            }

            await stream.WriteAsync(sizeHeader, 0, sizeHeader.Length, cancellationToken).ConfigureAwait(false);

            // Send the client request.
            ms.Position = 0;
            await ms.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a client request across the network stream and retrieves the result.
        /// </summary>
        /// <typeparam name="TService">The contract for the service.</typeparam>
        /// <typeparam name="TResult">The type of the return value of the requested service operation.</typeparam>
        /// <param name="stream">The network stream from the TCP client.</param>
        /// <param name="serviceMethod">Projection of the requested service method.</param>
        /// <returns>The result of the service operation.</returns>
        public static async Task<TResult> RequestAsync<TService, TResult>(
            this NetworkStream stream,
            Expression<Func<TService, Func<TResult>>> serviceMethod,
            CancellationToken cancellationToken = default)
        {
            var targetMethod =
                (((serviceMethod.Body as UnaryExpression)
                  ?.Operand as MethodCallExpression)
                 ?.Object as ConstantExpression)
                ?.Value as MethodInfo;

            if (targetMethod == null)
            {
                throw new ArgumentException("Target IWorker method could not be found from worker method expression.",
                                            nameof(serviceMethod));
            }

            await SendWrapperRequestAsync(stream, OperationWrapper.ForRequest(targetMethod.Name), cancellationToken).ConfigureAwait(false);

            return (await GetWrapperResponseAsync(stream, cancellationToken).ConfigureAwait(false)).GetResultAs<TResult>();
        }

        /// <summary>
        /// Sends a client request across the network stream and retrieves the result.
        /// </summary>
        /// <typeparam name="TService">The contract for the service.</typeparam>
        /// <typeparam name="TResult">The type of the return value of the requested service operation.</typeparam>
        /// <param name="stream">The network stream from the TCP client.</param>
        /// <param name="asyncServiceMethod">Projection of the requested asynchronous service method.</param>
        /// <returns>The result of the service operation.</returns>
        public static async Task<TResult> RequestAsync<TService, TResult>(
            this NetworkStream stream,
            Expression<Func<TService, Func<Task<TResult>>>> asyncServiceMethod,
            CancellationToken cancellationToken = default)
        {
            var targetMethod =
                (((asyncServiceMethod.Body as UnaryExpression)
                  ?.Operand as MethodCallExpression)
                 ?.Object as ConstantExpression)
                ?.Value as MethodInfo;

            if (targetMethod == null)
            {
                throw new ArgumentException("Target IWorker method could not be found from worker method expression.",
                                            nameof(asyncServiceMethod));
            }

            await SendWrapperRequestAsync(stream, OperationWrapper.ForRequest(targetMethod.Name), cancellationToken).ConfigureAwait(false);

            return (await GetWrapperResponseAsync(stream, cancellationToken).ConfigureAwait(false)).GetResultAs<TResult>();
        }

        /// <summary>
        /// Sends a client request across the network stream and retrieves the result.
        /// </summary>
        /// <typeparam name="TService">The contract for the service.</typeparam>
        /// <typeparam name="TArgument">The type of the argument for the requested service operation.</typeparam>
        /// <typeparam name="TResult">The type of the return value of the requested service operation.</typeparam>
        /// <param name="stream">The network stream from the TCP client.</param>
        /// <param name="serviceMethod">Projection of the requested service method.</param>
        /// <param name="argument">The argument to send when making performing the service operation.</param>
        /// <returns>The result of the service operation.</returns>
        public static async Task<TResult> RequestAsync<TService, TArgument, TResult>(
            this NetworkStream stream,
            Expression<Func<TService, Func<TArgument, TResult>>> serviceMethod,
            TArgument argument,
            CancellationToken cancellationToken = default)
        {
            var targetMethod =
                (((serviceMethod.Body as UnaryExpression)
                  ?.Operand as MethodCallExpression)
                 ?.Object as ConstantExpression)
                ?.Value as MethodInfo;

            if (targetMethod == null)
            {
                throw new ArgumentException("Target IWorker method could not be found from worker method expression.",
                                            nameof(serviceMethod));
            }

            await SendWrapperRequestAsync(stream, OperationWrapper.ForRequest(targetMethod.Name, new object[] { argument }), cancellationToken).ConfigureAwait(false);

            return (await GetWrapperResponseAsync(stream, cancellationToken).ConfigureAwait(false)).GetResultAs<TResult>();
        }

        /// <summary>
        /// Sends a client request across the network stream and retrieves the result.
        /// </summary>
        /// <typeparam name="TService">The contract for the service.</typeparam>
        /// <typeparam name="TArgument">The type of the argument for the requested service operation.</typeparam>
        /// <typeparam name="TResult">The type of the return value of the requested service operation.</typeparam>
        /// <param name="stream">The network stream from the TCP client.</param>
        /// <param name="asyncServiceMethod">Projection of the requested asynchronous service method.</param>
        /// <param name="argument">The argument to send when making performing the service operation.</param>
        /// <returns>The result of the service operation.</returns>
        public static async Task<TResult> RequestAsync<TService, TArgument, TResult>(
            this NetworkStream stream,
            Expression<Func<TService, Func<TArgument, Task<TResult>>>> asyncServiceMethod,
            TArgument argument,
            CancellationToken cancellationToken = default)
        {
            var targetMethod =
                (((asyncServiceMethod.Body as UnaryExpression)
                  ?.Operand as MethodCallExpression)
                 ?.Object as ConstantExpression)
                ?.Value as MethodInfo;

            if (targetMethod == null)
            {
                throw new ArgumentException("Target IWorker method could not be found from worker method expression.",
                                            nameof(asyncServiceMethod));
            }

            await SendWrapperRequestAsync(stream, OperationWrapper.ForRequest(targetMethod.Name, new object[] { argument }), cancellationToken).ConfigureAwait(false);

            return (await GetWrapperResponseAsync(stream, cancellationToken).ConfigureAwait(false)).GetResultAs<TResult>();
        }

        /// <summary>
        /// Begins a communication task with a TCP client using the worker instance to perform requested operations.
        /// </summary>
        /// <typeparam name="TService">The contract for the service.</typeparam>
        /// <param name="worker">The TService instance which will fulfill requested operations.</param>
        /// <param name="client">The TCP client connection.</param>
        /// <param name="onRequestReceived">(Optional) Action to perform when a client request has been received.</param>
        /// <param name="onSendingResponse">(Optional) Action to perform once the worker has processed the operation and prior to sending the server response.</param>
        /// <param name="onError">(Optional) Action to perform when processing encounters an error.</param>
        /// <returns>Awaitable task hosting the background processing of the client socket connection.</returns>
        public static async Task HandleClientAsync<TService>(
            this TService worker, TcpClient client,
            CancellationToken cancellationToken = default,
            Action<string, byte[][]> onRequestReceived = null,
            Action<string, object> onSendingResponse = null,
            Action<Exception> onError = null)
        {
            await Task.Run(async () =>
            {
                try
                {
                    await using NetworkStream stream = client.GetStream();

                    // Continue reading as long as client requests are available.
                    OperationWrapper request;
                    while (!cancellationToken.IsCancellationRequested &&
                           client.Connected && stream.CanRead &&
                           (request = await stream.GetWrapperResponseAsync(cancellationToken).ConfigureAwait(false)) != OperationWrapper.SessionEnded)
                    {
                        onRequestReceived?.Invoke(request.Operation, request.Arguments);

                        object result = await worker.InvokeRequestAsync(request).ConfigureAwait(false);

                        // Now send back a response.
                        onSendingResponse?.Invoke(request.Operation, result);
                        await stream.SendWrapperRequestAsync(OperationWrapper.FromResult(request.Operation, result), cancellationToken);

                        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    if (onError == null)
                    {
                        throw;
                    }

                    onError(ex);
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Begins a communication task with a TPC client using the worker instance to perform requested operations.
        /// </summary>
        /// <typeparam name="TService">The contract for the service.</typeparam>
        /// <param name="worker">The TService instance which will fulfill requested operations.</param>
        /// <param name="client">The TCP client connection.</param>
        /// <param name="onRequestReceived">(Optional) Action to perform when a client request has been received.</param>
        /// <param name="onSendingResponse">(Optional) Action to perform once the worker has processed the operation and prior to sending the server response.</param>
        /// <param name="onError">(Optional) Action to perform when processing encounters an error.</param>
        /// <returns>Awaitable task hosting the background processing of the client socket connection.</returns>
        public static async Task HandleClientAsync<TService>(
            this TService worker, TcpClient client,
            CancellationToken cancellationToken = default,
            Func<string, byte[][], Task> onRequestReceived = null,
            Func<string, object, Task> onSendingResponse = null,
            Func<Exception, Task> onError = null)
        {
            await Task.Run(async () =>
            {
                try
                {
                    await using NetworkStream stream = client.GetStream();

                    // Continue reading as long as client requests are available.
                    OperationWrapper request;
                    while (!cancellationToken.IsCancellationRequested &&
                           client.Connected && stream.CanRead &&
                           (request = await stream.GetWrapperResponseAsync(cancellationToken).ConfigureAwait(false)) != OperationWrapper.SessionEnded)
                    {
                        if (onRequestReceived != null)
                        {
                            await onRequestReceived.Invoke(request.Operation, request.Arguments).ConfigureAwait(false);
                        }

                        object result = await worker.InvokeRequestAsync(request).ConfigureAwait(false);

                        // Now send back a response.
                        if (onSendingResponse != null)
                        {
                            await onSendingResponse.Invoke(request.Operation, result).ConfigureAwait(false);
                        }

                        await stream.SendWrapperRequestAsync(OperationWrapper.FromResult(request.Operation, result), cancellationToken).ConfigureAwait(false);

                        await stream.FlushAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    if (onError == null)
                    {
                        throw;
                    }

                    await onError.Invoke(ex).ConfigureAwait(false);
                }
            });
        }

        internal static async Task<OperationWrapper> GetWrapperResponseAsync(this Socket socket)
        {

            // Read the size header.
            var sizeHeader = new byte[10];
            if (await socket.ReceiveAsync(sizeHeader, SocketFlags.None).ConfigureAwait(false) == 0)
            {
                return OperationWrapper.SessionEnded;
            }

            // Validate the size header.
            if (sizeHeader[0] != byte.MaxValue ||
                sizeHeader[1] != byte.MinValue ||
                sizeHeader[2] != byte.MaxValue ||
                sizeHeader[7] != byte.MinValue ||
                sizeHeader[8] != byte.MaxValue ||
                sizeHeader[9] != byte.MinValue)
            {
                if (sizeHeader.All(b => b == byte.MinValue))
                {
                    return OperationWrapper.SessionEnded;
                }

                throw new InvalidOperationException(
                    "Unexpected message received. Message did not represent a valid size header.");
            }

            // Now parse the size.
            int messageSize = 0;
            for (int i = 0; i < sizeof(int); i++)
            {
                messageSize = (sizeHeader[i + 3] << (i * 8)) | messageSize;
            }

            var buffer = new byte[messageSize];
            await socket.ReceiveAsync(buffer, SocketFlags.None).ConfigureAwait(false);
            await using var ms = new MemoryStream(buffer) {Position = 0};
            return Serializer.Deserialize<OperationWrapper>(ms);
        }

        internal static async Task SendWrapperRequestAsync(this Socket socket, OperationWrapper request)
        {
            // Serialize in memory so we can get the size before sending to the network buffer.
            await using var ms = new MemoryStream();
            Serializer.Serialize(ms, request);

            // Send the size header.
            byte[] sizeHeader = new byte[10];
            sizeHeader[0] = sizeHeader[2] = sizeHeader[8] = byte.MaxValue;
            for (int i = 0; i < sizeof(int); i++)
            {
                sizeHeader[i + 3] = (byte) ((ms.Length >> (i * 8)) & byte.MaxValue);
            }

            await socket.SendAsync(sizeHeader, SocketFlags.None).ConfigureAwait(false);

            // Send the client request.
            ms.Position = 0;
            await socket.SendAsync(ms.ToArray(), SocketFlags.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a client request across the network socket and retrieves the result.
        /// </summary>
        /// <typeparam name="TService">The contract for the service.</typeparam>
        /// <typeparam name="TResult">The type of the return value of the requested service operation.</typeparam>
        /// <param name="socket">The TCP client socket.</param>
        /// <param name="serviceMethod">Projection of the requested service method.</param>
        /// <returns>The result of the service operation.</returns>
        public static async Task<TResult> RequestAsync<TService, TResult>(
            this Socket socket,
            Expression<Func<TService, Func<TResult>>> serviceMethod)
        {
            var targetMethod =
                (((serviceMethod.Body as UnaryExpression)
                  ?.Operand as MethodCallExpression)
                 ?.Object as ConstantExpression)
                ?.Value as MethodInfo;

            if (targetMethod == null)
            {
                throw new ArgumentException("Target IWorker method could not be found from worker method expression.",
                                            nameof(serviceMethod));
            }

            await SendWrapperRequestAsync(socket, OperationWrapper.ForRequest(targetMethod.Name)).ConfigureAwait(false);

            return (await GetWrapperResponseAsync(socket).ConfigureAwait(false)).GetResultAs<TResult>();
        }

        /// <summary>
        /// Sends a client request across the network socket and retrieves the result.
        /// </summary>
        /// <typeparam name="TService">The contract for the service.</typeparam>
        /// <typeparam name="TResult">The type of the return value of the requested service operation.</typeparam>
        /// <param name="socket">The TCP client socket.</param>
        /// <param name="asyncServiceMethod">Projection of the requested asynchronous service method.</param>
        /// <returns>The result of the service operation.</returns>
        public static async Task<TResult> RequestAsync<TService, TResult>(
            this Socket socket,
            Expression<Func<TService, Func<Task<TResult>>>> asyncServiceMethod)
        {
            var targetMethod =
                (((asyncServiceMethod.Body as UnaryExpression)
                  ?.Operand as MethodCallExpression)
                 ?.Object as ConstantExpression)
                ?.Value as MethodInfo;

            if (targetMethod == null)
            {
                throw new ArgumentException("Target IWorker method could not be found from worker method expression.",
                                            nameof(asyncServiceMethod));
            }

            await SendWrapperRequestAsync(socket, OperationWrapper.ForRequest(targetMethod.Name)).ConfigureAwait(false);

            return (await GetWrapperResponseAsync(socket).ConfigureAwait(false)).GetResultAs<TResult>();
        }

        /// <summary>
        /// Sends a client request across the network socket and retrieves the result.
        /// </summary>
        /// <typeparam name="TService">The contract for the service.</typeparam>
        /// <typeparam name="TArgument">The type of the argument for the requested service operation.</typeparam>
        /// <typeparam name="TResult">The type of the return value of the requested service operation.</typeparam>
        /// <param name="socket">The TCP client socket.</param>
        /// <param name="serviceMethod">Projection of the requested service method.</param>
        /// <param name="argument">The argument to send when making performing the service operation.</param>
        /// <returns>The result of the service operation.</returns>
        public static async Task<TResult> RequestAsync<TService, TArgument, TResult>(
            this Socket socket,
            Expression<Func<TService, Func<TArgument, TResult>>> serviceMethod,
            TArgument argument)
        {
            var targetMethod =
                (((serviceMethod.Body as UnaryExpression)
                  ?.Operand as MethodCallExpression)
                 ?.Object as ConstantExpression)
                ?.Value as MethodInfo;

            if (targetMethod == null)
            {
                throw new ArgumentException("Target IWorker method could not be found from worker method expression.",
                                            nameof(serviceMethod));
            }

            await SendWrapperRequestAsync(socket, OperationWrapper.ForRequest(targetMethod.Name, new object[] { argument })).ConfigureAwait(false);

            return (await GetWrapperResponseAsync(socket).ConfigureAwait(false)).GetResultAs<TResult>();
        }

        /// <summary>
        /// Sends a client request across the network socket and retrieves the result.
        /// </summary>
        /// <typeparam name="TService">The contract for the service.</typeparam>
        /// <typeparam name="TArgument">The type of the argument for the requested service operation.</typeparam>
        /// <typeparam name="TResult">The type of the return value of the requested service operation.</typeparam>
        /// <param name="socket">The TCP client socket.</param>
        /// <param name="asyncServiceMethod">Projection of the requested asynchronous service method.</param>
        /// <param name="argument">The argument to send when making performing the service operation.</param>
        /// <returns>The result of the service operation.</returns>
        public static async Task<TResult> RequestAsync<TService, TArgument, TResult>(
            this Socket socket,
            Expression<Func<TService, Func<TArgument, Task<TResult>>>> asyncServiceMethod,
            TArgument argument)
        {
            var targetMethod =
                (((asyncServiceMethod.Body as UnaryExpression)
                  ?.Operand as MethodCallExpression)
                 ?.Object as ConstantExpression)
                ?.Value as MethodInfo;

            if (targetMethod == null)
            {
                throw new ArgumentException("Target IWorker method could not be found from worker method expression.",
                                            nameof(asyncServiceMethod));
            }

            await SendWrapperRequestAsync(socket, OperationWrapper.ForRequest(targetMethod.Name, new object[] { argument })).ConfigureAwait(false);

            return (await GetWrapperResponseAsync(socket).ConfigureAwait(false)).GetResultAs<TResult>();
        }

        /// <summary>
        /// Begins a communication task with a client socket using the worker instance to perform requested operations.
        /// </summary>
        /// <typeparam name="TService">The contract for the service.</typeparam>
        /// <param name="worker">The TService instance which will fulfill requested operations.</param>
        /// <param name="clientSocket">The client socket connection.</param>
        /// <param name="onRequestReceived">(Optional) Action to perform when a client request has been received.</param>
        /// <param name="onSendingResponse">(Optional) Action to perform once the worker has processed the operation and prior to sending the server response.</param>
        /// <param name="onError">(Optional) Action to perform when processing encounters an error.</param>
        /// <returns>Awaitable task hosting the background processing of the client socket connection.</returns>
        public static async Task HandleClientAsync<TService>(
            this TService worker, Socket clientSocket,
            CancellationToken cancellationToken = default,
            Action<string, byte[][]> onRequestReceived = null,
            Action<string, object> onSendingResponse = null,
            Action<Exception> onError = null)
        {
            await Task.Run(async () =>
            {
                try
                {
                    // Continue reading as long as client requests are available.
                    OperationWrapper request;
                    while (!cancellationToken.IsCancellationRequested &&
                           clientSocket.Connected &&
                           (request = await clientSocket.GetWrapperResponseAsync().ConfigureAwait(false)) != OperationWrapper.SessionEnded)
                    {
                        onRequestReceived?.Invoke(request.Operation, request.Arguments);

                        object result = await worker.InvokeRequestAsync(request).ConfigureAwait(false);

                        // Now send back a response.
                        onSendingResponse?.Invoke(request.Operation, result);
                        await clientSocket.SendWrapperRequestAsync(OperationWrapper.FromResult(request.Operation, result)).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    if (onError == null)
                    {
                        throw;
                    }

                    onError(ex);
                }
            });
        }
        
        /// <summary>
        /// Begins a communication task with a client socket using the worker instance to perform requested operations.
        /// </summary>
        /// <typeparam name="TService">The contract for the service.</typeparam>
        /// <param name="worker">The TService instance which will fulfill requested operations.</param>
        /// <param name="clientSocket">The client socket connection.</param>
        /// <param name="onRequestReceived">(Optional) Action to perform when a client request has been received.</param>
        /// <param name="onSendingResponse">(Optional) Action to perform once the worker has processed the operation and prior to sending the server response.</param>
        /// <param name="onError">(Optional) Action to perform when processing encounters an error.</param>
        /// <returns>Awaitable task hosting the background processing of the client socket connection.</returns>
        public static async Task HandleClientAsync<TService>(
            this TService worker, Socket clientSocket,
            CancellationToken cancellationToken = default,
            Func<string, byte[][], Task> onRequestReceived = null,
            Func<string, object, Task> onSendingResponse = null,
            Func<Exception, Task> onError = null)
        {
            await Task.Run(async () =>
            {
                try
                {
                    // Continue reading as long as client requests are available.
                    OperationWrapper request;
                    while (!cancellationToken.IsCancellationRequested &&
                           clientSocket.Connected &&
                           (request = await clientSocket.GetWrapperResponseAsync().ConfigureAwait(false)) != OperationWrapper.SessionEnded)
                    {
                        if (onRequestReceived != null)
                        {
                            await onRequestReceived.Invoke(request.Operation, request.Arguments).ConfigureAwait(false);
                        }

                        object result = await worker.InvokeRequestAsync(request).ConfigureAwait(false);

                        // Now send back a response.
                        if (onSendingResponse != null)
                        {
                            await onSendingResponse.Invoke(request.Operation, result).ConfigureAwait(false);
                        }

                        await clientSocket.SendWrapperRequestAsync(OperationWrapper.FromResult(request.Operation, result)).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    if (onError == null)
                    {
                        throw;
                    }

                    await onError.Invoke(ex).ConfigureAwait(false);
                }
            });
        }
    }
}
