namespace ProtobufTcpHelpers
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Net.Sockets;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using ProtoBuf;

    public static partial class Extensions
    {
        private static object InvokeRequest<T>(this T worker, OperationWrapper request)
        {
            //MethodInfo method;
            //if (!OperationCache.TryGetValue(typeof(T), out Dictionary<string, MethodInfo> workerCache))
            //{
            //    workerCache = new Dictionary<string, MethodInfo>();
            //    OperationCache.Add(typeof(T), workerCache);
            //}
            //if (!workerCache.TryGetValue(request.Operation, out method))
            //{
            //    method = typeof(T).GetMethod(request.Operation) ??
            //        throw new ArgumentException("Request operation was invalid.", nameof(request));
            //    workerCache.Add(request.Operation, method);
            //}
            MethodInfo method = typeof(T).GetMethod(request.Operation) ??
                                throw new ArgumentException("Request operation was invalid.", nameof(request));

            object[] arguments = null;
            ParameterInfo[] methodParameters = method.GetParameters();
            if (methodParameters.Length > 0)
            {
                if (!(request.Arguments.Length >= methodParameters.Length))
                {
                    throw new ArgumentException("Given request parameters did not match parameters for the operation.");
                }
                arguments = new object[methodParameters.Length];
                for (int i = 0; i < methodParameters.Length; i++)
                {
                    if (request.Arguments[i]?.Length > 0)
                    {
                        using var stream = new MemoryStream(request.Arguments[i]);
                        arguments[i] = Serializer.Deserialize(methodParameters[i].ParameterType, stream);
                    }
                    else
                    {
                        arguments[i] = null;
                    }
                }
            }

            bool isAwaitable = method.ReturnType.GetMethod(nameof(Task.GetAwaiter)) != null;
            if (!isAwaitable)
            {
                return method.Invoke(worker, arguments);
            }

            var task = (Task) method.Invoke(worker, arguments);
            PropertyInfo resultProperty = task.GetType().GetProperty("Result") ??
                                          throw new InvalidOperationException(
                                              $"Method {request.Operation} must return a result.");
            return resultProperty.GetValue(task);
        }

        private static OperationWrapper GetWrapperResponse(this NetworkStream stream, int bufferSize = 8192)
        {
            // Read the size header.
            var sizeHeader = new byte[14];
            stream.Read(sizeHeader, 0, sizeHeader.Length);

            // Validate the size header.
            if (sizeHeader[0] != byte.MaxValue ||
                sizeHeader[1] != byte.MinValue ||
                sizeHeader[2] != byte.MaxValue ||
                sizeHeader[11] != byte.MinValue ||
                sizeHeader[12] != byte.MaxValue ||
                sizeHeader[13] != byte.MinValue)
            {
                if (sizeHeader.All(b => b == byte.MinValue) && !stream.DataAvailable)
                {
                    return OperationWrapper.SessionEnded;
                }

                throw new InvalidOperationException(
                    "Unexpected message received. Message did not represent a valid size header.");
            }

            // Now parse the size.
            long messageSize = BitConverter.ToInt64(new ArraySegment<byte>(sizeHeader, 3, sizeof(long)));

            using var ms = new MemoryStream();
            //var buffer = new byte[1024];
            var buffer = new byte[bufferSize];
            int readLength = 0;
            while (readLength < messageSize)
            {
                int blockSize = (int) Math.Min(messageSize - readLength, buffer.Length);
                readLength += stream.Read(buffer, 0, blockSize);
                ms.Write(buffer, 0, blockSize);
            }

            ms.Position = 0;
            return Serializer.Deserialize<OperationWrapper>(ms);
        }

        private static void SendWrapperRequest(this NetworkStream stream, OperationWrapper request)
        {
            // Serialize in memory so we can get the size before sending to the network buffer.
            using var ms = new MemoryStream();
            Serializer.Serialize(ms, request);

            // Send the size header.
            byte[] sizeHeader = new byte[14];
            sizeHeader[0] = sizeHeader[2] = sizeHeader[12] = byte.MaxValue;
            Array.Copy(BitConverter.GetBytes(ms.Length), 0, sizeHeader, 3, sizeof(long));

            stream.Write(sizeHeader, 0, sizeHeader.Length);

            // Send the client request.
            ms.Position = 0;
            ms.CopyTo(stream);
        }

        /// <summary>
        /// Sends a client request across the network stream and retrieves the result.
        /// </summary>
        /// <typeparam name="TService">The contract for the service.</typeparam>
        /// <typeparam name="TResult">The type of the return value of the requested service operation.</typeparam>
        /// <param name="stream">The network stream from the TCP client.</param>
        /// <param name="serviceMethod">Projection of the requested service method.</param>
        /// <returns>The result of the service operation.</returns>
        public static TResult Request<TService, TResult>(
            this NetworkStream stream,
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

            SendWrapperRequest(stream, OperationWrapper.ForRequest(targetMethod.Name));

            return GetWrapperResponse(stream).GetResultAs<TResult>();
        }

        /// <summary>
        /// Sends a client request across the network stream and retrieves the result.
        /// </summary>
        /// <typeparam name="TService">The contract for the service.</typeparam>
        /// <typeparam name="TResult">The type of the return value of the requested service operation.</typeparam>
        /// <param name="stream">The network stream from the TCP client.</param>
        /// <param name="asyncServiceMethod">Projection of the requested asynchronous service method.</param>
        /// <returns>The result of the service operation.</returns>
        public static TResult Request<TService, TResult>(
            this NetworkStream stream,
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

            SendWrapperRequest(stream, OperationWrapper.ForRequest(targetMethod.Name));

            return GetWrapperResponse(stream).GetResultAs<TResult>();
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
        public static TResult Request<TService, TArgument, TResult>(
            this NetworkStream stream,
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

            SendWrapperRequest(stream, OperationWrapper.ForRequest(targetMethod.Name, new object[] { argument }));

            return GetWrapperResponse(stream).GetResultAs<TResult>();
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
        public static TResult Request<TService, TArgument, TResult>(
            this NetworkStream stream,
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

            SendWrapperRequest(stream, OperationWrapper.ForRequest(targetMethod.Name, new object[] { argument }));

            return GetWrapperResponse(stream).GetResultAs<TResult>();
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
        public static void HandleClient<TService>(
            this TService worker, TcpClient client,
            Action<string, byte[][]> onRequestReceived = null,
            Action<string, byte[]> onSendingResponse = null,
            Action<Exception> onError = null)
        {
            try
            {
                using NetworkStream stream = client.GetStream();

                // Continue reading as long as client requests are available.
                OperationWrapper request;
                while (client.Connected && stream.CanRead &&
                       (request = stream.GetWrapperResponse(client.ReceiveBufferSize)) != OperationWrapper.SessionEnded)
                {
                    onRequestReceived?.Invoke(request.Operation, request.Arguments);

                    object result = worker.InvokeRequest(request);

                    // Now send back a response.
                    var response = OperationWrapper.FromResult(request.Operation, result);
                    onSendingResponse?.Invoke(request.Operation, response.Result);
                    stream.SendWrapperRequest(response);

                    stream.Flush();
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
        }

        internal static OperationWrapper GetWrapperResponse(this Socket socket)
        {
            // Read the size header.
            var sizeHeader = new byte[14];
            if (socket.Receive(sizeHeader, SocketFlags.None) == 0)
            {
                return OperationWrapper.SessionEnded;
            }

            // Validate the size header.
            if (sizeHeader[0] != byte.MaxValue ||
                sizeHeader[1] != byte.MinValue ||
                sizeHeader[2] != byte.MaxValue ||
                sizeHeader[11] != byte.MinValue ||
                sizeHeader[12] != byte.MaxValue ||
                sizeHeader[13] != byte.MinValue)
            {
                if (sizeHeader.All(b => b == byte.MinValue))
                {
                    return OperationWrapper.SessionEnded;
                }

                throw new InvalidOperationException(
                    "Unexpected message received. Message did not represent a valid size header.");
            }

            // Now parse the size.
            long messageSize = BitConverter.ToInt64(new ArraySegment<byte>(sizeHeader, 3, sizeof(long)));

            using var ms = new MemoryStream();
            var buffer = new byte[Math.Min(socket.ReceiveBufferSize, messageSize)];
            int readLength = 0;
            while (readLength < messageSize)
            {
                if ((messageSize - readLength) < buffer.Length)
                {
                    Array.Resize(ref buffer, (int) messageSize - readLength);
                }
                socket.Receive(buffer, SocketFlags.None);
                readLength += buffer.Length;
                ms.Write(buffer, 0, buffer.Length);
            }

            ms.Position = 0;
            return Serializer.Deserialize<OperationWrapper>(ms);
        }

        internal static void SendWrapperRequest(this Socket socket, OperationWrapper request)
        {
            // Serialize in memory so we can get the size before sending to the network buffer.
            using var ms = new MemoryStream();
            Serializer.Serialize(ms, request);

            // Send the size header.
            byte[] sizeHeader = new byte[14];
            sizeHeader[0] = sizeHeader[2] = sizeHeader[12] = byte.MaxValue;
            Array.Copy(BitConverter.GetBytes(ms.Length), 0, sizeHeader, 3, sizeof(long));

            socket.Send(sizeHeader, SocketFlags.None);

            // Send the client request.
            ms.Position = 0;
            socket.Send(ms.ToArray(), SocketFlags.None);
        }

        /// <summary>
        /// Sends a client request across the network socket and retrieves the result.
        /// </summary>
        /// <typeparam name="TService">The contract for the service.</typeparam>
        /// <typeparam name="TResult">The type of the return value of the requested service operation.</typeparam>
        /// <param name="socket">The TCP client socket.</param>
        /// <param name="serviceMethod">Projection of the requested service method.</param>
        /// <returns>The result of the service operation.</returns>
        public static TResult Request<TService, TResult>(
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

            SendWrapperRequest(socket, OperationWrapper.ForRequest(targetMethod.Name));

            return GetWrapperResponse(socket).GetResultAs<TResult>();
        }

        /// <summary>
        /// Sends a client request across the network socket and retrieves the result.
        /// </summary>
        /// <typeparam name="TService">The contract for the service.</typeparam>
        /// <typeparam name="TResult">The type of the return value of the requested service operation.</typeparam>
        /// <param name="socket">The TCP client socket.</param>
        /// <param name="asyncServiceMethod">Projection of the requested asynchronous service method.</param>
        /// <returns>The result of the service operation.</returns>
        public static TResult Request<TService, TResult>(
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

            SendWrapperRequest(socket, OperationWrapper.ForRequest(targetMethod.Name));

            return GetWrapperResponse(socket).GetResultAs<TResult>();
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
        public static TResult Request<TService, TArgument, TResult>(
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

            SendWrapperRequest(socket, OperationWrapper.ForRequest(targetMethod.Name, new object[] { argument }));

            return GetWrapperResponse(socket).GetResultAs<TResult>();
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
        public static TResult Request<TService, TArgument, TResult>(
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

            SendWrapperRequest(socket, OperationWrapper.ForRequest(targetMethod.Name, new object[] { argument }));

            return GetWrapperResponse(socket).GetResultAs<TResult>();
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
        public static void HandleClient<TService>(
            this TService worker, Socket clientSocket,
            CancellationToken cancellationToken = default,
            Action<string, byte[][]> onRequestReceived = null,
            Action<string, byte[]> onSendingResponse = null,
            Action<Exception> onError = null)
        {
            try
            {
                // Continue reading as long as client requests are available.
                OperationWrapper request;
                while (!cancellationToken.IsCancellationRequested &&
                       clientSocket.Connected &&
                       (request = clientSocket.GetWrapperResponse()) != OperationWrapper.SessionEnded)
                {
                    onRequestReceived?.Invoke(request.Operation, request.Arguments);

                    object result = worker.InvokeRequest(request);

                    // Now send back a response.
                    var response = OperationWrapper.FromResult(request.Operation, result);
                    onSendingResponse?.Invoke(request.Operation, response.Result);
                    clientSocket.SendWrapperRequest(response);
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
        }
    }
}
