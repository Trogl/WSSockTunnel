using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WSSrv.MX;
using WSSrv.RsaKeys;
using WSSrv.WS;

namespace WSSrv
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<Multiplexor>();
            services.AddSingleton<MessageEncoder>();
            services.AddSingleton<DataEncoder>();
            services.AddSingleton<DataCache>();

        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {

            app.UseMiddleware<ExceptionTransformerMiddleware>();
            app.Map("/ws", a =>
            {
                a.UseWebSockets();
                a.UseMiddleware<WebSocketManagerMiddleware>();
            });
        }
    }
}
