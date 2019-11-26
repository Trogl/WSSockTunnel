using System;

namespace WSCli.Logging
{
    class LogEntity
    {
        public DateTime TimeStamp { get; set; }
        public string Logger { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
    }
}