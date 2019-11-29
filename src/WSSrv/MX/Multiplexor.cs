using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Jose;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using WS.Common;
using WSdto;
using WSdto.Json;
using WSSrv.RsaKeys;

namespace WSSrv.MX
{

    public class UserInfo
    {
        public string Kid;
        public string UserId;
        public string Passwd;
        public bool IsEnable;
    }

    public enum ConnectionType
    {
        Unknown = 0,
        Managing = 1,
        Data = 2
    }


    public class ConnectionInfo
    {
        public Guid connectionId;
        public ConnectionType type;
        public WebSocket socket;

    }

    public class ManagingConnectionInfo : ConnectionInfo
    {
        public CancellationTokenSource notAuthTimeOut;
        public UserInfo UserInfo;

        public ConcurrentDictionary<Guid, Guid> dataConnections = new ConcurrentDictionary<Guid, Guid>();
        public ConcurrentDictionary<Guid, Guid> dataScokets = new ConcurrentDictionary<Guid, Guid>();
    }

    public class DataConnectionInfo : ConnectionInfo
    {
        public Aes aes;
        public int bufferSize;
        public Guid mainConnectionId;
    }

    public class ReqDataConnection
    {
        public Guid reqCID;
        public CancellationTokenSource notConnectionTimeOut;
        public Aes aes;
        public int blockSize;
        public Guid mainConnectionId;
    }

    public class SocketInfo
    {
        public Guid mainConnectionId;
        public Guid SocketId;
        public Socket Socket;
    }


    public class Multiplexor
    {
        private ILogger log;
        private ConcurrentDictionary<Guid, ManagingConnectionInfo> mConnections = new ConcurrentDictionary<Guid, ManagingConnectionInfo>();
        private ConcurrentDictionary<Guid, DataConnectionInfo> dConnections = new ConcurrentDictionary<Guid, DataConnectionInfo>();
        private ConcurrentDictionary<Guid, ReqDataConnection> reqDConnections = new ConcurrentDictionary<Guid, ReqDataConnection>();
        private ConcurrentDictionary<Guid, SocketInfo> socketInfos = new ConcurrentDictionary<Guid, SocketInfo>();



        private MessageEncoder mEncoder;
        private DataEncryptor dEncoder;
        private DataCache dataCache;


        public Multiplexor(ILoggerProvider loggerProvider, MessageEncoder mEncoder, DataCache dataCache, DataEncryptor dEncoder)
        {
            this.mEncoder = mEncoder;
            this.dataCache = dataCache;
            this.dEncoder = dEncoder;
            log = loggerProvider.CreateLogger("Multiplexor");
        }


        public async Task RegisterManagingConnection(WebSocket socket, Guid connectionId)
        {
            var connectionInfo = new ManagingConnectionInfo
            {
                socket = socket,
                connectionId = connectionId,
                type = ConnectionType.Managing,
                notAuthTimeOut = new CancellationTokenSource()
            };

            if (!mConnections.TryAdd(connectionId, connectionInfo))
            {
                return;
            }

            connectionInfo.notAuthTimeOut.Token.Register(async () =>
            {
                if (mConnections.TryRemove(connectionId, out var ci))
                {
                    if (ci.socket.State == WebSocketState.Open)
                        await ci.socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                }
            });

            connectionInfo.notAuthTimeOut.CancelAfter(TimeSpan.FromSeconds(5));

            await ProcessingManagingConnection(connectionInfo);

            connectionInfo.notAuthTimeOut.Dispose();

            if (mConnections.TryRemove(connectionId, out var ci))
            {
                if (ci.socket.State == WebSocketState.Open)
                    await ci.socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            }

        }

        private async Task ProcessingManagingConnection(ManagingConnectionInfo connectionInfo)
        {
            var buffer = new byte[1024 * 16];
            while (connectionInfo.socket.State == WebSocketState.Open)
            {
                var ms = new MemoryStream();
                try
                {
                    WebSocketReceiveResult received;
                    do
                    {
                        received = await connectionInfo.socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                        await ms.WriteAsync(buffer, 0, received.Count);

                    } while (!received.EndOfMessage);

                    if (received.MessageType == WebSocketMessageType.Close)
                    {
                        if (connectionInfo.socket.State == WebSocketState.CloseReceived)
                            await connectionInfo.socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);

                    }
                    else
                    {
                        await HandleManagingMessage(ms.ToArray(), connectionInfo);
                    }
                }
                catch (Exception ex)
                {
                    log.LogError($"[{connectionInfo.connectionId}]: При получении данных произошла ошибка: {ex.Message} ");
                }
            }

        }

        private async Task HandleManagingMessage(byte[] bytes, ManagingConnectionInfo connectionInfo)
        {
            try
            {
                (Message Msg, string UserId) res = await mEncoder.Decode(bytes);


                switch (res.Msg.Type)
                {
                    case "AReq":
                        await Auth(connectionInfo, res.Msg, res.UserId);
                        break;

                    case "DTAReq":
                        await NewDataTunnel(connectionInfo, res.Msg);
                        break;

                    case "ConReq":
                        await NewConnection(connectionInfo, res.Msg);
                        break;

                    case "DisconEvnt":
                        await Disconnect(connectionInfo, res.Msg);
                        break;

                }
            }
            catch (Exception ex)
            {
                log.LogError($"[{connectionInfo.connectionId}] Error Handle mMessage - {ex.Message}");
            }
        }

        public async Task RegisterDataConnection(WebSocket socket, Guid cid, string path)
        {

            if (!Guid.TryParse(path.TrimStart('/'), out var reqCID))
                return;

            if (!reqDConnections.TryRemove(reqCID, out var reqConnInfo))
                return;

            if (!mConnections.TryGetValue(reqConnInfo.mainConnectionId, out var mainConnection))
                return;

            var dConn = new DataConnectionInfo
            {
                type = ConnectionType.Data,
                connectionId = cid,
                socket = socket,
                aes = reqConnInfo.aes,
                bufferSize = reqConnInfo.blockSize,
                mainConnectionId = mainConnection.connectionId
            };

            if (!dConnections.TryAdd(cid, dConn))
                return;

            mainConnection.dataConnections.TryAdd(cid, cid);

            await ProcessingDataConnection(dConn);

            mainConnection.dataConnections.TryRemove(cid, out _);
        }

        private async Task ProcessingDataConnection(DataConnectionInfo connectionInfo)
        {
            var buffer = new byte[16 * 1024];
            while (connectionInfo.socket.State == WebSocketState.Open)
            {
                var ms = new MemoryStream();
                try
                {
                    WebSocketReceiveResult received;
                    do
                    {
                        received = await connectionInfo.socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                        await ms.WriteAsync(buffer, 0, received.Count);

                    } while (!received.EndOfMessage);

                    if (received.MessageType == WebSocketMessageType.Close)
                    {
                        if (connectionInfo.socket.State == WebSocketState.CloseReceived)
                            await connectionInfo.socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);

                    }
                    else
                    {
                        await HandleDataMessage(ms.ToArray(), connectionInfo);
                    }
                }
                catch (Exception ex)
                {
                    log.LogError($"[{connectionInfo.connectionId}]: Приполучении данных произошла ошибка: {ex.Message} ");
                }
            }

        }

        private async Task HandleDataMessage(byte[] encryptedData, DataConnectionInfo connectionInfo)
        {
            var data = await dEncoder.Decrypt(connectionInfo.aes, encryptedData);

            var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms);

            var socketId = new Guid(br.ReadBytes(16));
            var payloadSize = br.ReadInt32();
            var payload = br.ReadBytes(payloadSize);


            if (socketId == Guid.Empty)
            {
                await EchoProcessing(connectionInfo, payload);
            }
            else
            {
                await SocketProcessing(socketId, payload);
            }
        }

        private async Task Auth(ManagingConnectionInfo connectionInfo, Message msg, string userId)
        {
            var sw = new Stopwatch();
            sw.Start();

            var aReq = msg.Payload.ConvertValue<AReq>();

            if (connectionInfo.UserInfo != null)
                return;

            var userInfo = await dataCache.GetUserInfo(userId);

            connectionInfo.UserInfo = userInfo;

            var aRes = new ARes
            {
                Error = AuthUser(userInfo, aReq)
            };

            if (aRes.Error == null)
            {
                aRes.Status = ResStatus.Ok;
                connectionInfo.notAuthTimeOut.Dispose();
            }
            else
                aRes.Status = ResStatus.Error;

            sw.Stop();

            var result = new Message
            {
                CorrelationId = msg.CorrelationId,
                Type = "ARes",
                Payload = aRes,
                TimeStamp = DateTime.UtcNow,
                ExecutionDuration = sw.Elapsed
            };

            await SendMessage(connectionInfo, result);


        }

        private Error AuthUser(UserInfo userInfo, AReq aReq)
        {
            if (!userInfo.IsEnable)
            {
                return new Error
                {
                    Code = "UserBlocked",
                };
            }
            else
            {
                if (aReq.Passwd != userInfo.Passwd)
                {
                    return new Error
                    {
                        Code = "InvalidPasswd",
                    };
                }
            }

            return null;
        }

        private async Task NewDataTunnel(ManagingConnectionInfo connectionInfo, Message msg)
        {
            var sw = new Stopwatch();
            sw.Start();


            var dtaRes = new DTARes();

            var result = new Message
            {
                CorrelationId = msg.CorrelationId,
                Type = "DTARes",
                Payload = dtaRes
            };


            if (connectionInfo.UserInfo == null)
            {
                dtaRes.Status = ResStatus.Error;
                dtaRes.Error = new Error
                {
                    Code = "InsufficientPermission",
                };

                sw.Stop();
                result.ExecutionDuration = sw.Elapsed;
                result.TimeStamp = DateTime.UtcNow;


                await SendMessage(connectionInfo, result);
                return;
            }


            var aes = Aes.Create();
            aes.GenerateIV();
            aes.GenerateKey();

            var dataConnection = new ReqDataConnection
            {
                reqCID = Guid.NewGuid(),
                notConnectionTimeOut = new CancellationTokenSource(),
                aes = aes,
                blockSize = 0,
                mainConnectionId = connectionInfo.connectionId
            };


            if (!reqDConnections.TryAdd(dataConnection.reqCID, dataConnection))
            {
                log.LogError("Ошибка добавления нового соединения");
                dtaRes.Status = ResStatus.Error;
                dtaRes.Error = new Error
                {
                    Code = "InternalError",
                };

                sw.Stop();
                result.ExecutionDuration = sw.Elapsed;
                result.TimeStamp = DateTime.UtcNow;

                await SendMessage(connectionInfo, result);
                return;

            }

            dtaRes.DTKey = dataConnection.aes.Key;
            dtaRes.DTIV = dataConnection.aes.IV;
            dtaRes.DTBS = dataConnection.blockSize;
            dtaRes.DTUri = dataConnection.reqCID.ToString("N");

            dataConnection.notConnectionTimeOut.Token.Register(() =>
                {
                    reqDConnections.TryRemove(dataConnection.reqCID, out var rdc);
                });


            dtaRes.Status = ResStatus.Ok;
            dtaRes.Error = null;

            sw.Stop();
            result.ExecutionDuration = sw.Elapsed;
            result.TimeStamp = DateTime.UtcNow;

            await SendMessage(connectionInfo, result);

            dataConnection.notConnectionTimeOut.CancelAfter(TimeSpan.FromSeconds(5));

        }

        private async Task NewConnection(ManagingConnectionInfo connectionInfo, Message msg)
        {
            var sw = new Stopwatch();
            sw.Start();

            var conReq = msg.Payload.ConvertValue<ConReq>();
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            try
            {

                var ip = Dns.GetHostAddresses(conReq.Addr)
                    .First(p => p.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                

                await socket.ConnectAsync(ip, conReq.Port);
                var socketInfo = new SocketInfo
                {
                    Socket = socket,
                    mainConnectionId = connectionInfo.connectionId,
                    SocketId = conReq.SocketId
                };
                if (socketInfos.TryAdd(conReq.SocketId, socketInfo))
                {
                    log.LogTrace($"Новый сокет {conReq.SocketId} -> {ip}:{conReq.Port}");
                    RunProcessingSocketConnection(socketInfo);
                }
                else
                {
                    throw new Exception("Ошибка добавления соедининия");
                }

                sw.Stop();

                await SendMessage(connectionInfo, new Message
                {
                    CorrelationId = msg.CorrelationId,
                    ExecutionDuration = sw.Elapsed,
                    TimeStamp = DateTime.UtcNow,
                    Type = "ConRes",
                    Payload = new ConRes
                    {
                        SocketId = conReq.SocketId,
                        Ip = ip.GetAddressBytes(),
                        Status = ResStatus.Ok
                    }
                });
            }
            catch (Exception ex)
            {
                await SendMessage(connectionInfo, new Message
                {
                    CorrelationId = msg.CorrelationId,
                    ExecutionDuration = sw.Elapsed,
                    TimeStamp = DateTime.UtcNow,
                    Type = "ConRes",
                    Payload = new ConRes
                    {
                        SocketId = conReq.SocketId,
                        Status = ResStatus.Error,
                        Error = new Error
                        {
                            Code = "ConnectionFailed",
                            Message = ex.Message
                        }
                    }
                });
            }

            void RunProcessingSocketConnection(SocketInfo si)
            {
                connectionInfo.dataScokets.TryAdd(si.SocketId, si.SocketId);
                Task.Run(async () =>
                {
                    await ProcessingSocketConnection(si);
                    await SocketDisconnect(si.SocketId, true);
                });
            }

        }

        private Task Disconnect(ManagingConnectionInfo connection, Message msg)
        {
            var dEvent = msg.Payload.ConvertValue<DisconEvnt>();
            return SocketDisconnect(dEvent.SocketId, false);
        }

        private async Task SocketDisconnect(Guid socketId, bool sendEvent)
        {
            if (socketInfos.TryRemove(socketId, out var socketInfo))
            {
                log.LogTrace($"Закрываес сокет {socketId}");
                if (socketInfo.Socket.Connected)
                {
                    try
                    {
                        socketInfo.Socket.Close();
                    }
                    catch (Exception)
                    {
                    }
                }
                if (mConnections.TryGetValue(socketInfo.mainConnectionId, out var managingConnection))
                {
                    if (managingConnection.dataScokets.TryRemove(socketId, out var _))
                    {
                        if (sendEvent)
                            await SendDisconectEvent(managingConnection, socketId);
                    }
                }
            }
        }

        private async Task<bool> SendMessage(ManagingConnectionInfo connection, Message msg)
        {
            var data = await mEncoder.Encode(msg, connection.UserInfo.Kid);
            return await SendData(connection, data);
        }

        private async Task<bool> SendData(DataConnectionInfo connectionInfo, Guid socketId, byte[] data, int size)
        {
            await using var ms = new MemoryStream();
            await using var bw = new BinaryWriter(ms);
            bw.Write(socketId.ToByteArray());
            bw.Write(size);
            bw.Write(data, 0, size);
            bw.Flush();

            var encodedData = await dEncoder.Encrypt(connectionInfo.aes, connectionInfo.bufferSize, ms.ToArray());

            return await SendData(connectionInfo, encodedData);
        }

        private async Task<bool> SendData(ConnectionInfo connection, byte[] data)
        {
            if (connection.socket.State != WebSocketState.Open)
            {
                RemoveConnection(connection);
                return false;
            }
            try
            {
                await connection.socket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true, CancellationToken.None);
                return true;
            }
            catch (Exception)
            {
                RemoveConnection(connection);
                return false;
            }


        }

        private void RemoveConnection(ConnectionInfo connection)
        {
            //todo
        }

        private async Task EchoProcessing(DataConnectionInfo connectionInfo, byte[] payload)
        {
            var str = Encoding.UTF8.GetString(payload);
            var echoReq = JsonConvert.DeserializeObject<EchoReq>(str, JsonSettings.settings);
            var echoRes = new EchoRes
            {
                ReqId = echoReq.ReqId,
                ReqTimestamp = echoReq.Timestamp,
                ResTimestamp = DateTime.UtcNow
            };

            var buffer = Encoding.UTF8.GetBytes(echoRes.ToJson());
            await SendData(connectionInfo, Guid.Empty, buffer, buffer.Length);
        }

        private async Task SocketProcessing(Guid socketId, byte[] payload)
        {
            if (socketInfos.TryGetValue(socketId, out var socketInfo))
            {
                if (!socketInfo.Socket.Connected)
                {
                    await SocketDisconnect(socketId, true);
                }

                try
                {
                    await socketInfo.Socket.SendAsync(payload, SocketFlags.None);
                }
                catch (SocketException e)
                {
                    await SocketDisconnect(socketId, true);
                }

            }
        }

        private Task SendDisconectEvent(ManagingConnectionInfo managingConnection, Guid socketId)
        {
            return SendMessage(managingConnection, new Message
            {
                CorrelationId = Guid.NewGuid().ToString("N"),
                Type = "DisconEvnt",
                TimeStamp = DateTime.UtcNow,
                Payload = new DisconEvnt
                {
                    SocketId = socketId
                }
            });
        }


        private async Task ProcessingSocketConnection(SocketInfo socketInfo)
        {
            var buffer = new byte[16 * 1024];
            var zeroBytesCount = 0;
            while (socketInfo.Socket.Connected)
            {
                try
                {
                    var bytes = await socketInfo.Socket.ReceiveAsync(new ArraySegment<byte>(buffer), SocketFlags.None);

                    if (bytes != 0)
                    {
                        zeroBytesCount = 0;
                        if (mConnections.TryGetValue(socketInfo.mainConnectionId, out var mC))
                        {
                            var dc = mC.dataConnections.FirstOrDefault();

                            if (dConnections.TryGetValue(dc.Key, out var dataConnection))
                            {
                                await SendData(dataConnection, socketInfo.SocketId, buffer, bytes);
                            }
                        }
                        else
                        {
                            zeroBytesCount++;
                        }

                        if (zeroBytesCount == 100)
                            break;
                    }
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode != SocketError.OperationAborted)
                    {
                        log.LogError($"[{socketInfo.SocketId}]: Приполучении данных  из сокета произошла ошибка: {ex.Message} ");
                    }
                    break;
                }
                catch (Exception ex)
                {
                    log.LogError($"[{socketInfo.SocketId}]: Приполучении данных  из сокета произошла ошибка: {ex.Message} ");
                    break;
                }
            }

        }
    }
}
