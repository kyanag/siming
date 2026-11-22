using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Siming.Core.Control;

namespace Siming.App;

/// <summary>
/// 本机控制接口：只监听 127.0.0.1，令牌在 mcp.token。
/// 协议为「一行一个 JSON」：先鉴权，再发请求/收响应。
/// </summary>
public sealed class ControlServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly string _token;
    private readonly Func<ControlRequest, Task<object?>> _handler;
    private readonly CancellationTokenSource _cts = new();
    private bool _started;

    public ControlServer(int port, string token, Func<ControlRequest, Task<object?>> handler)
    {
        _listener = new TcpListener(IPAddress.Loopback, port);
        _token = token;
        _handler = handler;
    }

    public bool Start(out string? error)
    {
        try
        {
            _listener.Start();
            _started = true;
            _ = AcceptLoopAsync();
            error = null;
            return true;
        }
        catch (SocketException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (_started)
        {
            _listener.Stop();
            _started = false;
        }

        _cts.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                continue;
            }

            _ = HandleClientAsync(client);
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            client.NoDelay = true;
            using var stream = client.GetStream();
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: true);
            using var writer = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };

            try
            {
                var helloLine = await reader.ReadLineAsync(_cts.Token).ConfigureAwait(false);
                if (helloLine is null)
                {
                    return;
                }

                ControlHello? hello;
                try
                {
                    hello = JsonSerializer.Deserialize<ControlHello>(helloLine, ControlProtocol.JsonOptions);
                }
                catch (JsonException)
                {
                    await WriteAsync(writer, new ControlHelloResponse { Ok = false, Error = "invalid hello" })
                        .ConfigureAwait(false);
                    return;
                }

                if (!TokenMatches(hello?.Token))
                {
                    await WriteAsync(writer, new ControlHelloResponse { Ok = false, Error = "unauthorized" })
                        .ConfigureAwait(false);
                    return;
                }

                await WriteAsync(writer, new ControlHelloResponse { Ok = true }).ConfigureAwait(false);

                while (!_cts.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(_cts.Token).ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    ControlRequest? request;
                    try
                    {
                        request = JsonSerializer.Deserialize<ControlRequest>(line, ControlProtocol.JsonOptions);
                    }
                    catch (JsonException ex)
                    {
                        await WriteAsync(
                            writer,
                            new ControlResponse { Id = 0, Ok = false, Error = $"invalid json: {ex.Message}" })
                            .ConfigureAwait(false);
                        continue;
                    }

                    if (request is null)
                    {
                        continue;
                    }

                    try
                    {
                        var data = await _handler(request).ConfigureAwait(false);
                        await WriteAsync(
                            writer,
                            new ControlResponse { Id = request.Id, Ok = true, Data = data })
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        await WriteAsync(
                            writer,
                            new ControlResponse { Id = request.Id, Ok = false, Error = ex.Message })
                            .ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private bool TokenMatches(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(_token);
        var actual = Encoding.UTF8.GetBytes(candidate);
        return expected.Length == actual.Length
            && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static async Task WriteAsync<T>(StreamWriter writer, T value)
    {
        await writer.WriteLineAsync(JsonSerializer.Serialize(value, ControlProtocol.JsonOptions))
            .ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
    }
}
