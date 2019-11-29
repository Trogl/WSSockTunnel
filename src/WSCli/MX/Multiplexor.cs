using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using WSCli.Configuration;
using WSCli.Logging;
using WSCli.TCP;
using WSCli.WS;
using WSdto;
using WSdto.Json;

namespace WSCli.MX
{
    public class WSDataConnectionInfo
    {
        public Guid TunnelId;
    }


    static class Multiplexor
    {
        private static ILogger log = AppLogging.CreateLogger(nameof(Multiplexor));

        private static WsClient client;
        private static Socks5Server server;
        private static WSTunnelConfig wsTunnelConfig;

        private static ConcurrentDictionary<Guid, WSDataConnectionInfo> dataConnections = new ConcurrentDictionary<Guid, WSDataConnectionInfo>();

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
                dataConnections.TryAdd(tunnelGuid, new WSDataConnectionInfo
                {
                    TunnelId = tunnelGuid
                });
                var testObj = new EchoReq {ReqId = Guid.NewGuid(), Timestamp = DateTime.UtcNow};
                var buffer = Encoding.UTF8.GetBytes(testObj.ToJson());
                await client.SendData(tunnelGuid, Guid.Empty, buffer, buffer.Length);
                log.LogInformation($"Послали echo - {testObj.ReqId}");

                server = new Socks5Server();
                server.Start(wsTunnelConfig.Port, RequireNewConnection, TransferData, CloseSocket );

            }
            catch (Exception e)
            {
                log.LogError($"Ошибка инициализации: {e.Message}");
                server?.Dispose();
                client?.Dispose();
            }
        }




        private static async Task<IPAddress> RequireNewConnection(Guid socketId, string addr, short port)
        {
            return await client.CreateConnection(socketId, addr, port);
        }

        private static  async Task<bool> TransferData(Guid socketId, byte[] buffer, int size)
        {
            var first = dataConnections.FirstOrDefault();
            await client.SendData(first.Key, socketId, buffer, size);
            return true;
        }

        private static Task CloseSocket(Guid socketId)
        {
            return client.CloseSocket(socketId);
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

            await server.SendData(socketId, data);
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
            server?.Dispose();
            client?.Dispose();
            return Task.CompletedTask;
        }
    }
}
