using System.Net.NetworkInformation;

namespace SafeShutdown;

public enum PowerState
{
    Unknown,
    Online,
    SuspectedOutage,
    Offline
}

public sealed record PowerStateChangedEventArgs(
    PowerState State,
    int ConsecutiveFailures,
    int FailureThreshold,
    DateTime CheckedAt);

public sealed class ServerMonitor : IAsyncDisposable
{
    private readonly object _stateLock = new();
    private CancellationTokenSource? _cancellation;
    private Task? _monitorTask;

    public bool IsRunning { get; private set; }

    public event Action<PowerStateChangedEventArgs>? PowerStateChanged;
    public event Action<ServerInfo, bool>? ServerStatusChanged;
    public event Action? OutageDetected;

    public void Start(AppConfig config, IReadOnlyCollection<ServerInfo> servers)
    {
        lock (_stateLock)
        {
            if (IsRunning)
            {
                return;
            }

            if (_monitorTask?.IsCompleted == true)
            {
                _cancellation?.Dispose();
                _cancellation = null;
                _monitorTask = null;
            }

            config.Normalize();
            _cancellation = new CancellationTokenSource();
            IsRunning = true;
            _monitorTask = RunAsync(config, servers, _cancellation.Token);
        }

        LogHelper.Info("监控已启动。");
    }

    public async Task StopAsync()
    {
        Task? task;
        lock (_stateLock)
        {
            if (!IsRunning && _monitorTask is null)
            {
                return;
            }

            _cancellation?.Cancel();
            task = _monitorTask;
        }

        if (task is not null)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 正常停止。
            }
        }

        lock (_stateLock)
        {
            _cancellation?.Dispose();
            _cancellation = null;
            _monitorTask = null;
            IsRunning = false;
        }

        LogHelper.Info("监控已停止。");
    }

    public static async Task<bool> PingAsync(string host, int timeoutMilliseconds, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host.Trim(), timeoutMilliseconds)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            return reply.Status == IPStatus.Success;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is PingException or ArgumentException)
        {
            return false;
        }
    }

    private async Task RunAsync(AppConfig config, IReadOnlyCollection<ServerInfo> servers, CancellationToken token)
    {
        try
        {
            await Task.WhenAll(
                MonitorPowerAsync(config, token),
                MonitorServersAsync(config, servers, token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // 正常停止。
        }
        catch (Exception ex)
        {
            LogHelper.Error("监控任务意外退出。", ex);
        }
        finally
        {
            lock (_stateLock)
            {
                IsRunning = false;
            }
        }
    }

    private async Task MonitorPowerAsync(AppConfig config, CancellationToken token)
    {
        var consecutiveFailures = 0;

        while (!token.IsCancellationRequested)
        {
            var isOnline = await PingAsync(config.MonIP, config.PingTimeoutMilliseconds, token)
                .ConfigureAwait(false);

            if (isOnline)
            {
                if (consecutiveFailures > 0)
                {
                    LogHelper.Info($"监控目标 {config.MonIP} 已恢复可达。");
                }

                consecutiveFailures = 0;
                PowerStateChanged?.Invoke(new PowerStateChangedEventArgs(
                    PowerState.Online, 0, config.FailureThreshold, DateTime.Now));
            }
            else
            {
                consecutiveFailures++;
                var state = consecutiveFailures >= config.FailureThreshold
                    ? PowerState.Offline
                    : PowerState.SuspectedOutage;

                LogHelper.Warn($"监控目标 {config.MonIP} 无响应（{consecutiveFailures}/{config.FailureThreshold}）。");
                PowerStateChanged?.Invoke(new PowerStateChangedEventArgs(
                    state, consecutiveFailures, config.FailureThreshold, DateTime.Now));

                if (state == PowerState.Offline)
                {
                    LogHelper.Warn("已达到连续失败阈值，判定市电中断并启动安全关机流程。");
                    OutageDetected?.Invoke();
                    _cancellation?.Cancel();
                    return;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(config.MonitorIntervalSeconds), token)
                .ConfigureAwait(false);
        }
    }

    private async Task MonitorServersAsync(
        AppConfig config,
        IReadOnlyCollection<ServerInfo> servers,
        CancellationToken token)
    {
        var previousStates = new Dictionary<ServerInfo, bool>();

        while (!token.IsCancellationRequested)
        {
            var snapshot = servers.Where(server => server.Enabled).ToArray();
            var checks = snapshot.Select(async server =>
            {
                var online = await PingAsync(server.IP, config.PingTimeoutMilliseconds, token)
                    .ConfigureAwait(false);
                return (Server: server, Online: online);
            });

            var results = await Task.WhenAll(checks).ConfigureAwait(false);
            foreach (var result in results)
            {
                if (!previousStates.TryGetValue(result.Server, out var previous) || previous != result.Online)
                {
                    var label = result.Online ? "在线" : "离线";
                    LogHelper.Info($"主机 {result.Server.IP} 状态：{label}。");
                    previousStates[result.Server] = result.Online;
                }

                ServerStatusChanged?.Invoke(result.Server, result.Online);
            }

            await Task.Delay(TimeSpan.FromSeconds(config.MonitorIntervalSeconds), token)
                .ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}
