using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Siming.App.Mcp;

/// <summary>
/// 同一份 Siming.exe 的 MCP 模式：`Siming.exe --mcp`。
/// stdout 是 MCP 协议通道，日志全部走 stderr。
/// </summary>
internal static class McpServerHost
{
    public static async Task<int> RunAsync()
    {
        try
        {
            var builder = Host.CreateApplicationBuilder(Array.Empty<string>());

            builder.Logging.ClearProviders();
            builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

            builder.Services.AddSingleton(new McpPaths(AppContext.BaseDirectory));
            builder.Services.AddSingleton<ControlClient>();
            builder.Services.AddSingleton<McpConfig>();

            builder.Services
                .AddMcpServer()
                .WithStdioServerTransport()
                .WithToolsFromAssembly(typeof(SimingTools).Assembly);

            await builder.Build().RunAsync().ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                await Console.Error.WriteLineAsync($"Siming MCP 服务异常退出：{ex}").ConfigureAwait(false);
            }
            catch (Exception)
            {
            }

            return 1;
        }
    }
}
