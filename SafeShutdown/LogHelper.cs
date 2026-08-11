using NLog;
using NLog.Config;
using System.IO;

namespace SafeShutdown;

public sealed record AppLogEntry(DateTime Timestamp, string Level, string Message);

public static class LogHelper
{
    private static readonly Logger Logger;

    static LogHelper()
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SafeShutdown",
            "Logs");

        var escapedPath = Path.Combine(logDirectory, "${shortdate}.log").Replace("\\", "/");
        var configuration = $$"""
            <?xml version="1.0" encoding="utf-8" ?>
            <nlog xmlns="http://www.nlog-project.org/schemas/NLog.xsd">
              <targets>
                <target name="file" xsi:type="File"
                        xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                        fileName="{{escapedPath}}"
                        archiveEvery="Day"
                        maxArchiveFiles="30"
                        layout="[${longdate}][${level:uppercase=true}] ${message} ${exception:format=tostring}" />
                <target name="debug" xsi:type="Debugger"
                        xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                        layout="[${time}][${level:uppercase=true}] ${message}" />
              </targets>
              <rules>
                <logger name="*" minlevel="Info" writeTo="file,debug" />
              </rules>
            </nlog>
            """;

        LogManager.Configuration = XmlLoggingConfiguration.CreateFromXmlString(configuration);
        Logger = LogManager.GetCurrentClassLogger();
    }

    public static event Action<AppLogEntry>? EntryWritten;

    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SafeShutdown",
        "Logs");

    public static void Info(string message) => Write(LogLevel.Info, message, null);
    public static void Warn(string message) => Write(LogLevel.Warn, message, null);
    public static void Error(string message, Exception? exception = null) => Write(LogLevel.Error, message, exception);

    private static void Write(LogLevel level, string message, Exception? exception)
    {
        Logger.Log(level, exception, message);
        EntryWritten?.Invoke(new AppLogEntry(DateTime.Now, level.Name, message));
    }

    // 兼容旧版 LogHelper.WriteLog.Info(...) 调用形式。
    public static LegacyLogFacade WriteLog { get; } = new();

    public sealed class LegacyLogFacade
    {
        public void Info(string message) => LogHelper.Info(message);
        public void Info(string message, Exception error) => LogHelper.Write(LogLevel.Info, message, error);
        public void Warn(string message) => LogHelper.Warn(message);
        public void Warn(string message, Exception error) => LogHelper.Write(LogLevel.Warn, message, error);
        public void Error(string message) => LogHelper.Error(message);
        public void Error(string message, Exception error) => LogHelper.Error(message, error);
    }
}
