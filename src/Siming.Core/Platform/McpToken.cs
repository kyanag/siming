using System.Security.Cryptography;
using System.Text;

namespace Siming.Core.Platform;

/// <summary>
/// 本机控制接口的端口与令牌。令牌与 processes.json 放在同一目录。
/// </summary>
public static class McpToken
{
    public const int ControlPort = 17321;

    public const string TokenFileName = "mcp.token";

    public static string TokenPathFor(string configPath) =>
        Path.Combine(Path.GetDirectoryName(configPath) ?? AppContext.BaseDirectory, TokenFileName);

    public static string Ensure(string configPath)
    {
        var path = TokenPathFor(configPath);
        var existing = TryReadFile(path);
        if (existing is not null && existing.Length >= 16)
        {
            return existing;
        }

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppContext.BaseDirectory);
        File.WriteAllText(path, token, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return token;
    }

    public static string? TryRead(string configPath) => TryReadFile(TokenPathFor(configPath));

    private static string? TryReadFile(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
