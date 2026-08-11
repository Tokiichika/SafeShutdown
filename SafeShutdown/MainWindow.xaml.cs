using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SafeShutdown;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const int MaximumVisibleLogLines = 500;
    private static readonly Brush GreenBrush = CreateBrush("#16A34A");
    private static readonly Brush GreenBackground = CreateBrush("#DCFCE7");
    private static readonly Brush AmberBrush = CreateBrush("#D97706");
    private static readonly Brush AmberBackground = CreateBrush("#FEF3C7");
    private static readonly Brush RedBrush = CreateBrush("#DC2626");
    private static readonly Brush RedBackground = CreateBrush("#FEE2E2");
    private static readonly Brush GrayBrush = CreateBrush("#64748B");
    private static readonly Brush GrayBackground = CreateBrush("#E2E8F0");

    private readonly ServerMonitor _monitor = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly Queue<string> _visibleLogLines = new();
    private AppConfig _config = AppConfig.CreateDefault();
    private CancellationTokenSource? _shutdownCancellation;
    private CancellationTokenSource? _sshTestCancellation;
    private bool _isShutdownInProgress;
    private bool _isTestingSsh;
    private bool _isDirty;
    private int _consecutiveFailures;
    private DateTime? _lastCheck;
    private string _powerStatus = "未检测";
    private string _powerStatusDetail = "等待监控启动";
    private Brush _powerStatusBrush = GrayBrush;
    private Brush _powerStatusBackground = GrayBackground;
    private string _footerStatus = "就绪";

    public MainWindow()
    {
        InitializeComponent();

        Config = ConfigDataHelper.LoadConfig();
        DataContext = this;

        _monitor.PowerStateChanged += Monitor_PowerStateChanged;
        _monitor.ServerStatusChanged += Monitor_ServerStatusChanged;
        _monitor.OutageDetected += Monitor_OutageDetected;
        LogHelper.EntryWritten += LogHelper_EntryWritten;

        AttachConfigTracking(Config);
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;

        UpdateAllStateProperties();
        LogHelper.Info($"程序初始化完成，已加载 {Config.Servers.Count} 台主机。");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppConfig Config
    {
        get => _config;
        private set
        {
            _config = value;
            OnPropertyChanged();
        }
    }

    public bool IsBusy => _isShutdownInProgress || _isTestingSsh;
    public string MonitoringStatus => _isShutdownInProgress ? "正在执行关机流程" : _monitor.IsRunning ? "监控运行中" : "监控已停止";
    public string MonitorActionText => _monitor.IsRunning ? "停止监控" : "开始监控";
    public Brush MonitoringIndicatorBrush => _isShutdownInProgress ? AmberBrush : _monitor.IsRunning ? GreenBrush : GrayBrush;
    public string PowerStatus { get => _powerStatus; private set => SetField(ref _powerStatus, value); }
    public string PowerStatusDetail { get => _powerStatusDetail; private set => SetField(ref _powerStatusDetail, value); }
    public Brush PowerStatusBrush { get => _powerStatusBrush; private set => SetField(ref _powerStatusBrush, value); }
    public Brush PowerStatusBackground { get => _powerStatusBackground; private set => SetField(ref _powerStatusBackground, value); }
    public string ServerSummary => $"{Config.Servers.Count(server => server.Enabled)} 台";
    public string OnlineSummary
    {
        get
        {
            var enabled = Config.Servers.Where(server => server.Enabled).ToArray();
            var checkedCount = enabled.Count(server => server.IsOnline.HasValue);
            return checkedCount == 0 ? "尚未检测在线状态" : $"在线 {enabled.Count(server => server.IsOnline == true)} / 已检测 {checkedCount}";
        }
    }

    public string FailureSummary => $"{_consecutiveFailures} / {Config.FailureThreshold}";
    public string LastCheckText => _lastCheck is null ? "尚未检测" : $"上次检测 {_lastCheck:HH:mm:ss}";
    public string FooterStatus { get => _footerStatus; private set => SetField(ref _footerStatus, value); }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (!Config.AutoStartMonitoring)
        {
            return;
        }

        try
        {
            if (Config.AutoStartDelaySeconds > 0)
            {
                FooterStatus = $"将在 {Config.AutoStartDelaySeconds} 秒后自动开始监控";
                await Task.Delay(TimeSpan.FromSeconds(Config.AutoStartDelaySeconds), _lifetimeCancellation.Token);
            }

            if (!_monitor.IsRunning && !_isShutdownInProgress)
            {
                await StartMonitoringAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // 窗口关闭或用户已经执行其他操作。
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_isShutdownInProgress)
        {
            var result = MessageBox.Show(
                "关机流程仍在运行。关闭程序会取消尚未执行的命令，确定退出吗？",
                "SafeShutdown",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        if (_isDirty)
        {
            var saveResult = MessageBox.Show(
                "配置有未保存的更改，退出前保存吗？",
                "SafeShutdown",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (saveResult == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            if (saveResult == MessageBoxResult.Yes &&
                (!ValidateConfiguration(requireServers: false) || !SaveConfiguration(showConfirmation: false)))
            {
                e.Cancel = true;
                return;
            }
        }

        _lifetimeCancellation.Cancel();
        _shutdownCancellation?.Cancel();
        _sshTestCancellation?.Cancel();
        LogHelper.EntryWritten -= LogHelper_EntryWritten;
        _ = _monitor.StopAsync();
    }

    private async void MonitorButton_Click(object sender, RoutedEventArgs e)
    {
        if (_monitor.IsRunning)
        {
            await StopMonitoringAsync();
        }
        else
        {
            await StartMonitoringAsync();
        }
    }

    private async Task StartMonitoringAsync()
    {
        if (!ValidateConfiguration(requireServers: false))
        {
            return;
        }

        try
        {
            SaveConfiguration(showConfirmation: false);
            _monitor.Start(Config, Config.Servers);
            FooterStatus = $"正在监控 {Config.MonIP}";
            UpdateAllStateProperties();
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            LogHelper.Error("监控启动失败。", ex);
            MessageBox.Show($"无法启动监控：{ex.Message}", "SafeShutdown", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task StopMonitoringAsync()
    {
        await _monitor.StopAsync();
        FooterStatus = "监控已停止";
        PowerStatusDetail = "保留上次检测结果";
        UpdateAllStateProperties();
    }

    private void Monitor_PowerStateChanged(PowerStateChangedEventArgs args)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _consecutiveFailures = args.ConsecutiveFailures;
            _lastCheck = args.CheckedAt;

            switch (args.State)
            {
                case PowerState.Online:
                    PowerStatus = "供电正常";
                    PowerStatusDetail = $"{Config.MonIP} 可达";
                    PowerStatusBrush = GreenBrush;
                    PowerStatusBackground = GreenBackground;
                    break;
                case PowerState.SuspectedOutage:
                    PowerStatus = "疑似断电";
                    PowerStatusDetail = $"正在复核（{args.ConsecutiveFailures}/{args.FailureThreshold}）";
                    PowerStatusBrush = AmberBrush;
                    PowerStatusBackground = AmberBackground;
                    break;
                case PowerState.Offline:
                    PowerStatus = "确认断电";
                    PowerStatusDetail = "已触发安全关机流程";
                    PowerStatusBrush = RedBrush;
                    PowerStatusBackground = RedBackground;
                    break;
                default:
                    SetPowerUnknown();
                    break;
            }

            OnPropertyChanged(nameof(FailureSummary));
            OnPropertyChanged(nameof(LastCheckText));
        });
    }

    private void Monitor_ServerStatusChanged(ServerInfo server, bool online)
    {
        Dispatcher.BeginInvoke(() =>
        {
            server.IsOnline = online;
            UpdateServerSummary();
        });
    }

    private void Monitor_OutageDetected()
    {
        Dispatcher.BeginInvoke(() => _ = BeginShutdownSequenceAsync(triggeredAutomatically: true));
    }

    private async void ManualShutdownButton_Click(object sender, RoutedEventArgs e)
    {
        await BeginShutdownSequenceAsync(triggeredAutomatically: false);
    }

    private async Task BeginShutdownSequenceAsync(bool triggeredAutomatically)
    {
        if (_isShutdownInProgress || !ValidateConfiguration(requireServers: true, requireMonitorTarget: triggeredAutomatically))
        {
            return;
        }

        var enabledCount = Config.Servers.Count(server => server.Enabled);
        if (!triggeredAutomatically)
        {
            var localAction = Config.ShutdownLocalComputer
                ? $"随后等待 {Config.LocalShutdownDelaySeconds} 秒并关闭本机。"
                : "本机将保持运行。";
            var result = MessageBox.Show(
                $"将按列表顺序向 {enabledCount} 台主机发送关机命令。{Environment.NewLine}{localAction}{Environment.NewLine}{Environment.NewLine}确定继续吗？",
                "确认执行安全关机",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        _isShutdownInProgress = true;
        _shutdownCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        UpdateAllStateProperties();

        try
        {
            SaveConfiguration(showConfirmation: false);
            await _monitor.StopAsync();
            LogHelper.Warn(triggeredAutomatically ? "开始执行自动安全关机流程。" : "开始执行手动安全关机流程。");

            if (triggeredAutomatically && Config.OutageGracePeriodSeconds > 0)
            {
                FooterStatus = $"断电缓冲期：{Config.OutageGracePeriodSeconds} 秒（可取消）";
                LogHelper.Warn($"进入 {Config.OutageGracePeriodSeconds} 秒断电缓冲期；若供电恢复，将自动终止关机。");
                await Task.Delay(TimeSpan.FromSeconds(Config.OutageGracePeriodSeconds), _shutdownCancellation.Token);

                var powerRecovered = await ServerMonitor.PingAsync(
                    Config.MonIP,
                    Config.PingTimeoutMilliseconds,
                    _shutdownCancellation.Token);
                if (powerRecovered)
                {
                    LogHelper.Info("缓冲期结束时监控目标已恢复，自动关机流程已终止并恢复监控。");
                    FooterStatus = "供电已恢复，关机流程自动终止";
                    _monitor.Start(Config, Config.Servers);
                    return;
                }
            }

            await cmdHelper.ShutdownAllServersAsync(
                Config.Servers.ToArray(),
                Config,
                (server, online) => Dispatcher.BeginInvoke(() =>
                {
                    server.IsOnline = online;
                    UpdateServerSummary();
                }),
                _shutdownCancellation.Token);

            FooterStatus = Config.ShutdownLocalComputer ? "已请求关闭本机" : "关机流程已完成";
        }
        catch (OperationCanceledException)
        {
            LogHelper.Warn("关机流程已由用户取消，尚未执行的步骤不会继续。");
            FooterStatus = "关机流程已取消";
        }
        catch (Exception ex)
        {
            LogHelper.Error("关机流程发生未处理错误，已停止后续步骤。", ex);
            FooterStatus = "关机流程异常停止";
            MessageBox.Show($"关机流程异常停止：{ex.Message}", "SafeShutdown", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _shutdownCancellation?.Dispose();
            _shutdownCancellation = null;
            _isShutdownInProgress = false;
            UpdateAllStateProperties();
        }
    }

    private void CancelShutdownButton_Click(object sender, RoutedEventArgs e)
    {
        _shutdownCancellation?.Cancel();
        FooterStatus = "正在取消关机流程……";
    }

    private async void TestSshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isTestingSsh || !ValidateConfiguration(requireServers: true, requireMonitorTarget: false))
        {
            return;
        }

        _isTestingSsh = true;
        _sshTestCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        FooterStatus = "正在测试 SSH 连接……";
        UpdateAllStateProperties();

        try
        {
            await cmdHelper.TestAllAsync(Config.Servers, _sshTestCancellation.Token);
            FooterStatus = "SSH 测试完成，请查看运行日志";
        }
        catch (OperationCanceledException)
        {
            FooterStatus = "SSH 测试已取消";
        }
        finally
        {
            _sshTestCancellation.Dispose();
            _sshTestCancellation = null;
            _isTestingSsh = false;
            UpdateAllStateProperties();
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateConfiguration(requireServers: false))
        {
            return;
        }

        var restartMonitoring = _monitor.IsRunning;
        if (restartMonitoring)
        {
            await _monitor.StopAsync();
        }

        SaveConfiguration(showConfirmation: true);
        if (restartMonitoring)
        {
            _monitor.Start(Config, Config.Servers);
        }

        UpdateAllStateProperties();
    }

    private bool SaveConfiguration(bool showConfirmation)
    {
        try
        {
            CommitGridEdits();
            ConfigDataHelper.SaveConfigToJson(Config);
            _isDirty = false;
            FooterStatus = $"配置已保存：{ConfigDataHelper.ConfigPath}";
            LogHelper.Info("配置已保存并重新载入到运行状态。");

            if (showConfirmation)
            {
                MessageBox.Show("配置已保存。", "SafeShutdown", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return true;
        }
        catch (Exception ex)
        {
            LogHelper.Error("保存配置失败。", ex);
            MessageBox.Show($"保存配置失败：{ex.Message}", "SafeShutdown", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private bool ValidateConfiguration(bool requireServers, bool requireMonitorTarget = true)
    {
        CommitGridEdits();

        if (requireMonitorTarget && !IsValidHost(Config.MonIP))
        {
            MessageBox.Show("请输入有效的监控 IP 或主机名。", "配置检查", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var enabledServers = Config.Servers.Where(server => server.Enabled).ToArray();
        if (requireServers && enabledServers.Length == 0)
        {
            MessageBox.Show("至少需要启用一台远程主机。", "配置检查", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        foreach (var server in enabledServers)
        {
            if (!IsValidHost(server.IP) || string.IsNullOrWhiteSpace(server.Username) || string.IsNullOrWhiteSpace(server.Command))
            {
                MessageBox.Show($"主机“{server.IP}”的地址、用户名或关机命令不完整。", "配置检查", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (server.Port is < 1 or > 65535 || server.Delay is < 0 or > 86400)
            {
                MessageBox.Show($"主机“{server.IP}”的端口或延时超出允许范围。", "配置检查", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        if (Config.MonitorIntervalSeconds is < 1 or > 3600 ||
            Config.FailureThreshold is < 1 or > 100 ||
            Config.PingTimeoutMilliseconds is < 200 or > 30000 ||
            Config.OutageGracePeriodSeconds is < 0 or > 600 ||
            Config.LocalShutdownDelaySeconds is < 0 or > 86400)
        {
            MessageBox.Show("监控参数超出允许范围，请检查间隔、阈值、超时和本机关机延时。", "配置检查", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    private static bool IsValidHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        return IPAddress.TryParse(host, out _) || Uri.CheckHostName(host) != UriHostNameType.Unknown;
    }

    private void AddItem_Click(object sender, RoutedEventArgs e)
    {
        var server = new ServerInfo("10.0.0.10", "admin", string.Empty, "shutdown -h now", 0, false);
        Config.Servers.Add(server);
        ServerDataGrid.SelectedItem = server;
        ServerDataGrid.ScrollIntoView(server);
    }

    private void DeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (ServerDataGrid.SelectedItem is not ServerInfo selected)
        {
            MessageBox.Show("请先选择要删除的主机。", "SafeShutdown", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show($"确定删除主机“{selected.IP}”吗？", "删除主机", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            Config.Servers.Remove(selected);
        }
    }

    private void MoveUpItem_Click(object sender, RoutedEventArgs e) => MoveSelectedItem(-1);
    private void MoveDownItem_Click(object sender, RoutedEventArgs e) => MoveSelectedItem(1);

    private void MoveSelectedItem(int offset)
    {
        var oldIndex = ServerDataGrid.SelectedIndex;
        var newIndex = oldIndex + offset;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= Config.Servers.Count)
        {
            return;
        }

        Config.Servers.Move(oldIndex, newIndex);
        ServerDataGrid.SelectedIndex = newIndex;
    }

    private void PasswordBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox && passwordBox.DataContext is ServerInfo server && passwordBox.Password != server.Password)
        {
            passwordBox.Password = server.Password;
        }
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox && passwordBox.DataContext is ServerInfo server)
        {
            server.Password = passwordBox.Password;
        }
    }

    private void ClearLogsButton_Click(object sender, RoutedEventArgs e)
    {
        _visibleLogLines.Clear();
        LogTextBox.Clear();
    }

    private void OpenLogsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(LogHelper.LogDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{LogHelper.LogDirectory}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开日志目录：{ex.Message}", "SafeShutdown", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LogHelper_EntryWritten(AppLogEntry entry)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var marker = entry.Level switch
            {
                "Warn" => "WARN ",
                "Error" => "ERROR",
                _ => "INFO "
            };
            _visibleLogLines.Enqueue($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] [{marker}] {entry.Message}");
            while (_visibleLogLines.Count > MaximumVisibleLogLines)
            {
                _visibleLogLines.Dequeue();
            }

            LogTextBox.Text = string.Join(Environment.NewLine, _visibleLogLines);
            LogTextBox.ScrollToEnd();
        });
    }

    private void AttachConfigTracking(AppConfig config)
    {
        config.PropertyChanged += Config_PropertyChanged;
        config.Servers.CollectionChanged += Servers_CollectionChanged;
        foreach (var server in config.Servers)
        {
            server.PropertyChanged += Server_PropertyChanged;
        }
    }

    private void Config_PropertyChanged(object? sender, PropertyChangedEventArgs e) => MarkConfigurationDirty();

    private void Servers_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ServerInfo server in e.OldItems)
            {
                server.PropertyChanged -= Server_PropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (ServerInfo server in e.NewItems)
            {
                server.PropertyChanged += Server_PropertyChanged;
            }
        }

        MarkConfigurationDirty();
        UpdateServerSummary();
    }

    private void Server_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ServerInfo.IsOnline))
        {
            MarkConfigurationDirty();
        }

        if (e.PropertyName is nameof(ServerInfo.IsOnline) or nameof(ServerInfo.Enabled))
        {
            UpdateServerSummary();
        }
    }

    private void MarkConfigurationDirty()
    {
        _isDirty = true;
        FooterStatus = "配置有未保存的更改";
    }

    private void UpdateServerSummary()
    {
        OnPropertyChanged(nameof(ServerSummary));
        OnPropertyChanged(nameof(OnlineSummary));
    }

    private void UpdateAllStateProperties()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(MonitoringStatus));
        OnPropertyChanged(nameof(MonitorActionText));
        OnPropertyChanged(nameof(MonitoringIndicatorBrush));
        OnPropertyChanged(nameof(FailureSummary));
        OnPropertyChanged(nameof(LastCheckText));
        UpdateServerSummary();

        MonitorButton.IsEnabled = !IsBusy;
        ManualShutdownButton.IsEnabled = !IsBusy;
        CancelShutdownButton.Visibility = _isShutdownInProgress ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetPowerUnknown()
    {
        PowerStatus = "未检测";
        PowerStatusDetail = "等待监控启动";
        PowerStatusBrush = GrayBrush;
        PowerStatusBackground = GrayBackground;
    }

    private void CommitGridEdits()
    {
        ServerDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        ServerDataGrid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private static Brush CreateBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
