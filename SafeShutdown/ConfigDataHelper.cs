using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;

namespace SafeShutdown;

public static class ConfigDataHelper
{
    private const string FileName = "Config.json";
    private static readonly string ConfigDirectory = Path.Combine(AppContext.BaseDirectory, "config");
    public static string ConfigPath { get; } = Path.Combine(ConfigDirectory, FileName);

    public static void SaveConfigToJson(AppConfig config)
    {
        config.Normalize();
        Directory.CreateDirectory(ConfigDirectory);

        var json = JsonConvert.SerializeObject(config, Formatting.Indented);
        var temporaryPath = ConfigPath + ".tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, ConfigPath, true);
    }

    // 保留旧版调用方式，方便已有代码或二次开发继续使用。
    public static void SaveConfigToJson(ObservableCollection<ServerInfo> servers, string monIP)
    {
        SaveConfigToJson(new AppConfig(servers, monIP));
    }

    public static AppConfig LoadConfig()
    {
        if (!File.Exists(ConfigPath))
        {
            return AppConfig.CreateDefault();
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            var config = JsonConvert.DeserializeObject<AppConfig>(json) ?? AppConfig.CreateDefault();
            config.Normalize();
            return config;
        }
        catch (Exception ex)
        {
            var backupPath = ConfigPath + $".invalid-{DateTime.Now:yyyyMMdd-HHmmss}";
            try
            {
                File.Copy(ConfigPath, backupPath, true);
            }
            catch
            {
                // 备份失败不应阻止程序使用安全的默认配置启动。
            }

            LogHelper.Error("配置文件无法读取，已使用默认配置。原文件未被覆盖。", ex);
            return AppConfig.CreateDefault();
        }
    }

    public static (ObservableCollection<ServerInfo>, string) LoadConfigFromJson()
    {
        var config = LoadConfig();
        return (config.Servers, config.MonIP);
    }
}

public sealed class AppConfig : NotifyObject
{
    private string _monIP = "10.0.0.0";
    private int _monitorIntervalSeconds = 10;
    private int _failureThreshold = 5;
    private int _pingTimeoutMilliseconds = 1000;
    private int _localShutdownDelaySeconds = 60;
    private int _outageGracePeriodSeconds = 30;
    private int _autoStartDelaySeconds = 10;
    private bool _autoStartMonitoring = true;
    private bool _shutdownLocalComputer = true;

    public AppConfig()
    {
    }

    public AppConfig(ObservableCollection<ServerInfo> servers, string monIP)
    {
        Servers = servers;
        MonIP = monIP;
    }

    public ObservableCollection<ServerInfo> Servers { get; set; } = [];

    public string MonIP
    {
        get => _monIP;
        set => SetField(ref _monIP, value);
    }

    public int MonitorIntervalSeconds
    {
        get => _monitorIntervalSeconds;
        set => SetField(ref _monitorIntervalSeconds, value);
    }

    public int FailureThreshold
    {
        get => _failureThreshold;
        set => SetField(ref _failureThreshold, value);
    }

    public int PingTimeoutMilliseconds
    {
        get => _pingTimeoutMilliseconds;
        set => SetField(ref _pingTimeoutMilliseconds, value);
    }

    public int LocalShutdownDelaySeconds
    {
        get => _localShutdownDelaySeconds;
        set => SetField(ref _localShutdownDelaySeconds, value);
    }

    public int OutageGracePeriodSeconds
    {
        get => _outageGracePeriodSeconds;
        set => SetField(ref _outageGracePeriodSeconds, value);
    }

    public int AutoStartDelaySeconds
    {
        get => _autoStartDelaySeconds;
        set => SetField(ref _autoStartDelaySeconds, value);
    }

    public bool AutoStartMonitoring
    {
        get => _autoStartMonitoring;
        set => SetField(ref _autoStartMonitoring, value);
    }

    public bool ShutdownLocalComputer
    {
        get => _shutdownLocalComputer;
        set => SetField(ref _shutdownLocalComputer, value);
    }

    public void Normalize()
    {
        Servers ??= [];
        MonIP = MonIP?.Trim() ?? string.Empty;
        MonitorIntervalSeconds = Math.Clamp(MonitorIntervalSeconds, 1, 3600);
        FailureThreshold = Math.Clamp(FailureThreshold, 1, 100);
        PingTimeoutMilliseconds = Math.Clamp(PingTimeoutMilliseconds, 200, 30000);
        LocalShutdownDelaySeconds = Math.Clamp(LocalShutdownDelaySeconds, 0, 86400);
        OutageGracePeriodSeconds = Math.Clamp(OutageGracePeriodSeconds, 0, 600);
        AutoStartDelaySeconds = Math.Clamp(AutoStartDelaySeconds, 0, 3600);

        foreach (var server in Servers)
        {
            server.Normalize();
        }
    }

    public static AppConfig CreateDefault() => new()
    {
        AutoStartMonitoring = false,
        Servers =
        [
            new ServerInfo("10.0.0.1", "admin", string.Empty, "shutdown", 0, false) { IsOnline = null },
            new ServerInfo("10.0.0.2", "admin", string.Empty, "shutdown", 60, false) { IsOnline = null },
            new ServerInfo("10.0.0.3", "admin", string.Empty, "shutdown", 120, false) { IsOnline = null }
        ]
    };
}

public sealed class ServerInfo : NotifyObject
{
    private string _ip = string.Empty;
    private int _port = 22;
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _command = "shutdown";
    private int _delay;
    private bool _enabled = true;
    private bool _useSudo = true;
    private bool? _isOnline;

    public ServerInfo()
    {
    }

    public ServerInfo(string iP, string username, string password, string command, int delay, bool isOnline)
    {
        IP = iP;
        Username = username;
        Password = password;
        Command = command;
        Delay = delay;
        IsOnline = isOnline;
    }

    public string IP { get => _ip; set => SetField(ref _ip, value); }

    [DefaultValue(22)]
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
    public int Port { get => _port; set => SetField(ref _port, value); }

    public string Username { get => _username; set => SetField(ref _username, value); }
    [JsonIgnore]
    public string Password { get => _password; set => SetField(ref _password, value); }

    // JSON 字段名保持为 Password，以兼容旧配置；新保存的值使用当前 Windows 用户的 DPAPI 加密。
    [JsonProperty("Password")]
    private string StoredPassword
    {
        get => CredentialProtector.Protect(_password);
        set => _password = CredentialProtector.UnprotectOrMigrate(value);
    }
    public string Command { get => _command; set => SetField(ref _command, value); }
    public int Delay { get => _delay; set => SetField(ref _delay, value); }

    [DefaultValue(true)]
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
    public bool Enabled { get => _enabled; set => SetField(ref _enabled, value); }

    [DefaultValue(true)]
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
    public bool UseSudo { get => _useSudo; set => SetField(ref _useSudo, value); }

    [JsonIgnore]
    public bool? IsOnline { get => _isOnline; set => SetField(ref _isOnline, value); }

    public void Normalize()
    {
        IP = IP?.Trim() ?? string.Empty;
        Username = Username?.Trim() ?? string.Empty;
        Password ??= string.Empty;
        Command = Command?.Trim() ?? string.Empty;
        Port = Math.Clamp(Port, 1, 65535);
        Delay = Math.Clamp(Delay, 0, 86400);
    }
}

public abstract class NotifyObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal static class CredentialProtector
{
    private const string Prefix = "dpapi:";
    private const int CryptProtectUiForbidden = 0x1;

    public static string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(plainText);
        var input = CreateBlob(bytes);
        DataBlob output = default;
        try
        {
            if (!CryptProtectData(
                    ref input,
                    "SafeShutdown SSH credential",
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out output))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法保护 SSH 密码");
            }

            var encrypted = CopyBlob(output);
            return Prefix + Convert.ToBase64String(encrypted);
        }
        finally
        {
            ClearAndFreeInput(input, bytes.Length);
            if (output.Data != IntPtr.Zero)
            {
                LocalFree(output.Data);
            }
        }
    }

    public static string UnprotectOrMigrate(string? storedValue)
    {
        if (string.IsNullOrEmpty(storedValue))
        {
            return string.Empty;
        }

        if (!storedValue.StartsWith(Prefix, StringComparison.Ordinal))
        {
            // 旧版明文配置：保持可用，并在下次保存时自动迁移为 DPAPI 密文。
            return storedValue;
        }

        try
        {
            var encrypted = Convert.FromBase64String(storedValue[Prefix.Length..]);
            var input = CreateBlob(encrypted);
            DataBlob output = default;
            try
            {
                if (!CryptUnprotectData(
                        ref input,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        CryptProtectUiForbidden,
                        out output))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "无法解密 SSH 密码");
                }

                var plainBytes = CopyBlob(output);
                try
                {
                    return Encoding.UTF8.GetString(plainBytes);
                }
                finally
                {
                    Array.Clear(plainBytes);
                }
            }
            finally
            {
                ClearAndFreeInput(input, encrypted.Length);
                Array.Clear(encrypted);
                if (output.Data != IntPtr.Zero)
                {
                    LocalFree(output.Data);
                }
            }
        }
        catch (Exception ex)
        {
            LogHelper.Error("配置中的 SSH 密码无法由当前 Windows 用户解密，请重新输入该密码。", ex);
            return string.Empty;
        }
    }

    private static DataBlob CreateBlob(byte[] bytes)
    {
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return new DataBlob { Size = bytes.Length, Data = pointer };
    }

    private static byte[] CopyBlob(DataBlob blob)
    {
        var bytes = new byte[blob.Size];
        Marshal.Copy(blob.Data, bytes, 0, blob.Size);
        return bytes;
    }

    private static void ClearAndFreeInput(DataBlob blob, int length)
    {
        if (blob.Data == IntPtr.Zero)
        {
            return;
        }

        Marshal.Copy(new byte[length], 0, blob.Data, length);
        Marshal.FreeHGlobal(blob.Data);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
