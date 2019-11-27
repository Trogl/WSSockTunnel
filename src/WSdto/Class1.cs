using System;
using System.Collections.Generic;

namespace WSdto
{
    public class Message
    {
        public string CorrelationId { get; set; }
        public string Type { get; set; }
        public object Payload { get; set; }
        public DateTime? TimeStamp { get; set; }
        public TimeSpan? ExecutionDuration { get; set; }
    }


    public enum ResStatus
    {
        Ok,
        Error
    }


    public class AReq
    {
        public string Passwd { get; set; }
    }

    public class ARes
    {
        public ResStatus Status { get; set; }
        public Error Error { get; set; }
    }

    public class ConReq
    {
        public Guid SocketId { get; set; } 
        public string Addr { get; set; }
        public int Port { get; set; }
    }

    public class ConRes
    {
        public Guid SocketId { get; set; }
        public ResStatus Status { get; set; }
        public Error Error { get; set; }
    }

    public class DisconEvnt
    {
        public Guid SocketId { get; set; }
    }


    public class DTAReq
    {
        
    }

    public class DTARes
    {
        public string DTUri { get; set; }
        public byte[] DTKey { get; set; }
        public byte[] DTIV { get; set; }
        public int DTBS{ get; set; }
        public ResStatus Status { get; set; }
        public Error Error { get; set; }

    }


    public class Error
    {
        public string Code { get; set; }
        public string Message { get; set; }
    }


    public class AuthResult
    {
        public string Kid { get; set; }
        public string UserId { get; set; }
        public string JweToken { get; set; }
        public IDictionary<string, object> JwsHeader { get; set; }
    }

    public class EchoReq
    {
        public Guid  ReqId { get; set; }
        public DateTime Timestamp { get; set; }

    }

    public class EchoRes
    {
        public Guid ReqId { get; set; }
        public DateTime ReqTimestamp { get; set; }
        public DateTime ResTimestamp { get; set; }

    }
}
