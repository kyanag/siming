using Siming.Core.Platform;

namespace Siming.App.Mcp;

public sealed class McpPaths(string baseDirectory)
{
    public string BaseDirectory { get; } = baseDirectory;

    public string ConfigPath => Path.Combine(BaseDirectory, "processes.json");

    public string TokenPath => McpToken.TokenPathFor(ConfigPath);

    public string AppExePath => Path.Combine(BaseDirectory, "Siming.exe");

    public int ControlPort => McpToken.ControlPort;
}
