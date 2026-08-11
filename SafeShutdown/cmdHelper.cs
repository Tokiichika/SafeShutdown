using Renci.SshNet;
using Renci.SshNet.Common;
using System.Diagnostics;
using System.Text;

namespace SafeShutdown;

public sealed record SshOperationResult(bool Success, string Message);

public static class cmdHelper
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    public static async Task<SshOperationResult> TestConnectionAsync(
        ServerInfo server,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient(server);
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            client.Disconnect();
            return new SshOperationResult(true, "连接与身份验证成功");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new SshOperationResult(false, FriendlyError(ex));
        }
    }

    public static async Task<SshOperationResult> ExecuteSshCommandAsync(
        ServerInfo server,
        CancellationToken cancellationToken)
    {
        SshClient? client = null;
        var commandStarted = false;

        try
        {
            client = CreateClient(server);
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);

            var commandText = server.UseSudo
                ? $"sudo -S -p '' -- sh -c {QuoteForPosixShell(server.Command)}"
                : server.Command;

            using var command = client.CreateCommand(commandText);
            command.CommandTimeout = CommandTimeout;
            commandStarted = true;

            var execution = command.ExecuteAsync(cancellationToken);
            if (server.UseSudo)
            {
                await using var input = command.CreateInputStream();
                var passwordBytes = Encoding.UTF8.GetBytes(server.Password + "\n");
                await input.WriteAsync(passwordBytes, cancellationToken).ConfigureAwait(false);
                await input.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            await execution.ConfigureAwait(false);
            var output = CombineOutput(command.Result, command.Error);
            return command.ExitStatus == 0
                ? new SshOperationResult(true, string.IsNullOrWhiteSpace(output) ? "命令已发送" : output)
                : new SshOperationResult(false, $"退出码 {command.ExitStatus}：{output}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SshConnectionException ex) when (
            commandStarted &&
            ex.DisconnectReason == Renci.SshNet.Messages.Transport.DisconnectReason.ConnectionLost)
        {
            // shutdown/poweroff 常在回传退出码前主动断开 SSH。
            return new SshOperationResult(true, "命令发送后连接已断开（目标主机可能正在关机）");
        }
        catch (Exception ex)
        {
            return new SshOperationResult(false, FriendlyError(ex));
        }
        finally
        {
            if (client is not null)
            {
                if (client.IsConnected)
                {
                    client.Disconnect();
                }

                client.Dispose();
            }
        }
    }

    public static async Task TestAllAsync(
        IEnumerable<ServerInfo> servers,
        CancellationToken cancellationToken)
    {
        foreach (var server in servers.Where(item => item.Enabled))
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogHelper.Info($"正在测试 {server.IP}:{server.Port} 的 SSH 连接……");
            var result = await TestConnectionAsync(server, cancellationToken).ConfigureAwait(false);
            if (result.Success)
            {
                LogHelper.Info($"[{server.IP}] SSH {result.Message}。");
            }
            else
            {
                LogHelper.Warn($"[{server.IP}] SSH 测试失败：{result.Message}");
            }
        }
    }

    public static async Task ShutdownAllServersAsync(
        IEnumerable<ServerInfo> servers,
        AppConfig config,
        Action<ServerInfo, bool?> updateStatus,
        CancellationToken cancellationToken)
    {
        var enabledServers = servers.Where(item => item.Enabled).ToArray();

        foreach (var server in enabledServers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (server.Delay > 0)
            {
                LogHelper.Info($"主机 {server.IP} 将在 {server.Delay} 秒后执行关机命令。");
                await Task.Delay(TimeSpan.FromSeconds(server.Delay), cancellationToken).ConfigureAwait(false);
            }

            LogHelper.Warn($"正在向主机 {server.IP} 发送关机命令……");
            var result = await ExecuteSshCommandAsync(server, cancellationToken).ConfigureAwait(false);
            if (result.Success)
            {
                LogHelper.Info($"[{server.IP}] {result.Message}");
                updateStatus(server, false);
            }
            else
            {
                LogHelper.Error($"[{server.IP}] 关机命令失败：{result.Message}");
            }
        }

        if (!config.ShutdownLocalComputer)
        {
            LogHelper.Info("远程主机处理完成；配置为保留本机运行。");
            return;
        }

        if (config.LocalShutdownDelaySeconds > 0)
        {
            LogHelper.Warn($"远程主机处理完成，本机将在 {config.LocalShutdownDelaySeconds} 秒后关机。可在倒计时内取消。");
            await Task.Delay(TimeSpan.FromSeconds(config.LocalShutdownDelaySeconds), cancellationToken)
                .ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        LogHelper.Warn("正在关闭本机……");
        Process.Start(new ProcessStartInfo
        {
            FileName = "shutdown.exe",
            Arguments = "/s /f /t 0",
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    private static SshClient CreateClient(ServerInfo server)
    {
        var passwordAuthentication = new PasswordAuthenticationMethod(server.Username, server.Password);
        var keyboardAuthentication = new KeyboardInteractiveAuthenticationMethod(server.Username);
        keyboardAuthentication.AuthenticationPrompt += (_, args) =>
        {
            foreach (var prompt in args.Prompts)
            {
                prompt.Response = server.Password;
            }
        };

        var connectionInfo = new ConnectionInfo(
            server.IP,
            server.Port,
            server.Username,
            passwordAuthentication,
            keyboardAuthentication)
        {
            Timeout = ConnectionTimeout
        };

        return new SshClient(connectionInfo);
    }

    private static string QuoteForPosixShell(string value)
        => $"'{value.Replace("'", "'\\''")}'";

    private static string CombineOutput(string standardOutput, string standardError)
    {
        var combined = string.Join(
            Environment.NewLine,
            new[] { standardOutput, standardError }.Where(value => !string.IsNullOrWhiteSpace(value)))
            .Trim();

        const int maxLength = 1000;
        return combined.Length <= maxLength ? combined : combined[..maxLength] + "…";
    }

    private static string FriendlyError(Exception exception) => exception switch
    {
        SshAuthenticationException => "身份验证失败，请检查用户名和密码",
        SshOperationTimeoutException => "连接或命令执行超时",
        System.Net.Sockets.SocketException => "无法连接到目标主机",
        _ => exception.Message
    };
}
