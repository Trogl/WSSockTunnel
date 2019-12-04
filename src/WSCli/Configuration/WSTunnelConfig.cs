namespace WSCli.Configuration
{
    public class WSTunnelConfig
    {
        public string ServerUri { get; set; }
        public string Kid { get; set; }

        public string Passwd { get; set; }
        public int Port { get; set; }

        public ProxyConfig Proxy { get; set; }

    }

    public class ProxyConfig
    {
        public string Server { get; set; }
        public int Port { get; set; }

        public string Login { get; set; }
        public string Passwd { get; set; }
        public bool UseDefaultCredentials { get; set; }
    }
}