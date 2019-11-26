using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WSCli.Configuration;
using WSCli.Logging;
using WSCli.WS;
using WSdto.Json;

namespace WSCli.MX
{
    static class Multiplexor
    {
        private static ILogger log = AppLogging.CreateLogger(nameof(Multiplexor));

        private static WsClient client;
        private static WSTunnelConfig wsTunnelConfig;

        public static async Task Start()
        {
            log.LogInformation("Инициализация");
            wsTunnelConfig = ConfigWatcher.GetSection("wstunnel").ConvertValue<WSTunnelConfig>();


            client = new WsClient();
            await client.OpenManagerTunnel(wsTunnelConfig.ServerUri, DisconnectTunnel, DisconnectSocket);
            await client.Auth(wsTunnelConfig.Passwd, wsTunnelConfig.Kid);
            await client.OpenDataTunnel();

        }

        private static Task DisconnectSocket(Guid socketId)
        {
            throw new NotImplementedException();
        }

        private static Task DisconnectTunnel()
        {
            throw new NotImplementedException();
        }

        public static async Task Stop()
        {

        }
    }
}
