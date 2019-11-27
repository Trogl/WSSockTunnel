using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using WSCli.Configuration;
using WSCli.Logging;
using WSCli.WS;
using WSdto;
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
            try
            {
                log.LogInformation("Инициализация");
                wsTunnelConfig = ConfigWatcher.GetSection("wstunnel").ConvertValue<WSTunnelConfig>();


                client = new WsClient();
                await client.OpenManagerTunnel(wsTunnelConfig.ServerUri, DisconnectTunnel, DisconnectSocket);
                await client.Auth(wsTunnelConfig.Passwd, wsTunnelConfig.Kid);
                var tunnelGuid = await client.OpenDataTunnel(DataReceiver, DisconnecDataTunnel);
                var testObj = new EchoReq {ReqId = Guid.NewGuid(), Timestamp = DateTime.UtcNow};
                await client.SendData(tunnelGuid, Guid.Empty, Encoding.UTF8.GetBytes(testObj.ToJson()));
                log.LogInformation($"Послали echo - {testObj.ReqId}");

            }
            catch (Exception e)
            {
                log.LogError($"Ошибка инициализации: {e.Message}");
                client.Dispose();
            }



        }



        private static Task DisconnectSocket(Guid socketId)
        {
            return Task.CompletedTask;
        }

        private static Task DisconnectTunnel()
        {
            return Task.CompletedTask;
        }

        private static Task DisconnecDataTunnel(Guid socketId)
        {
            return Task.CompletedTask;
        }

        private static async Task DataReceiver(Guid socketId, byte[] data)
        {
            if (socketId == Guid.Empty)
                await EchoResponce(data);


        }

        private static Task EchoResponce(byte[] payload)
        {
            var str = Encoding.UTF8.GetString(payload);
            var echoRes = JsonConvert.DeserializeObject<EchoRes>(str, JsonSettings.settings);
            log.LogInformation($"Получили ответ на эхо - {echoRes.ReqId}");
            return Task.CompletedTask;
        }

        public static Task Stop()
        {
            client.Dispose();
            return Task.CompletedTask;
        }
    }
}
