namespace ProtobufTcpHelpers.Sample.Server
{
    using Newtonsoft.Json;
    using ProtoBuf;
    using ProtobufTcpHelpers;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net.Sockets;
    using System.Reflection;
    using System.Threading.Tasks;
    using System.Windows;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly Worker _worker;
        private bool _isOpen = true;

        private long _bytesReceived;
        private long _bytesSent;

        public MainWindow()
        {
            _worker = new Worker();
            InitializeComponent();
            StartListener();
        }

        private void StartListener()
        {
            Task.Run(async () =>
            {
                var server = new TcpListener(ConnectionConstants.Server);
                try
                {
                    server.Start();
                    while (_isOpen)
                    {
                        TcpClient client = await server.AcceptTcpClientAsync();
                        var _ = _worker.HandleClientAsync(
                            client,
                            onRequestReceived: async (op, body) =>
                                await Dispatcher.InvokeAsync(
                                    () =>
                                    {
                                        _bytesReceived += body?.Sum(a => a?.Length ?? 0) ?? 0;
                                        //OutputBox.AppendText(GetOutputText(op, body) + Environment.NewLine);
                                        BytesReceived.Text = $"{_bytesReceived:N0}";
                                        TotalTransfer.Text = $"{_bytesReceived + _bytesSent:N0}";
                                    }),
                            onSendingResponse: async (op, body) =>
                                await Dispatcher.InvokeAsync(
                                    () =>
                                    {
                                        _bytesSent += body?.Length ?? 0;
                                        BytesSent.Text = $"{_bytesSent:N0}";
                                        TotalTransfer.Text = $"{_bytesReceived + _bytesSent:N0}";
                                    })
                                ).ConfigureAwait(false);
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

        protected override void OnClosed(EventArgs e)
        {
            _isOpen = false;
        }

        private string GetOutputText(string operation, byte[][] arguments)
        {
            string text = operation + Environment.NewLine;
            if (arguments == null)
            {
                return text;
            }

            text += JsonConvert.SerializeObject(ParseArguments(operation, arguments)) + Environment.NewLine;

            return text;
        }

        private static Dictionary<string, object> ParseArguments(string operation, byte[][] binArgs)
        {
            if (!(binArgs?.Length > 0))
            {
                return new Dictionary<string, object>();
            }

            var method = typeof(IWorker).GetMethod(operation);
            ParameterInfo[] methodParameters = method.GetParameters();
            if (binArgs.Length != methodParameters.Length)
            {
                throw new ArgumentException("Given request parameters did not match parameters for the operation.");
            }

            var arguments = new Dictionary<string, object>(binArgs.Length);
            for (int i = 0; i < methodParameters.Length; i++)
            {
                if (binArgs[i] == null)
                {
                    arguments.Add(methodParameters[i].Name, null);
                    continue;
                }
                using var stream = new MemoryStream(binArgs[i]);
                arguments.Add(methodParameters[i].Name, Serializer.Deserialize(methodParameters[i].ParameterType, stream));
            }
            return arguments;
        }
    }
}
