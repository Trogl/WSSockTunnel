using System;
using Microsoft.Extensions.Logging;

namespace WSCli.Logging
{
    internal class LogEntityLoggerProvider : ILoggerProvider
    {
        private readonly Action<LogEntity> addAction;

        public LogEntityLoggerProvider(Action<LogEntity> addAction)
        {
            this.addAction = addAction;
        }

        public ILogger CreateLogger(string loggerName)
        {
            return new LELogger(loggerName, addAction);
        }

        public void Dispose()
        {
        }

        internal class LELogger : ILogger
        {
            private readonly string loggerName;
            private readonly Action<LogEntity> addAction;

            public LELogger(string loggerName, Action<LogEntity> addAction)
            {
                this.loggerName = loggerName;
                this.addAction = addAction;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {

                var lEntity = new LogEntity
                {
                    Level = logLevel.ToString(),
                    Message = formatter(state, exception),
                    Logger = loggerName,
                    TimeStamp = DateTime.Now
                };

                addAction(lEntity);
            }

            public IDisposable BeginScope<TState>(TState state)
            {
                return new NoopDisposable();
            }

            private class NoopDisposable : IDisposable
            {
                public void Dispose()
                {
                }
            }
        }
    }
}