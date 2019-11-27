using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using WSSrv.MX;

namespace WSSrv.WS
{
    public class WebSocketManagerMiddleware
    {
        private readonly Multiplexor multiplexor;
        private ILogger log;
        public WebSocketManagerMiddleware(RequestDelegate next, ILoggerProvider loggerProvider, Multiplexor multiplexor)
        {
            this.multiplexor = multiplexor;
            log = loggerProvider.CreateLogger("WSSrv");
        }
        public async Task Invoke(HttpContext context)
        {
            var path = context.Request.Path;

            var connectionId = Guid.NewGuid();


            if (string.IsNullOrWhiteSpace(path))
            {
                using var socket = await context.WebSockets.AcceptWebSocketAsync();

                log.LogInformation($"[{connectionId}] - Установили управляющее соединение");

                await multiplexor.RegisterManagingConnection(socket, connectionId);

                if (socket.State == WebSocketState.Open)
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);

            }
            else
            {
                using var socket = await context.WebSockets.AcceptWebSocketAsync();
                log.LogInformation($"[{connectionId}] - Установили data соединение");

                await multiplexor.RegisterDataConnection(socket, connectionId, path);

                if (socket.State == WebSocketState.Open)
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            }
            log.LogInformation($"[{connectionId}] - Закрыли соединение");

        }

    }

    public class ConnectionData
    {
        public string UserId { get; set; }

    }





}
