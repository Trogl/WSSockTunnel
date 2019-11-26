using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Prism.Commands;
using Prism.Mvvm;
using WSCli.Logging;
using WSCli.MX;
using WSCli.WS;

namespace WSCli
{
    class MainWindowVM : BindableBase
    {
        private ILogger log = AppLogging.CreateLogger("App");
        public ObservableCollection<LogEntity> LogEntities { get; }= new ObservableCollection<LogEntity>();

        public MainWindowVM()
        {
            AppLogging.LoggerFactory.AddProvider(new LogEntityLoggerProvider(AddLogAction));

            ConnectsCommand = new DelegateCommand(ConnectMethod);
            DisconnectCommand = new DelegateCommand(DisconnectMethod);

        }

        private void AddLogAction(LogEntity log)
        {

            Application.Current.Dispatcher.BeginInvoke(() => {
                LogEntities.Add(log);
            });

           
        }


        public DelegateCommand ConnectsCommand { get; }
        public DelegateCommand DisconnectCommand { get; }
        public DelegateCommand SettingsCommand { get; }



        private async void ConnectMethod()
        {
            log.LogInformation("Инициализация мультиплексора");
            await Multiplexor.Start();
        }

        private async void DisconnectMethod()
        {
            log.LogInformation("Завершение работы");
            await Multiplexor.Stop();
        }

    }





}
