﻿using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using WS.Common;
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
        private string kid;
        private Uri serverUri;




        static WsClient()
        {
        }
        public async Task OpenManagerTunnel(string uri, Func<Task> disconnectTunnel, Func<Guid,Task> disconnectSocket)
        {
            managerWebSocket = new ClientWebSocket(); 
            serverUri = new Uri(uri);
            log.LogInformation($"Подключение к {serverUri}");

            try
            {
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
        public async Task Auth(string passwd, string kid)
        {
            this.kid = kid;
            var aRes = await SendAReq(new AReq { Passwd = passwd });
            if (aRes.Status == ResStatus.Ok)
            {
                log.LogInformation($"Авторизация пройдена");
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
        public  async Task Stop()
        {
            await managerWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);

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
        private  async Task<JObject> SendManagerCommand(Message msg)
        {
            var data = await mEncoder.Encode(msg, kid);
            var tcs = RegisterSimpleCommandResultHandler(msg.CorrelationId,  5);
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

            Task CloseHandler()
            {
                return Task.CompletedTask;
            }
        }       

        public  async Task SendData(Guid tunnelId, Guid socketId, byte[] data)
        {
            if (dataTunnelInfos.TryGetValue(tunnelId, out var tunnelInfo))
            {
                await using var ms = new MemoryStream();
                await using var bw = new BinaryWriter(ms);
                bw.Write(socketId.ToByteArray());
                bw.Write(data.Length);
                bw.Write(data);
                bw.Flush();

                var encodedData = await dEncoder.Encrypt(tunnelInfo.Key, tunnelInfo.BlockSize, ms.ToArray());

                await SendData(tunnelInfo, encodedData);
            }
        }

        private async Task SendData(DataTunnelInfo tunnelInfo, byte[] data)
        {
            if (tunnelInfo.Socket.State != WebSocketState.Open)
            {
                RemoveConnection(tunnelInfo);
            }
            try
            {
                await tunnelInfo.Socket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true, CancellationToken.None);
            }
            catch (Exception)
            {
                RemoveConnection(tunnelInfo);
            }
        }

        private void RemoveConnection(DataTunnelInfo tunnelInfo)
        {

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
