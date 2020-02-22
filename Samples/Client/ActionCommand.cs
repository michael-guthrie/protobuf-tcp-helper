namespace ProtobufTcpHelpers.Sample.Client
{
    using System;
    using System.Windows.Input;

    public class ActionCommand : ICommand
    {
        private readonly Action _action;
        private bool _isBusy;

        public ActionCommand(Action action)
        {
            _action = action;
        }

        public bool CanExecute(object parameter) => !_isBusy;

        public void Execute(object parameter)
        {
            _isBusy = true;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            try
            {
                _action();
            }
            finally
            {
                _isBusy = false;
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler CanExecuteChanged;
    }
}
