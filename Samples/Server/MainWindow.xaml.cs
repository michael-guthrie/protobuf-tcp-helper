namespace ProtobufTcpHelpers.Sample.Server
{
    using Newtonsoft.Json;
    using ProtoBuf;
    using ProtobufTcpHelpers;
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Net.Sockets;
    using System.Threading.Tasks;
    using System.Windows;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly Worker _worker;
        private bool _isOpen = true;

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
                        var _ = _worker.HandleClient(
                            client,
                            onRequestReceived: async (op, body) =>
                                await Dispatcher.InvokeAsync(
                                    () => OutputBox.AppendText(GetOutputText(op, body) + Environment.NewLine))).ConfigureAwait(false);
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

        private string GetOutputText(string operation, byte[] body)
        {
            string text = operation + Environment.NewLine;
            if (body == null)
            {
                return text;
            }

            using (var ms = new MemoryStream(body))
            {
                text += JsonConvert.SerializeObject(Serializer.Deserialize(typeof(IWorker).GetMethod(operation).ReturnType, ms)) + Environment.NewLine;
            }

            return text;
        }
    }
}
