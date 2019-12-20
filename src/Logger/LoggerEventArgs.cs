using System;

namespace Afonsoft.Framework.Logger
{
    public class LoggerEventArgs : EventArgs
    {
        public LoggerEventArgs(LogLevel logLevel, string description, Exception exception)
        {
            LogLevel = logLevel;
            Description = description;
            Exception = exception;
        }
        public LoggerEventArgs(string description, Exception exception)
        {
            LogLevel = LogLevel.Error;
            Description = description;
            Exception = exception;
        }
        public LoggerEventArgs(LogLevel logLevel, string description)
        {
            LogLevel = logLevel;
            Description = description;
        }

        public LogLevel LogLevel { get; set; }
        public string Description { get; set; }
        public Exception Exception { get; set; }
    }
}
