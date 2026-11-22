using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Siming.App;
using Siming.Core.Control;

namespace Siming.Tests;

public sealed class ControlServerTests
{
    [Fact]
    public async Task Authenticates_And_Handles_Request()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        using var server = new ControlServer(
            port,
            "test-token",
            request => Task.FromResult<object?>(new { cmd = request.Cmd }));
        Assert.True(server.Start(out var error), error);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

        await writer.WriteLineAsync(JsonSerializer.Serialize(
            new ControlHello { Token = "test-token" },
            ControlProtocol.JsonOptions));

        var helloLine = await reader.ReadLineAsync();
        var hello = JsonSerializer.Deserialize<ControlHelloResponse>(helloLine!, ControlProtocol.JsonOptions);
        Assert.True(hello?.Ok);

        await writer.WriteLineAsync(JsonSerializer.Serialize(
            new ControlRequest { Id = 7, Cmd = "ping" },
            ControlProtocol.JsonOptions));

        var line = await reader.ReadLineAsync();
        var response = JsonSerializer.Deserialize<ControlResponse>(line!, ControlProtocol.JsonOptions);

        Assert.NotNull(response);
        Assert.True(response!.Ok);
        Assert.Equal(7, response.Id);
        Assert.Equal("ping", ((JsonElement)response.Data!).GetProperty("cmd").GetString());
    }

    [Fact]
    public async Task Rejects_Wrong_Token()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        using var server = new ControlServer(port, "good", _ => Task.FromResult<object?>(null));
        Assert.True(server.Start(out var error), error);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

        await writer.WriteLineAsync(JsonSerializer.Serialize(
            new ControlHello { Token = "bad" },
            ControlProtocol.JsonOptions));

        var hello = JsonSerializer.Deserialize<ControlHelloResponse>(
            (await reader.ReadLineAsync())!,
            ControlProtocol.JsonOptions);

        Assert.False(hello?.Ok);
        Assert.Equal("unauthorized", hello?.Error);
    }
}
