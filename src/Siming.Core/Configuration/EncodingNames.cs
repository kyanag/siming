using System.Text;

namespace Siming.Core.Configuration;

public static class EncodingNames
{
    static EncodingNames()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static bool IsSupported(string? value) => Normalize(value) is not null;

    public static Encoding Resolve(string? value) => Normalize(value) switch
    {
        "gbk" or "gb2312" or "gb18030" or "936" => Encoding.GetEncoding(936),
        _ => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
    };

    public static string NormalizeName(string? value)
    {
        var normalized = Normalize(value);
        return normalized ?? "utf-8";
    }

    private static string? Normalize(string? value)
    {
        var v = value?.Trim().ToLowerInvariant().Replace("_", "-");
        return v switch
        {
            null or "" or "utf-8" or "utf8" => "utf-8",
            "gbk" => "gbk",
            "gb2312" => "gb2312",
            "gb18030" => "gb18030",
            "936" => "936",
            _ => null,
        };
    }
}
