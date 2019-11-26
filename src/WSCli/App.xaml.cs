using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using WSCli.Configuration;
using Application = System.Windows.Application;

namespace WSCli
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            this.Exit += OnExit;
            this.Startup += OnStartup;
        }

        private void OnStartup(object sender, StartupEventArgs e)
        {
               CurrentConfiguration.HostName  = System.Net.Dns.GetHostName();
            
               CurrentConfiguration.HostIp = System.Net.Dns.GetHostAddresses(CurrentConfiguration.HostName).Where(ip => ip.AddressFamily == AddressFamily.InterNetwork).Select(ip => ip.ToString()).ToArray();

               CurrentConfiguration.RootPath = AppDomain.CurrentDomain.BaseDirectory;
               CurrentConfiguration.ConfigPath = Path.Combine(CurrentConfiguration.RootPath, "config");

               ConfigWatcher.Init(CurrentConfiguration.ConfigPath);

               KeyStore.LoadKeys();
        }

        private void OnExit(object sender, ExitEventArgs e)
        {

        }


        //

    }
}
