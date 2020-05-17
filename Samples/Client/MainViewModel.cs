namespace ProtobufTcpHelpers.Sample.Client
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Threading.Tasks;
    using System.Windows.Input;
    using ProtobufTcpHelpers.Sample.Client.Annotations;
    using ProtobufTcpHelpers.Sample.Models;

    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly IWorker _workerClient;

        public PocTypeA TypeA
        {
            get => _typeA;
            set
            {
                _typeA = value;
                OnPropertyChanged();
            }
        }

        private PocTypeA _typeA = new PocTypeA();

        public PocTypeB TypeB
        {
            get => _typeB;
            set
            {
                _typeB = value;
                OnPropertyChanged();
            }
        }

        private PocTypeB _typeB = new PocTypeB();

        public int TypeAChildCount
        {
            get => _typeAChildCount;
            set
            {
                if (value != _typeAChildCount)
                {
                    _typeAChildCount = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _typeAChildCount = 10;

        public ICollection<PocTypeB> TypeBs
        {
            get => _typeBs;
            set
            {
                _typeBs = value;
                OnPropertyChanged();
            }
        }
        private ICollection<PocTypeB> _typeBs = new List<PocTypeB>();

        public ICommand OnSendTypeA { get; }
        public ICommand OnSendTypeB { get; }
        public ICommand OnGetTypeBs { get; }

        public event PropertyChangedEventHandler PropertyChanged;

        public MainViewModel()
        {
            OnSendTypeA = new ActionCommand(async () => await SendTypeA());
            OnSendTypeB = new ActionCommand(async () => await SendTypeB());
            OnGetTypeBs = new ActionCommand(async () => await GetTypeBs());
            _workerClient = new WorkerClient(ConnectionConstants.Server);
        }

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private async Task SendTypeA()
        {
            TypeA.Children = CreateRandomChildren();

            int result = await _workerClient.SendTypeAAsync(TypeA);

            Debug.WriteLine($"{nameof(SendTypeA)} : {result}");
        }

        private ICollection<PocTypeAChild> CreateRandomChildren()
        {
            var r = new Random();
            PocTypeAChild RandomChild() => new PocTypeAChild {Code = Guid.NewGuid().ToString(), Id = r.Next()};
            return Enumerable.Repeat<Func<PocTypeAChild>>(RandomChild, TypeAChildCount).Select(fn => fn()).ToList();
        }

        private async Task SendTypeB()
        {
            int result = await _workerClient.SendTypeBAsync(TypeB);

            Debug.WriteLine($"{nameof(SendTypeB)} : {result}");
        }

        private async Task GetTypeBs()
        {
            TypeBs = await _workerClient.GetTypeBsAsync();
        }
    }
}
