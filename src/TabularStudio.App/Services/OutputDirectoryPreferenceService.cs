using System.IO;
using System.Text;
using System.Text.Json;

namespace TabularStudio.App.Services;

/// <summary>
/// 本机输出目录偏好管理服务。
/// 完全离线保存于本地应用数据目录中（%LOCALAPPDATA%\TabularStudio\preferences.json）。
/// </summary>
public sealed class OutputDirectoryPreferenceService : IOutputDirectoryPreferenceService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _preferencesFilePath;
    private readonly Func<string, bool> _directoryExists;
    private readonly object _lock = new();
    private string? _cachedDirectory;
    private bool _hasLoaded;

    public OutputDirectoryPreferenceService(
        string? preferencesFilePath = null,
        Func<string, bool>? directoryExists = null)
    {
        _preferencesFilePath = preferencesFilePath ?? GetDefaultPreferencesFilePath();
        _directoryExists = directoryExists ?? Directory.Exists;
    }

    public static string GetDefaultPreferencesFilePath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "TabularStudio", "preferences.json");
    }

    public string? GetRememberedDirectory()
    {
        lock (_lock)
        {
            if (!_hasLoaded)
            {
                _cachedDirectory = LoadFromDisk();
                _hasLoaded = true;
            }

            return _cachedDirectory;
        }
    }

    public void SetRememberedDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        string normalized;
        try
        {
            normalized = Path.GetFullPath(directory);
        }
        catch
        {
            return;
        }

        lock (_lock)
        {
            _cachedDirectory = normalized;
            _hasLoaded = true;
            SaveToDisk(normalized);
        }
    }

    public string ResolveOutputDirectory(string fallbackDirectory)
    {
        string? remembered = GetRememberedDirectory();
        if (!string.IsNullOrWhiteSpace(remembered) && _directoryExists(remembered))
        {
            return remembered;
        }

        return fallbackDirectory;
    }

    private string? LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_preferencesFilePath))
            {
                return null;
            }

            string json = File.ReadAllText(_preferencesFilePath, Encoding.UTF8);
            var model = JsonSerializer.Deserialize<PreferencesData>(json, SerializerOptions);
            if (!string.IsNullOrWhiteSpace(model?.OutputDirectory))
            {
                return model.OutputDirectory;
            }
        }
        catch
        {
            // 本地偏好文件异常或损坏时优雅降级为未指定
        }

        return null;
    }

    private void SaveToDisk(string? directory)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_preferencesFilePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var model = new PreferencesData { OutputDirectory = directory };
            string json = JsonSerializer.Serialize(model, SerializerOptions);
            File.WriteAllText(_preferencesFilePath, json, Encoding.UTF8);
        }
        catch
        {
            // 离线单机桌面工具：偏好设置保存失败时静默降级，不中断主业务流程
        }
    }

    private sealed class PreferencesData
    {
        public string? OutputDirectory { get; set; }
    }
}
