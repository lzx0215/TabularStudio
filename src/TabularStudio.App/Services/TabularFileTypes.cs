using System.IO;

namespace TabularStudio.App.Services;

public static class TabularFileTypes
{
    public const string OpenFilter = "表格文件 (*.xlsx;*.xls;*.csv)|*.xlsx;*.xls;*.csv";
    public static bool IsCsv(string? path) => string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase);
    public static bool IsSupported(string? path) => Path.GetExtension(path)?.ToLowerInvariant() is ".xlsx" or ".xls" or ".csv";
    public static string SaveFilter(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return $"表格文件 (*{extension})|*{extension}";
    }
}
