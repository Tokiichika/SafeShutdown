using NLog.Config;
using NLog;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SafeShutdown
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window , INotifyPropertyChanged
    {
        // 使用 ObservableCollection 作为数据源
        public ObservableCollection<ServerInfo> Servers { get; set; }
        public bool is_start { get; set; } = false;
        public static MainWindow mainwindow = null;
        public static MainWindow Instance()
        {
            if(mainwindow == null)
            {
                mainwindow = new MainWindow();
            }
            return mainwindow;
        }
        private string _MonIP;
        public string MonIP 
        {
            get { return this._MonIP; }
            set
            { 
                this._MonIP = value;
                OnPropertyChanged(nameof(MonIP));
            }
        }

        public MainWindow()
        {
            DataContext = this;
            InitializeComponent();
            mainwindow = this;
            #region nlog配置
            string NLogcfg = "<?xml version=\"1.0\" encoding=\"utf-8\" ?>\r\n<nlog xmlns=\"http://www.nlog-project.org/schemas/NLog.xsd\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">\r\n\t<targets>\r\n\t\t<!--屏幕打印消息-->\r\n\t\t<target name=\"console\" xsi:type=\"ColoredConsole\"\r\n\t\t\t\t\t\tlayout=\"${date:format=HH\\:mm\\:ss}> ${message}\"/>\r\n\r\n\t\t<!--vs输出窗口-->\r\n\t\t<target name=\"vsdebugger\" xsi:type=\"Debugger\"\r\n\t\t\t\t\t   layout=\"[${date:format=HH\\:mm\\:ss}][${level:padding=5:uppercase=true}] ${message}\"/>\r\n\t\t<!--保存info至文件-->\r\n\t\t<target name=\"info_file\" xsi:type=\"File\" maxArchiveFiles=\"30\"\r\n\t\t\t\t\t\tfileName=\"${basedir}/Logs/${shortdate}.log\"\r\n\t\t\t\t\t\tlayout=\"[${longdate}] ${message} | ${exception}\" />\r\n\t</targets>\r\n\t<rules>\r\n\t\t<logger name=\"*\" writeTo=\"console\" />\r\n\t\t<logger name=\"*\" minlevel=\"Trace\" writeTo=\"vsdebugger\"/>\r\n\t\t<logger name=\"*\" levels=\"Debug\" writeTo=\"debugger\" />\r\n\t\t<logger name=\"*\" levels=\"Info\" writeTo=\"info_file\" />\r\n\t</rules>\r\n</nlog>";
            #endregion
            LogManager.Configuration = XmlLoggingConfiguration.CreateFromXmlString(NLogcfg);
            InitServerData();
            logbox.Text = string.Empty;
            LogHelper.WriteLog.Info("程序初始化完成。");
            Thread tinit = new Thread(() => {
                LogHelper.WriteLog.Info("监控线程将在10秒后自动启动！");
                Thread.Sleep(10000);
                if(is_start == false)
                {
                    this.Dispatcher.Invoke(new Action(() => {
                        ServerMonitor.Start();
                    }));
                }  
            });
            tinit.IsBackground = true;
            tinit.Start();
        }

        public void change_online_sta(int index,bool sta)
        {
            this.Servers[index].IsOnline = sta;
        }

        public void WriteLogBox(string msg)
        {
            string logline =  DateTime.Now.ToString("[yyyy:MM:dd-HH:mm:ss]")+ " " + msg + "\n";
            logbox.Dispatcher.Invoke(
                new Action(
                    delegate
                    {
                        logbox.Text += logline;
                        logbox.ScrollToEnd();
                    }
                    )
                );
        }
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void InitServerData()
        {
            //这里读取配置文件，并返回一个ObservableCollection和string
            (ObservableCollection<ServerInfo>,string) data = ConfigDataHelper.LoadConfigFromJson();
            Servers = data.Item1;
            //将数据绑定到 ListBox
            ServerListBox.ItemsSource = Servers;
            MonIP = data.Item2;
        }
        private void AddItem_Click(object sender, RoutedEventArgs e)
        {
            Servers.Add(new ServerInfo("10.0.0.0", "admin", "123456", "shutdown", 120, true));
        }

        // 删除选中的服务器项
        private void DeleteItem_Click(object sender, RoutedEventArgs e)
        {
            var selectedServer = ServerListBox.SelectedItem as ServerInfo;
            if (selectedServer != null)
            {
                Servers.Remove(selectedServer);
            }
            else
            {
                MessageBox.Show("请先选择一个要删除的项目。");
            }
        }
        private void SwapItems(int index1, int index2)
        {
            if ((index1 >= 0 && index2 >= 0) && (index1 <= Servers.Count - 1 && index2 <= Servers.Count - 1))
            {
                var temp = Servers[index1];
                Servers[index1] = Servers[index2];
                Servers[index2] = temp;
            }
        }
        private void UP_Item_Click(object sender, RoutedEventArgs e)
        {
            var selectedIndex = ServerListBox.SelectedIndex;

            if (selectedIndex > 0) // 确保当前选中的元素不在第一个位置
            {
                // 交换元素位置
                SwapItems(selectedIndex, selectedIndex - 1);
                ServerListBox.SelectedIndex = selectedIndex - 1; // 更新选中项
            }
        }
        private void Down_Item_Click(object sender, RoutedEventArgs e)
        {
            var selectedIndex = ServerListBox.SelectedIndex;

            if (selectedIndex < Servers.Count - 1) // 确保当前选中的元素不在最后一个位置
            {
                // 交换元素位置
                SwapItems(selectedIndex, selectedIndex + 1);
                ServerListBox.SelectedIndex = selectedIndex + 1; // 更新选中项
            }
        }
        // 保存更改（实际绑定已处理）
        private void SaveChanges_Click(object sender, RoutedEventArgs e)
        {
            ConfigDataHelper.SaveConfigToJson(Servers, MonIP);
            // 由于使用了数据绑定，TextBox 的更改会自动反映到 Servers 集合中。
            MessageBox.Show("更改已保存。");
            //重启线程，如果线程启动了的话
            if(is_start)
            {
                ServerMonitor.ReStart();
            }
            LogHelper.WriteLog.Info("配置已重载");
        }
        
        private void sshTest_Click(object sender, RoutedEventArgs e)
        {
            cmdHelper.Ssh_All_test();
            LogHelper.WriteLog.Info("已测试用户名和密码的有效性，请查看日志信息。");
        }
        private void Start_Click(object sender, RoutedEventArgs e)
        {
            if (mon_start.Content is TextBlock textBlock)
            {
                if(!is_start)
                {
                    ServerMonitor.Start();
                }
                else
                {
                    ServerMonitor.Stop();
                }
            }
        }

        private void Manual_Shutdown_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult result = MessageBox.Show("确定要关闭所有主机吗？本机也将关闭！", "警告", MessageBoxButton.YesNo);
            if(result == MessageBoxResult.Yes)
            {
                LogHelper.WriteLog.Info("已手动执行关机步骤……");
                cmdHelper.Shutdown_all_server();
            }
        }
    }
}


