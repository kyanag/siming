using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ModelContextProtocol;
using Siming.Core.Control;

namespace Siming.App.Mcp;

public sealed class ControlUnavailableException(string message) : Exception(message);

/// <summary>
/// 访问司命主程序的本机控制接口。主程序没运行时，控制类命令会尝试拉起它。
/// </summary>
public sealed class ControlClient(McpPaths paths)
{
    private static readonly JsonSerializerOptions Json = ControlProtocol.JsonOptions;

    public async Task<JsonElement> SendAsync(
        ControlRequest request,
        bool autoLaunch,
        CancellationToken cancellationToken)
    {
        try
        {
            return await SendOnceAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (ControlUnavailableException) when (autoLaunch)
        {
            await LaunchAppAsync(cancellationToken).ConfigureAwait(false);

            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return await SendOnceAsync(request, cancellationToken).ConfigureAwait(false);
                }
                catch (ControlUnavailableException)
                {
                    await Task.Delay(300, cancellationToken).ConfigureAwait(false);
                }
            }

            throw new McpException("司命在 15 秒内没有启动完成，控制接口不可用。");
        }
    }

    private async Task<JsonElement> SendOnceAsync(
        ControlRequest request,
        CancellationToken cancellationToken)
    {
        var token = ReadToken();
        if (token is null)
        {
            throw new ControlUnavailableException("找不到 mcp.token，司命可能没有运行。");
        }

        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, paths.ControlPort, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            throw new ControlUnavailableException($"连接司命控制接口失败：{ex.Message}");
        }

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

        await writer.WriteLineAsync(
                JsonSerializer.Serialize(new ControlHello { Token = token }, Json))
            .ConfigureAwait(false);

        var helloLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (helloLine is null)
        {
            throw new ControlUnavailableException("控制接口没有响应。");
        }

        var hello = JsonSerializer.Deserialize<ControlHelloResponse>(helloLine, Json);
        if (hello is not { Ok: true })
        {
            throw new McpException(hello?.Error ?? "控制接口鉴权失败。");
        }

        request.Id = 1;
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, Json)).ConfigureAwait(false);

        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (line is null)
        {
            throw new ControlUnavailableException("控制接口断开了连接。");
        }

        var response = JsonSerializer.Deserialize<ControlResponse>(line, Json)
            ?? throw new McpException("控制接口返回了空响应。");

        if (!response.Ok)
        {
            throw new McpException(response.Error ?? "控制接口返回错误。");
        }

        return response.Data is JsonElement element
            ? element
            : JsonSerializer.SerializeToElement(response.Data, Json);
    }

    private async Task LaunchAppAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.AppExePath))
        {
            throw new McpException($"找不到司命主程序：{paths.AppExePath}");
        }

        Process.Start(new ProcessStartInfo(paths.AppExePath, "--tray")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        await Task.Delay(700, cancellationToken).ConfigureAwait(false);
    }

    private string? ReadToken()
    {
        try
        {
            return File.Exists(paths.TokenPath)
                ? File.ReadAllText(paths.TokenPath).Trim()
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
