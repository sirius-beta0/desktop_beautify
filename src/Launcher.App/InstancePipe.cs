using System.IO;
using System.IO.Pipes;
using System.Text;

namespace Launcher.App;

internal static class InstancePipe
{
    public const string PipeName = "DesktopBeautify.Launcher.SingleInstance";
    public const string ToggleMessage = "toggle";
    public const string SettingsMessage = "settings";
    public const string AboutMessage = "about";

    /// <summary>
    /// 第二个实例调用：尝试发一条消息给首实例。失败（首实例没起来）返回 false。
    /// </summary>
    public static bool TrySend(string message, int timeoutMs = 500)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeoutMs);
            var bytes = Encoding.UTF8.GetBytes(message);
            client.Write(bytes, 0, bytes.Length);
            client.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

internal sealed class InstancePipeServer : IDisposable
{
    private readonly Action<string> _onMessage;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    public InstancePipeServer(Action<string> onMessage)
    {
        _onMessage = onMessage;
        _loop = Task.Run(ListenAsync);
    }

    private async Task ListenAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    InstancePipe.PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(_cts.Token);

                using var ms = new MemoryStream();
                await server.CopyToAsync(ms, _cts.Token);
                var msg = Encoding.UTF8.GetString(ms.ToArray()).TrimEnd('\0').Trim();
                _onMessage(msg);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // pipe 偶发异常，等会儿重试
                await Task.Delay(200, CancellationToken.None);
            }
            finally
            {
                server?.Dispose();
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts.Dispose();
    }
}