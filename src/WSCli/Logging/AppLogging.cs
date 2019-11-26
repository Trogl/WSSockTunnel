using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;


namespace WSCli.Logging
{
    internal static class AppLogging
    {
        static AppLogging()
        {
            LoggerFactory = new LoggerFactory();

        }

        public static ILoggerFactory LoggerFactory { get; }
        public static ILogger CreateLogger<T>() => LoggerFactory.CreateLogger<T>();
        public static ILogger CreateLogger(string name) => LoggerFactory.CreateLogger(name);

    }
}
