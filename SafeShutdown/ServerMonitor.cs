using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Net.NetworkInformation;
using System.Printing;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SafeShutdown
{
    public class ServerMonitor
    {
        public static int failping = 0;
        public static bool DoPing(string ipAddress)
        {
            try
            {
                using (Ping ping = new Ping())
                {
                    PingReply reply = ping.Send(ipAddress, 1000); // 1000ms 超时
                    if (reply.Status == IPStatus.Success)
                    {
                        return true;
                    }
                    else
                    {
                        LogHelper.WriteLog.Warn($"无法ping通主机{ipAddress}，状态： {reply.Status}");
                        return false;
                    }
                }
            }
            catch (PingException ex)
            {
                // 处理 ping 异常（例如网络不可达）
                LogHelper.WriteLog.Error($"{ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                // 处理其他异常
                LogHelper.WriteLog.Error($"{ex.Message}");
                return false;
            }
        }

        public static bool is_Online(string ip)
        {
            int failping = 0;
            if (!DoPing(ip))
            {
                failping++;
                if (failping >= 3)
                {
                    return false;
                }
            }
            else
            {
                return true;
            }
            return false;
        }
        static List<Thread> ThreadList = new List<Thread>();
        /// <summary>
        /// 启动所有监控线程
        /// </summary>
        public static void Start()
        {
            if (MainWindow.Instance().is_start == true)
                return;
            MainWindow.Instance().is_start = true;
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                MainWindow.Instance().start_btn_text.Text = "停止监控";
            }));
            ThreadList.Clear();
            //监控市电状态
            Thread tpower = new Thread(() =>
            {
                LogHelper.WriteLog.Info("市电状态监控线程已启动！");
                while (MainWindow.Instance().is_start)
                {
                    if (!is_Online(MainWindow.Instance().MonIP))
                    {
                        failping++;
                        LogHelper.WriteLog.Warn($"ping监控IP {MainWindow.Instance().MonIP} 失败！重试次数:{failping}");
                        if (failping >= 5)
                        {
                            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                MainWindow.Instance().power_sta.Text = "停电";
                                MainWindow.Instance().power_sta.Foreground = new SolidColorBrush(Colors.Red);
                            }));
                            LogHelper.WriteLog.Warn("检测到市电断开，执行关机步骤……");
                            cmdHelper.Shutdown_all_server();
                            break;
                        }
                    }
                    else
                    {
                        failping = 0;
                        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            MainWindow.Instance().power_sta.Text = "有电";
                            MainWindow.Instance().power_sta.Foreground = new SolidColorBrush(Colors.Green);
                        }));
                    }
                    for (int i = 0; i < 600; i++)
                    {
                        Thread.Sleep(100);
                        if (!MainWindow.Instance().is_start)
                            break;
                    }
                }
                LogHelper.WriteLog.Info("市电状态监控线程已停止！");
            });
            tpower.IsBackground = true;
            ThreadList.Add(tpower);
            //主机在线状态监测线程
            Thread tserver = new Thread(() =>
            {
                LogHelper.WriteLog.Info("主机在线状态监控线程已启动！");
                while (MainWindow.Instance().is_start)
                {
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        int cnt = MainWindow.Instance().Servers.Count;
                        for (int i = 0; i < cnt ;i++)
                        {
                            MainWindow.Instance().change_online_sta(i, DoPing(MainWindow.Instance().Servers[i].IP));
                        }
                        MainWindow.Instance().ServerListBox.ItemsSource = null;
                        MainWindow.Instance().ServerListBox.ItemsSource = MainWindow.Instance().Servers;
                    }));
                    for(int i = 0;i<100;i++)
                    {
                        Thread.Sleep(100);
                        if (!MainWindow.Instance().is_start)
                            break;
                    } 
                }
                LogHelper.WriteLog.Info("主机在线状态监控线程已停止！");
            });
            tserver.IsBackground = true;
            ThreadList.Add(tserver);

            foreach (var thread in ThreadList)
            {
                if (thread != null)
                {
                    thread.Start();
                }
            }
        }
        /// <summary>
        /// 停止所有监控线程
        /// </summary>
        public static void Stop() 
        {
            MainWindow.Instance().is_start = false;
            if (ThreadList != null && ThreadList.Count > 0)
            {
                foreach (var thread in ThreadList)
                {
                    if (thread != null && thread.ThreadState == ThreadState.Running)
                    {
                        thread.Join();
                    }
                }
            }
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                MainWindow.Instance().start_btn_text.Text = "开始监控";
            }));
            LogHelper.WriteLog.Info("所有监控线程已停止！");
        }
        //重启所有监控线程
        public static void ReStart()
        {
            Stop();
            Start();
        }




    }
}
