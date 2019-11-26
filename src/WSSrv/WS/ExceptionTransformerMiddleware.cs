using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using WSdto.Json;


namespace WSSrv
{
    public class ExceptionTransformerMiddleware
    {
        private readonly RequestDelegate next;

        public ExceptionTransformerMiddleware(RequestDelegate next)
        {
            this.next = next;
        }

        public async Task Invoke(HttpContext context)
        {

            Stream orgResponseStream = null;
            if (!context.Response.Body.CanSeek)
            {
                orgResponseStream = context.Response.Body;
                context.Response.Body = new MemoryStream();
            }
            try
            {
                await next.Invoke(context);
            }
            catch (Exception ex)
            {
                await WriteExceptionAsync(context, ex);
            }

            if (orgResponseStream != null)
            {
                context.Response.Body.Position = 0;
                await context.Response.Body.CopyToAsync(orgResponseStream);
                context.Response.Body = orgResponseStream;
            }
        }


        private async Task WriteExceptionAsync(HttpContext context, Exception ex)
        {
            context.Response.ContentType = "application/json";

            using (var tw = new StreamWriter(context.Response.Body, Encoding.UTF8, 65536, true))
            {
                var dto = new
                {
                    Message = ex.Message
                };


                await tw.WriteAsync(JsonConvert.SerializeObject(dto, Formatting.Indented, JsonSettings.settings));

            }
            context.Response.StatusCode = 500;

        }


    }






}