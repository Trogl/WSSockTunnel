using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using WS.Common;
using WSCli.Configuration;
using WSCli.Logging;
using WSCli.MX;
using WSdto;
using WSdto.Json;

namespace WSCli.WS
{

    internal class ReqInfo
    {
        public string CorrelationId;
        public DateTime ExpireDate;
        public TaskCompletionSource<JObject> CompletionSource;
        public CancellationTokenSource CancellationTokenSource;
    }


    public class DataTunnelInfo
    {
        public Guid TunnelId { get; set; }
        public string Uri { get; set; }
        public Aes Key { get; set; }
        public int BlockSize { get; set; }
        public ClientWebSocket Socket { get; set; }

        public Func<Guid, Task>  DisconectCallback;
    }

    public  class WsClient : IDisposable
    {
        private  ILogger log = AppLogging.CreateLogger(nameof(WsClient));
        private  ClientWebSocket managerWebSocket;

        private readonly ConcurrentDictionary<string, ReqInfo> reqResultHandlers = new ConcurrentDictionary<string, ReqInfo>();
        private readonly ConcurrentDictionary<Guid, DataTunnelInfo> dataTunnelInfos = new ConcurrentDictionary<Guid, DataTunnelInfo>();
        private Action<byte[]> resiveAction;
        private MessageEncoder mEncoder = new MessageEncoder();
        private DataEncryptor dEncoder = new DataEncryptor();
        private Uri serverUri;
        private WSTunnelConfig config;




        static WsClient()
        {
        }
        public async Task OpenManagerTunnel(WSTunnelConfig config, Func<Task> disconnectTunnel, Func<Guid,Task> disconnectSocket)
        {
            managerWebSocket = new ClientWebSocket(); 
            serverUri = new Uri(config.ServerUri);
            this.config = config;
            log.LogInformation($"Подключение к {serverUri}");

            try
            {
                if (!string.IsNullOrWhiteSpace(config.Proxy?.Server))
                {
                     var proxy = new WebProxy(config.Proxy.Server, config.Proxy.Port);

                     proxy.UseDefaultCredentials = config.Proxy.UseDefaultCredentials;
                     if (!string.IsNullOrWhiteSpace(config.Proxy.Login))
                     {
                         proxy.Credentials = new NetworkCredential(config.Proxy.Login,config.Proxy.Passwd);
                     }

                     managerWebSocket.Options.Proxy = proxy;
                }

                await managerWebSocket.ConnectAsync(serverUri, CancellationToken.None);
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"{ex.Message}");
                throw;
            }


            log.LogInformation($"Есть контакт");
            RunManagerReceiver(disconnectTunnel, disconnectSocket);
        }
        public async Task Auth()
        {
            var aRes = await SendAReq(new AReq { Passwd = config.Passwd });
            if (aRes.Status == ResStatus.Ok)
            {
                log.LogTrace($"Авторизация пройдена");
                return;
            }
            else
            {
                log.LogInformation($"Ошибка авторизации: {aRes.Error.Message} ({aRes.Error.Code})");
                await Stop();
                throw new Exception(aRes.Error.Code);
            }
        }
        public async Task<Guid> OpenDataTunnel(Func<Guid, byte[], Task> dataReceiver, Func<Guid,Task> disconnectTunnel)
        {
            var res = await SendDataTunnelRequest();
            if (res.Status != ResStatus.Ok)
            { 
                log.LogError($"Ошибка открытия дата тунеля: {res.Error.Code}");
                throw new Exception($"Ошибка открытия дата тунеля: {res.Error.Code}");
            }

            var ws  = new ClientWebSocket();
            var wsUri = new Uri($"{serverUri}/{res.DTUri}");
            log.LogInformation($"Подключение к {wsUri}");


            try
            {

                if (!string.IsNullOrWhiteSpace(config.Proxy?.Server))
                {
                    var proxy = new WebProxy(config.Proxy.Server, config.Proxy.Port);

                    proxy.UseDefaultCredentials = config.Proxy.UseDefaultCredentials;
                    if (!string.IsNullOrWhiteSpace(config.Proxy.Login))
                    {
                        proxy.Credentials = new NetworkCredential(config.Proxy.Login, config.Proxy.Passwd);
                    }

                    managerWebSocket.Options.Proxy = proxy;
                }

                await ws.ConnectAsync(wsUri, CancellationToken.None);
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Ошибка соединения :{ex.Message}");
                throw;
            }

            var dtInfo = new DataTunnelInfo
            {
                TunnelId = Guid.NewGuid(),
                Uri = wsUri.ToString(),
                BlockSize = res.DTBS,
                Socket = ws,
                DisconectCallback = disconnectTunnel
            };

            try
            {
                var aes = Aes.Create();
                aes.Key = res.DTKey;
                aes.IV = res.DTIV;
                dtInfo.Key = aes;

            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Ошибка ключа :{ex.Message}");
                throw;
            }

            dataTunnelInfos.TryAdd(dtInfo.TunnelId, dtInfo);

            RunDataReceiver(ws, dtInfo, dataReceiver);
            log.LogInformation($"Дата тунель открыт");

            return dtInfo.TunnelId;


        }

        public async Task CloseSocket(Guid socketId)
        {
            var msg = new Message
            {
                CorrelationId = Guid.NewGuid().ToString("N"),
                Type = "DisconEvnt",
                Payload = new DisconEvnt
                {
                    SocketId = socketId
                },
                TimeStamp = DateTime.UtcNow
            };

            var data = await mEncoder.Encode(msg, config.Kid);

            await managerWebSocket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true,
                CancellationToken.None);
        }

        public  async Task Stop()
        {
            await managerWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);

        }

        public async Task<IPAddress> CreateConnection(Guid socketId, string addr, short port)
        {
            var msg = new Message
            {
                CorrelationId = Guid.NewGuid().ToString("N"),
                Type = "ConReq",
                Payload = new ConReq
                {
                    SocketId = socketId,
                    Addr = addr,
                    Port = port,
                    Timeout = TimeSpan.FromSeconds(30)
                },
                TimeStamp = DateTime.UtcNow
            };

            var jObj = await SendManagerCommand(msg,40);

            var res = jObj.ConvertValue<ConRes>();
            if (res.Status == ResStatus.Ok)
                return new IPAddress(res.Ip);

            return null;

        }
        //internal
        private  void RunManagerReceiver(Func<Task> disconnectTunnel, Func<Guid, Task> disconnectSocket)
        {
            Task.Run(async () =>
            {
                await ReceiveAsync(managerWebSocket, MessageHandler, CloseHandler);
                if (managerWebSocket.State == WebSocketState.Open)
                {
                    await managerWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                }

                log.LogInformation($"Дисконект MT");
                await disconnectTunnel();
            });
            
            async Task MessageHandler(byte[] arg)
            {
                var msg = await mEncoder.Decode(arg);
                
                if (msg.Type == "DisconEvnt")
                {
                    var de = msg.Payload.ConvertValue<DisconEvnt>();
                    await disconnectSocket(de.SocketId);
                    return;
                }

                if (reqResultHandlers.TryRemove(msg.CorrelationId, out var reqInfo))
                {
                    reqInfo.CompletionSource.TrySetResult(msg.Payload.ConvertValue<JObject>());
                }
            }

            Task CloseHandler()
            {
                return Task.CompletedTask;
            }
        }
        private  async Task<ARes> SendAReq(AReq aReq)
        {
            var msg = new Message
            {
                CorrelationId = Guid.NewGuid().ToString("N"),
                Type = "AReq",
                Payload = aReq,
                TimeStamp = DateTime.UtcNow
            };

            var jObj = await SendManagerCommand(msg);
            return jObj.ConvertValue<ARes>();
        }
        private async Task<DTARes> SendDataTunnelRequest()
        {
            var msg = new Message
            {
                CorrelationId = Guid.NewGuid().ToString("N"),
                Type = "DTAReq",
                Payload = new DTAReq(),
                TimeStamp = DateTime.UtcNow
            };

            var jObj = await SendManagerCommand(msg);
            return jObj.ConvertValue<DTARes>();
        }
        private  async Task<JObject> SendManagerCommand(Message msg, int timeout = 5)
        {
            var data = await mEncoder.Encode(msg, config.Kid);
            var tcs = RegisterSimpleCommandResultHandler(msg.CorrelationId, timeout);
            await managerWebSocket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true,
                CancellationToken.None);

            return await tcs.Task;

        }
        // common
        private  async Task ReceiveAsync(ClientWebSocket socket, Func<byte[], Task> messageHandler, Func<Task> closeHandler)
        {
            var bytes = new byte[1024*16];
            var buffer = new ArraySegment<byte>(bytes);
            while (socket.State == WebSocketState.Open)
            {
                var ms = new MemoryStream();
                try
                {

                    WebSocketReceiveResult received;
                    do
                    {
                        received = await socket.ReceiveAsync(buffer, CancellationToken.None);
                        await ms.WriteAsync(bytes, 0, received.Count);

                    } while (!received.EndOfMessage);


                    if (received.MessageType == WebSocketMessageType.Close)
                    {
                        if(socket.State == WebSocketState.CloseReceived)
                            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                        await closeHandler();
                    }
                    else
                    {
                        await messageHandler(ms.ToArray());
                    }
                }
                catch (Exception ex)
                {
                    log.LogError($"При получении данных произошла ошибка: {ex.Message} ");
                }
            }
        }
        private  TaskCompletionSource<JObject> RegisterSimpleCommandResultHandler(string correlationId, int timeOut)
        {
            var info = new ReqInfo
            {
                CorrelationId = correlationId,
                ExpireDate = DateTime.UtcNow + TimeSpan.FromSeconds(timeOut),
                CompletionSource = new TaskCompletionSource<JObject>(),
                CancellationTokenSource = new CancellationTokenSource()
            };

            reqResultHandlers.TryAdd(correlationId, info);

            info.CancellationTokenSource.Token.Register(() =>
            {
                if (reqResultHandlers.TryRemove(correlationId, out var scrh))
                {
                    scrh.CompletionSource.TrySetException(new TimeoutException());
                }
            });
            info.CancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(timeOut));

            return info.CompletionSource;
        }
        //data
        private  void RunDataReceiver(ClientWebSocket socket, DataTunnelInfo dInfo, Func<Guid, byte[], Task> dataReceiver)
        {
            Task.Run(async () =>
            {
                await ReceiveAsync(socket, MessageHandler, CloseHandler);
                if (socket.State == WebSocketState.Open)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                }

                log.LogInformation($"Дисконект DT");
            });

            async Task MessageHandler(byte[] encryptedData)
            {
                var data = await dEncoder.Decrypt(dInfo.Key, encryptedData);
                await using var ms  = new MemoryStream(data);
                using var br = new BinaryReader(ms);
                var guidb = br.ReadBytes(16);
                var payloadSize = br.ReadInt32();
                var payload = br.ReadBytes(payloadSize);

                await dataReceiver(new Guid(guidb), payload);


            }

            async Task CloseHandler()
            {
                await RemoveConnection(dInfo.TunnelId);
            }
        }       

        public  async Task SendData(Guid tunnelId, Guid socketId, byte[] buffer, int size)
        {
            if (dataTunnelInfos.TryGetValue(tunnelId, out var tunnelInfo))
            {
                await using var ms = new MemoryStream();
                await using var bw = new BinaryWriter(ms);
                bw.Write(socketId.ToByteArray());
                bw.Write(size);
                bw.Write(buffer,0, size);
                bw.Flush();

                var encodedData = await dEncoder.Encrypt(tunnelInfo.Key, tunnelInfo.BlockSize, ms.ToArray());

                await SendData(tunnelInfo, encodedData);
            }
        }

        private async Task SendData(DataTunnelInfo tunnelInfo, byte[] data)
        {
            if (tunnelInfo.Socket.State != WebSocketState.Open)
            {
                await RemoveConnection(tunnelInfo.TunnelId);
            }
            try
            {
                await tunnelInfo.Socket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true, CancellationToken.None);
            }
            catch (Exception)
            {
                await RemoveConnection(tunnelInfo.TunnelId);
            }
        }



        private async Task RemoveConnection(Guid dataTunnelId)
        {
            if (dataTunnelInfos.TryRemove(dataTunnelId, out var tunnelInfo))
            {
                if(tunnelInfo.Socket.State == WebSocketState.Open)
                    await tunnelInfo.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);

                tunnelInfo.Key.Dispose();
                await tunnelInfo.DisconectCallback(dataTunnelId);
            }
        }

        public void Dispose()
        {
            foreach (var dataTunnelInfo in dataTunnelInfos)
            {
                if (dataTunnelInfo.Value.Socket.State == WebSocketState.Open)
                    dataTunnelInfo.Value.Socket
                        .CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).Wait();
            }

            dataTunnelInfos.Clear();

            if (managerWebSocket?.State == WebSocketState.Open)
                managerWebSocket?.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).Wait();
            managerWebSocket?.Dispose();
        }


    }


}
