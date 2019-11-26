using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jose;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
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
        public List<Guid> DataConnections = new List<Guid>();
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

        public ConcurrentDictionary<Guid,Guid> dataConnections = new ConcurrentDictionary<Guid,Guid>();
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
        public int bufferSize;
        public Guid mainConnectionId;
    }


    public class Multiplexor
    {
        private ILogger log;
        private ConcurrentDictionary<Guid, ManagingConnectionInfo> mConnections = new ConcurrentDictionary<Guid, ManagingConnectionInfo>();
        private ConcurrentDictionary<Guid, DataConnectionInfo> dConnections = new ConcurrentDictionary<Guid, DataConnectionInfo>();
        private ConcurrentDictionary<Guid, ReqDataConnection> reqDConnections = new ConcurrentDictionary<Guid, ReqDataConnection>();




        private MessageEncoder mEncoder;
        private DataEncoder dEncoder;
        private DataCache dataCache;


        public Multiplexor(ILoggerProvider loggerProvider, MessageEncoder mEncoder, DataCache dataCache, DataEncoder dEncoder)
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

        public async Task RegisterDataConnection(WebSocket socket, Guid cid ,string path)
        {
            if (!Guid.TryParse(path, out var reqCID))
                return;

            if(!reqDConnections.TryRemove(reqCID, out var reqConnInfo))
                return;

            if(!mConnections.TryGetValue(reqConnInfo.mainConnectionId, out var mainConnection))
                return;

            var dConn = new DataConnectionInfo
            {
                connectionId = cid,
                socket = socket,
                aes = reqConnInfo.aes,
                bufferSize = reqConnInfo.bufferSize,
                mainConnectionId = mainConnection.connectionId
            };

            if(!dConnections.TryAdd(cid,dConn))
                return;

            mainConnection.dataConnections.TryAdd(cid, cid);

            await ProcessingDataConnection(dConn);

            mainConnection.dataConnections.TryRemove(cid, out _);
        }


        private async Task ProcessingDataConnection(DataConnectionInfo connectionInfo)
        {
            var buffer = new byte[connectionInfo.bufferSize];
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

        private async Task HandleDataMessage(byte[] data, DataConnectionInfo connectionInfo)
        {
            (Guid socketId, byte[] data) res = await dEncoder.Encode(connectionInfo.aes, data);

            if (res.socketId == Guid.Empty)
            {

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
                bufferSize = 8 * 1024,
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

            }

            dtaRes.DTKey = dataConnection.aes.Key;
            dtaRes.DTIV = dataConnection.aes.Key;
            dtaRes.DTBS = dataConnection.bufferSize;
            dtaRes.DTUri = dataConnection.reqCID.ToString("N");

            dataConnection.notConnectionTimeOut.Token.Register(() =>
                {
                    reqDConnections.TryRemove(dataConnection.reqCID, out var rdc);
                });


            dtaRes.Status = ResStatus.Ok;
            dtaRes.Error = new Error
            {
                Code = "InternalError",
            };

            sw.Stop();
            result.ExecutionDuration = sw.Elapsed;
            result.TimeStamp = DateTime.UtcNow;

            await SendMessage(connectionInfo, result);

            dataConnection.notConnectionTimeOut.CancelAfter(TimeSpan.FromSeconds(5));

        }

        private async Task NewConnection(ManagingConnectionInfo connectionInfo, Message msg)
        {
            throw new NotImplementedException();
        }

        private async Task Disconnect(ManagingConnectionInfo connection, Message msg)
        {
            throw new NotImplementedException();
        }

        private async Task<bool> SendMessage(ManagingConnectionInfo connection, Message msg)
        {
            var data = await mEncoder.Encode(msg, connection.UserInfo.Kid);
            return await SendData(connection, data);
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
            catch (Exception e)
            {
                RemoveConnection(connection);
                return false;
            }


        }

        private void RemoveConnection(ConnectionInfo connection)
        {
            //todo
        }
    }
}
