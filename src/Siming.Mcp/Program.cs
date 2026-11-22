using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Siming.Mcp;

var builder = Host.CreateApplicationBuilder(args);

// stdout 是 MCP 协议通道，日志必须全部走 stderr。
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton(new McpPaths(AppContext.BaseDirectory));
builder.Services.AddSingleton<ControlClient>();
builder.Services.AddSingleton<McpConfig>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
