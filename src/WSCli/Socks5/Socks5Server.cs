using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Prism.Events;
using WSCli.Logging;

namespace WSCli.TCP
{
    public class SocketInfo
    {
        public Guid SocketId;
        public Socket Socket;
        public bool HelloCompleted;
        public bool ConnectCompleted;
        public Task<bool> WaitingConnectCompleted;

    }

    internal class Socks5Server : IDisposable
    {
        private readonly ILogger log = AppLogging.CreateLogger<Socks5Server>();
        private ConcurrentDictionary<Guid, SocketInfo> socketsInfo = new ConcurrentDictionary<Guid, SocketInfo>();
        private TcpListener tcpListener;

        private Func<Guid, string, short, Task<IPAddress>> requireNewConnection;
        private Func<Guid, byte[], int, Task<bool>> transferData;
        private Func<Guid, Task> closeSocket;



        public void Start(int port, Func<Guid, string, short, Task<IPAddress>> requireNewConnection,
            Func<Guid, byte[], int, Task<bool>> transferData,
            Func<Guid, Task> closeSocket
            )
        {
            this.requireNewConnection = requireNewConnection;
            this.transferData = transferData;
            this.closeSocket = closeSocket;

            Task.Run(async () =>
            {
                log.LogInformation($"Запускаем Socks5  сервер на порту {port}");
                var localEndPoint = new IPEndPoint(IPAddress.Loopback, port);
                tcpListener = new TcpListener(localEndPoint);
                tcpListener.Start();
                while (true)
                {
                    var socket = await tcpListener.AcceptSocketAsync();
                    AcceptClient(socket);
                }
            });
        }

        private void AcceptClient(Socket socket)
        {



            Task.Run(async () =>
            {
                var socketInfo = new SocketInfo
                {
                    SocketId = Guid.NewGuid(),
                    Socket = socket,
                    WaitingConnectCompleted = Task.FromResult(false),
                    HelloCompleted = false,
                    ConnectCompleted = false,

                };

                log.LogTrace($"{socketInfo.SocketId} - Новое соединение ");

                if (socketsInfo.TryAdd(socketInfo.SocketId, socketInfo))
                {
                    await ProcessingSocketConnection(socketInfo);
                    await RemoveSocket(socketInfo.SocketId);
                }
                else
                {
                    socket.Close();
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

                        if (!socketInfo.ConnectCompleted)
                        {
                            if (buffer[0] == 5)
                            {
                                if (!socketInfo.HelloCompleted)
                                {
                                    if (await Handle5Hello(socketInfo, buffer, bytes))
                                    {
                                        log.LogTrace($"{socketInfo.SocketId} - S5 Hello Done ");
                                    }
                                    else
                                    {
                                        log.LogTrace($"{socketInfo.SocketId} - каето фигня а не Hello 5 ");
                                        break;
                                    }

                                    continue;
                                }

                                if (!socketInfo.ConnectCompleted)
                                {
                                    if (await Handle5Connect(socketInfo, buffer, bytes))
                                    {
                                        log.LogTrace($"{socketInfo.SocketId} - S5 Connect Done ");
                                    }
                                    else
                                    {
                                        log.LogTrace($"{socketInfo.SocketId} - каето фигня а не Connect 5 ");
                                        break;
                                    }

                                    continue;
                                }
                            }

                            if (buffer[0] == 4)
                            {
                                if (await Handle4Connect(socketInfo, buffer, bytes))
                                {
                                    log.LogTrace($"{socketInfo.SocketId} - S4 Connect Done ");
                                }
                                else
                                {
                                    log.LogTrace($"{socketInfo.SocketId} - каето фигня а не Connect 4 ");
                                    break;
                                }

                                continue;
                            }
                        }


                        await HandleData(socketInfo, buffer, bytes);
                    }
                    else
                    {
                        zeroBytesCount++;
                    }

                    if (zeroBytesCount == 100)
                        break;
                }
                catch (SocketException ex)
                {

                }
                catch (Exception ex)
                {
                    log.LogError($"[{socketInfo.SocketId}]: Приполучении данных произошла ошибка: {ex.Message} ");
                    break;
                }
            }
        }



        public async Task RemoveSocket(Guid socketId)
        {
            if (socketsInfo.TryRemove(socketId, out var si))
            {
                log.LogTrace($"{socketId} - Закрываем сокет");
                if (si.Socket.Connected)
                    si.Socket.Close();

                await closeSocket(socketId);
            }
        }


        private async Task<bool> Handle5Hello(SocketInfo socketInfo, byte[] buffer, int resiveBytes)
        {
            if (resiveBytes <= 2 && resiveBytes > 257)
                return false;

            await using var ms = new MemoryStream(buffer);
            using var br = new BinaryReader(ms);

            var socksVersion = br.ReadByte();
            if (socksVersion != 5)
                return false;

            var authCount = br.ReadByte();

            if (resiveBytes != authCount + 2)
                return false;

            var helloRes = new byte[2];
            helloRes[0] = 5;
            helloRes[1] = 0xFF;

            for (var i = 0; i < authCount; i++)
            {
                var authMethod = br.ReadByte();
                if (authMethod == 0x00)
                {
                    helloRes[1] = 0x00;
                    break;
                }
            }

            await socketInfo.Socket.SendAsync(new ArraySegment<byte>(helloRes), SocketFlags.None);

            socketInfo.HelloCompleted = true;
            return true;

        }

        private async Task<bool> Handle5Connect(SocketInfo socketInfo, byte[] buffer, int resiveBytes)
        {
            if (resiveBytes <= 7)
                return false;

            await using var ms = new MemoryStream(buffer);
            using var br = new BinaryReader(ms);

            var socksVersion = br.ReadByte();
            if (socksVersion != 5)
                return false;

            var commandId = br.ReadByte();
            var reserve = br.ReadByte();
            if (reserve != 0)
                return false;

            var addressType = br.ReadByte();

            byte[] addressB = null;

            if (addressType == 0x01) //ipv4
            {
                addressB = br.ReadBytes(4);
            }
            else if (addressType == 0x03) //domain
            {
                var addressLen = br.ReadByte();
                addressB = br.ReadBytes(addressLen);
            }
            else if (addressType == 0x04) //ipv6
            {
                addressB = br.ReadBytes(16);
            }
            else
            {
                addressB = br.ReadBytes(resiveBytes - 6);
            }

            var portB = br.ReadBytes(2);
            var port = (short)(portB[1] + (portB[0] << 8));


            var tcs = new TaskCompletionSource<bool>();
            socketInfo.WaitingConnectCompleted = tcs.Task;


            byte result = 0x07;
            IPAddress connetedIp = null;
            if (commandId == 1)
            {
                if (addressType == 0x01 || addressType == 0x03)
                {
                    var strAddr = "";
                    if (addressType == 0x01)
                    {
                        strAddr = new IPAddress(addressB).ToString();
                    }

                    if (addressType == 0x03)
                    {
                        strAddr = Encoding.ASCII.GetString(addressB);
                    }

                    connetedIp = await requireNewConnection(socketInfo.SocketId, strAddr, port);
                    result = connetedIp != null ? (byte)0x00 : (byte)0x05;
                }
                else
                {
                    result = 0x08;
                }
            }

            await using var outMs = new MemoryStream();
            await using var bw = new BinaryWriter(outMs);

            bw.Write((byte)0x05);
            bw.Write(result);
            bw.Write((byte)0x00);

            if (connetedIp != null)
            {
                bw.Write((byte)0x01);
                bw.Write(connetedIp.GetAddressBytes());
            }
            else
            {
                bw.Write(addressType);
                if (addressType == 0x03)
                    bw.Write((byte)addressB.Length);
                bw.Write(addressB);
            }

            bw.Write(portB);

            if (result == 0x00)
                socketInfo.ConnectCompleted = true;

            await socketInfo.Socket.SendAsync(new ArraySegment<byte>(outMs.ToArray()), SocketFlags.None);

            tcs.SetResult(result == 0x00);

            return socketInfo.ConnectCompleted;
        }

        private async Task<bool> Handle4Connect(SocketInfo socketInfo, byte[] buffer, int resiveBytes)
        {
            if (resiveBytes < 8)
                return false;

            await using var ms = new MemoryStream(buffer);
            using var br = new BinaryReader(ms);

            var socksVersion = br.ReadByte();
            if (socksVersion != 4)
                return false;

            var commandId = br.ReadByte();
            var portB = br.ReadBytes(2);
            var port = (short)(portB[1] + (portB[0] << 8));
            var addressB = br.ReadBytes(4);

            var tcs = new TaskCompletionSource<bool>();
            socketInfo.WaitingConnectCompleted = tcs.Task;


            byte result = 0x5b;
            IPAddress connetedIp = null;
            if (commandId == 1)
            {
                var strAddr = new IPAddress(addressB).ToString();
                
                connetedIp = await requireNewConnection(socketInfo.SocketId, strAddr, port);
                result = connetedIp != null ? (byte)0x5a : (byte)0x5b;

            }

            await using var outMs = new MemoryStream();
            await using var bw = new BinaryWriter(outMs);

            bw.Write((byte)0x04);
            bw.Write(result);
            bw.Write(portB);

            if (connetedIp != null)
            {
                bw.Write(connetedIp.GetAddressBytes());
            }
            else
            {
                bw.Write(addressB);
            }

            if (result == 0x5a)
                socketInfo.ConnectCompleted = true;

            await socketInfo.Socket.SendAsync(new ArraySegment<byte>(outMs.ToArray()), SocketFlags.None);

            tcs.SetResult(result == 0x5a);

            return socketInfo.ConnectCompleted;
        }



        private Task HandleData(SocketInfo socketInfo, byte[] buffer, int bytes)
        {
            return transferData(socketInfo.SocketId, buffer, bytes);
        }

        public async Task SendData(Guid socketId, byte[] buffer)
        {
            if (socketsInfo.TryGetValue(socketId, out var socketInfo))
            {
                if (await socketInfo.WaitingConnectCompleted)
                    await socketInfo.Socket.SendAsync(buffer, SocketFlags.None);
            }
        }


        public void Stop()
        {
            tcpListener.Stop();
        }


        public void Dispose()
        {
            Stop();
        }
    }
}
