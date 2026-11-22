using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Siming.Core.Configuration;

public sealed record ConfigLoadResult(bool Success, AppConfig? Config, string? Error);

public sealed class ConfigChangedEventArgs(ConfigLoadResult result) : EventArgs
{
    public ConfigLoadResult Result { get; } = result;
}

/// <summary>
/// 读写 exe 同目录的 processes.json，并监听外部修改。
/// </summary>
public sealed class ConfigStore : IDisposable
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        IndentCharacter = ' ',
        IndentSize = 2,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly FileSystemWatcher? _watcher;
    private readonly Timer? _debounce;
    private readonly object _gate = new();
    private AppConfig _current;
    private DateTime _suppressWatcherUntil;

    public ConfigStore(string filePath, TimeSpan? debounce = null)
    {
        FilePath = Path.GetFullPath(filePath);
        _current = CreateDefault();

        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            try
            {
                _watcher = new FileSystemWatcher(directory, Path.GetFileName(FilePath))
                {
                    NotifyFilter = NotifyFilters.LastWrite
                        | NotifyFilters.Size
                        | NotifyFilters.FileName
                        | NotifyFilters.CreationTime,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true,
                };
                _watcher.Changed += OnFileTouched;
                _watcher.Created += OnFileTouched;
                _watcher.Renamed += OnFileRenamed;
                _watcher.Error += OnWatcherError;
            }
            catch (Exception)
            {
                // 监听失败不影响读写，界面仍可手动重载。
                _watcher = null;
            }
        }

        var delay = debounce ?? TimeSpan.FromMilliseconds(300);
        _debounce = new Timer(_ => ReloadFromWatcher(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        DebounceDelay = delay;
    }

    public string FilePath { get; }

    public TimeSpan DebounceDelay { get; }

    public AppConfig Current => Volatile.Read(ref _current);

    public string? LastError { get; private set; }

    public event EventHandler<ConfigChangedEventArgs>? Changed;

    public static AppConfig CreateDefault() => new()
    {
        Version = 1,
        Settings = new AppSettings(),
        Entries = [],
    };

    /// <summary>读取配置。文件不存在时创建空配置。</summary>
    public ConfigLoadResult Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    var created = CreateDefault();
                    WriteFile(created);
                    _current = created;
                    LastError = null;
                    return new ConfigLoadResult(true, created, null);
                }

                var json = ReadFileWithRetry();
                var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (config is null)
                {
                    LastError = "配置文件内容为空";
                    return new ConfigLoadResult(false, null, LastError);
                }

                Normalize(config);
                var validationError = EntryValidator.ValidateConfig(config);
                if (validationError is not null)
                {
                    LastError = validationError;
                    return new ConfigLoadResult(false, null, validationError);
                }

                _current = config;
                LastError = null;
                return new ConfigLoadResult(true, config, null);
            }
            catch (Exception ex)
            {
                LastError = ex is JsonException je
                    ? $"JSON 解析失败（第 {je.LineNumber + 1} 行，第 {je.BytePositionInLine + 1} 列）：{je.Message}"
                    : ex.Message;
                return new ConfigLoadResult(false, null, LastError);
            }
        }
    }

    /// <summary>写回配置；成功后抑制自己触发的文件监听。</summary>
    public bool TrySave(AppConfig config, out string? error)
    {
        lock (_gate)
        {
            try
            {
                var validationError = EntryValidator.ValidateConfig(config);
                if (validationError is not null)
                {
                    error = validationError;
                    return false;
                }

                WriteFile(config);
                _current = config;
                LastError = null;
                _suppressWatcherUntil = DateTime.UtcNow.AddSeconds(1.5);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }

    public void Reload()
    {
        var result = Load();
        Changed?.Invoke(this, new ConfigChangedEventArgs(result));
    }

    public void Dispose()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnFileTouched;
            _watcher.Created -= OnFileTouched;
            _watcher.Renamed -= OnFileRenamed;
            _watcher.Error -= OnWatcherError;
            _watcher.Dispose();
        }

        _debounce?.Dispose();
    }

    private void OnFileTouched(object sender, FileSystemEventArgs e) => ScheduleReload();

    private void OnFileRenamed(object sender, RenamedEventArgs e) => ScheduleReload();

    private void OnWatcherError(object sender, ErrorEventArgs e) => ScheduleReload();

    private void ScheduleReload()
    {
        if (_debounce is null)
        {
            return;
        }

        try
        {
            _debounce.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void ReloadFromWatcher()
    {
        if (DateTime.UtcNow < _suppressWatcherUntil)
        {
            return;
        }

        var result = Load();
        Changed?.Invoke(this, new ConfigChangedEventArgs(result));
    }

    private string ReadFileWithRetry()
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                using var stream = new FileStream(
                    FilePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return reader.ReadToEnd();
            }
            catch (IOException ex)
            {
                last = ex;
                Thread.Sleep(40);
            }
        }

        throw last ?? new IOException("无法读取配置文件");
    }

    private void WriteFile(AppConfig config)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, FilePath, overwrite: true);
    }

    private static void Normalize(AppConfig config)
    {
        config.Version = config.Version <= 0 ? 1 : config.Version;
        config.Settings ??= new AppSettings();
        config.Entries ??= [];
        config.Entries.RemoveAll(static entry => entry is null);
        foreach (var entry in config.Entries)
        {
            entry.Name = entry.Name?.Trim() ?? "";
            entry.Cwd = entry.Cwd?.Trim() ?? "";
            entry.Command = entry.Command?.Trim() ?? "";
            entry.Shell = string.IsNullOrWhiteSpace(entry.Shell) ? "cmd" : entry.Shell.Trim();
            entry.Encoding = EncodingNames.NormalizeName(entry.Encoding);
        }
    }
}
