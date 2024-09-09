using Renci.SshNet;
using Renci.SshNet.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SafeShutdown
{
    public class cmdHelper
    {
        /// <summary>
        /// 当主机不接受密码验证时尝试交互式键盘验证
        /// </summary>
        /// <param name="host"></param>
        /// <param name="username"></param>
        /// <param name="password"></param>
        /// <returns></returns>
        public static bool SshConnectTest_Keyboard(string host, string username, string password)
        {
            bool result = false;
            var keyboardAuth = new KeyboardInteractiveAuthenticationMethod($"{username}");
            keyboardAuth.AuthenticationPrompt += (sender, e) =>
            {
                foreach (var prompt in e.Prompts)
                {
                    //处理交互式认证
                    prompt.Response = $"{password}";
                }
            };

            var connectionInfo = new ConnectionInfo($"{host}", $"{username}", keyboardAuth);

            using (var client = new SshClient(connectionInfo))
            {
                try
                {
                    client.Connect();
                    if (client.IsConnected)
                    {
                        result = true;
                        LogHelper.WriteLog.Info($"[{host}]交互式键盘验证成功！");
                    }
                }
                catch (Exception ex)
                {
                    result = false;
                    LogHelper.WriteLog.Error($"{ex.Message}");
                }
                finally
                {
                    //断开连接
                    if (client.IsConnected)
                    {
                        client.Disconnect();
                    }
                }
                return result;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="host"></param>
        /// <param name="username"></param>
        /// <param name="password"></param>
        /// <param name="command"></param>
        /// <returns></returns>
        public static string ExecuteSshCommand_Keyboard(string host, string username, string password, string command)
        {
            string result = string.Empty;
            var keyboardAuth = new KeyboardInteractiveAuthenticationMethod($"{username}");
            keyboardAuth.AuthenticationPrompt += (sender, e) =>
            {
                foreach (var prompt in e.Prompts)
                {
                    //处理交互式认证
                    prompt.Response = $"{password}";
                }
            };

            var connectionInfo = new ConnectionInfo($"{host}", $"{username}", keyboardAuth);

            using (var client = new SshClient(connectionInfo))
            {
                try
                {
                    client.Connect();
                    if (client.IsConnected)
                    {
                        LogHelper.WriteLog.Info($"[{host}]交互式键盘验证成功！");
                        // 执行命令
                        var cmd = client.CreateCommand($"echo {password} | sudo -S {command}");
                        result = cmd.Execute();
                        if (client.IsConnected)
                        {
                            cmd = client.CreateCommand($"{command}");
                            result = cmd.Execute();
                        }
                        LogHelper.WriteLog.Info($"[{host}]已执行命令[{command}],result:{result}");
                    }
                }
                catch (SshConnectionException ex)
                {
                    // 检查断开连接的原因
                    if (ex.DisconnectReason == Renci.SshNet.Messages.Transport.DisconnectReason.ConnectionLost)
                    {
                        result = "";
                        LogHelper.WriteLog.Info($"[{host}]已执行命令[{command}],result:{result}");
                    }
                    else
                    {
                        result = $"Error: {ex.Message}, Reason: {ex.DisconnectReason}";
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLog.Error($"{ex.Message}");
                }
                finally
                {
                    // 断开连接
                    if (client.IsConnected)
                    {
                        client.Disconnect();
                    }
                }
            }
            return result;
        }
        /// <summary>
        /// 测试所有主机的ssh用户名密码是否有效
        /// </summary>
        public static void Ssh_All_test()
        {
            foreach(var host in MainWindow.Instance().Servers)
            {
                if (!SshConnectTest(host.IP, host.Username, host.Password))
                {
                    LogHelper.WriteLog.Warn($"[ssh]主机{host.IP}无法连接，请检查用户名和密码！");
                }
            }
        }

        /// <summary>
        /// 测试ssh连接
        /// </summary>
        public static bool SshConnectTest(string host, string username, string password)
        {
            bool result = false;
            // 创建 SSH 客户端
            using (var client = new SshClient(host, username, password))
            {
                try
                {
                    // 连接到服务器
                    client.Connect();

                    if (client.IsConnected)
                    {
                        result = true;
                    }
                }
                catch (Exception ex)
                {
                    result = false;
                    LogHelper.WriteLog.Error($"{ex.Message}");
                }
                finally
                {
                    // 断开连接
                    if (client.IsConnected)
                    {
                        client.Disconnect();
                    }
                }
            }
            if (!result)
            {
                LogHelper.WriteLog.Warn($"主机{host}不接受密码验证,尝试交互式键盘验证……");
                result = SshConnectTest_Keyboard(host, username, password);
            }
            return result;
        }
        /// <summary>
        /// 执行ssh命令
        /// </summary>
        /// <param name="host"></param>
        /// <param name="username"></param>
        /// <param name="password"></param>
        /// <param name="command"></param>
        /// <returns></returns>
        public static string ExecuteSshCommand(string host, string username, string password, string command)
        {
            string result = string.Empty;

            // 创建 SSH 客户端
            using (var client = new SshClient(host, username, password))
            {
                try
                {
                    // 连接到服务器
                    client.Connect();

                    if (client.IsConnected)
                    {
                        // 执行命令
                        var cmd = client.CreateCommand($"echo {password} | sudo -S {command}");
                        result = cmd.Execute();
                        if(client.IsConnected)
                        {
                            cmd = client.CreateCommand($"{command}");
                            result = cmd.Execute();
                        }
                        LogHelper.WriteLog.Info($"[{host}]已执行命令[{command}],result:{result}");
                    }
                }
                catch (SshConnectionException ex)
                {
                    // 检查断开连接的原因
                    if (ex.DisconnectReason == Renci.SshNet.Messages.Transport.DisconnectReason.ConnectionLost)
                    {
                        result = "";
                        LogHelper.WriteLog.Info($"[{host}]已执行命令[{command}],result:{result}");
                    }
                    else
                    {
                        result = $"Error: {ex.Message}, Reason: {ex.DisconnectReason}";
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLog.Warn($"Error: {ex.Message}");
                    LogHelper.WriteLog.Warn($"主机{host}不接受密码验证,尝试交互式键盘验证……");
                    result = ExecuteSshCommand_Keyboard(host, username, password, command);
                }
                finally
                {
                    // 断开连接
                    if (client.IsConnected)
                    {
                        client.Disconnect();
                    }
                }
            }

            return result;
        }

        public static void Shutdown_all_server()
        {
            ServerMonitor.Stop();
            Thread tshutdown = new Thread(() => {
                int index = 0;
                foreach(var host in MainWindow.Instance().Servers)
                {
                    LogHelper.WriteLog.Info($"主机[{host.IP}]设置的延时为{host.Delay}秒，将于此时间后执行关机！");
                    Thread.Sleep(host.Delay*1000);//延时
                    LogHelper.WriteLog.Info($"正在关闭主机{host.IP}!");
                    string result = ExecuteSshCommand(host.IP,host.Username,host.Password,host.Command);
                    if(result != string.Empty)
                    {
                        LogHelper.WriteLog.Warn($"[{host.IP}]{result}");
                    }
                    MainWindow.Instance().Servers[index].IsOnline = false;
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        MainWindow.Instance().ServerListBox.ItemsSource = null;
                        MainWindow.Instance().ServerListBox.ItemsSource = MainWindow.Instance().Servers;
                    }));
                    index++;
                }
                LogHelper.WriteLog.Warn("所有远程主机关机完成，60秒后关闭本机！");
                Thread.Sleep(60000);
                LogHelper.WriteLog.Warn("正在关闭本机……");
                Process.Start("shutdown", "/s /f /t 0");
            });
            tshutdown.IsBackground = true;
            tshutdown.Start();
        }



    }
}
