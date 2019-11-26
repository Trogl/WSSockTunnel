using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
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
        private DataEncoder dEncoder = new DataEncoder();
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

        public async Task OpenDataTunnel(Func<Task<(Guid, byte[])>> dataReceiver)
        {
            var res = await SendDataTunnelRequest();
            if (res.Status != ResStatus.Ok)
            { 
                log.LogError($"Ошибка открытия дата тунеля: {res.Error.Code}");
                return;
            }



            var ws  = new ClientWebSocket();
            var wsUri = new Uri(serverUri, res.DTUri);
            log.LogInformation($"Подключение к {wsUri}");


            try
            {
                await ws.ConnectAsync(wsUri, CancellationToken.None);
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"{ex.Message}");
                return;
            }



            RunDataReceiver(ws, res.DTUri, dataReceiver);
            log.LogInformation($"Дата тунель открыт");


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
            bool isClose = false;
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

        private  void RunDataReceiver(ClientWebSocket socket, DataTunnelInfo dInfo, Func<Task<(Guid, byte[])>> dataReceiver)
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

            async Task MessageHandler(byte[] encodedData)
            {
                var data = await dEncoder.Decode(dInfo.Key, dInfo.BlockSize, encodedData);

                await using var ms = new MemoryStream(data);

                using var br = new BinaryReader(ms);
                
                    
                



            }

            Task CloseHandler()
            {
                return Task.CompletedTask;
            }
        }



        public  async Task SendData(Guid socketId, byte[] data)
        {

        }

        public void Dispose()
        {
            managerWebSocket?.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).Wait();
            managerWebSocket?.Dispose();
        }
    }


}
