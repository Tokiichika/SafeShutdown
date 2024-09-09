using Newtonsoft.Json;
using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;

namespace SafeShutdown
{
    public class ConfigDataHelper
    {
        private static string filename = "Config.json";
        private static string configPath = Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config"), filename);
        /// <summary>
        /// 保存配置文件
        /// </summary>
        /// <param name="Servers">远程主机信息</param>
        /// <param name="MonIP">用于监控市电的设备ip</param>
        public static void SaveConfigToJson(ObservableCollection<ServerInfo> Servers,string MonIP)
        {
            var config = new AppConfig(Servers, MonIP);
            string jsonData = JsonConvert.SerializeObject(config, Formatting.Indented);
            if (!Directory.Exists(configPath.Replace(filename, string.Empty)))
            {
                Directory.CreateDirectory(configPath.Replace(filename, string.Empty));
            }
            File.WriteAllText(configPath, jsonData);
        }
        /// <summary>
        /// 读取配置文件
        /// </summary>
        public static (ObservableCollection<ServerInfo>, string) LoadConfigFromJson()
        {
            string MonIP = "10.0.0.0";
            ObservableCollection<ServerInfo> Servers = new ObservableCollection<ServerInfo>
            {
                new ServerInfo("10.0.0.1", "admin", "123456", "shutdown", 0, true),
                new ServerInfo("10.0.0.2", "admin", "123456", "shutdown", 60, true),
                new ServerInfo("10.0.0.3", "admin", "123456", "shutdown", 120, true)
            };
            if (File.Exists(configPath))
            {
                string jsonData = File.ReadAllText(configPath);
                var config = JsonConvert.DeserializeObject<AppConfig>(jsonData);
                if (config != null)
                {
                    Servers = config.Servers ?? Servers;
                    MonIP = config.MonIP ?? MonIP;
                    LogHelper.WriteLog.Info("配置文件已载入。");
                }
            }
            else
            {
                return (Servers, MonIP);
            }
            return (Servers, MonIP);
        }
    }

    public class AppConfig
    {
        public AppConfig(ObservableCollection<ServerInfo> servers, string monIP)
        {
            Servers = servers;
            MonIP = monIP;
        }
        public ObservableCollection<ServerInfo> Servers { get; set; }
        public string MonIP { get; set; }
    }

    public class ServerInfo
    {
        public string IP { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string Command { get; set; }
        public int Delay { get; set; }
        public bool IsOnline { get; set; }
        public ServerInfo(string iP, string username, string password, string command, int delay, bool isOnline)
        {
            IP = iP;
            Username = username;
            Password = password;
            Command = command;
            Delay = delay;
            IsOnline = isOnline;
        }
    }
}
