using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TabularStudio.Core.Contracts;

namespace TabularStudio.Core.Services;

public sealed class JsonProcessingProfileStore : IProcessingProfileStore
{
    private const int MaxFileBytes = 1024 * 1024;
    private readonly string directory;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32
    };

    public JsonProcessingProfileStore(string? directory = null)
    {
        this.directory = Path.GetFullPath(directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TabularStudio", "processing-profiles"));
    }

    public ProfileListResult List(ProcessingProfileKind kind)
    {
        var profiles = new List<ProcessingProfile>();
        var errors = new List<string>();
        if (!Enum.IsDefined(kind)) return new([], ["配置功能类型无效。"]);
        try
        {
            var folder = KindDirectory(kind);
            foreach (var path in Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly))
            {
                var loaded = Read(path, kind);
                if (loaded.Success) profiles.Add(loaded.Profile!);
                else errors.Add($"配置文件 {Path.GetFileName(path)}：{loaded.Error}");
            }
        }
        catch (DirectoryNotFoundException ex)
        {
            // A missing first-use directory is normal. A file in any ancestor's place is not.
            var path = KindDirectory(kind);
            while (!string.IsNullOrEmpty(path))
            {
                if (File.Exists(path)) { errors.Add($"无法读取配置列表，目录位置被文件占用：{ex.Message}"); break; }
                path = Path.GetDirectoryName(path);
            }
        }
        catch (Exception ex) when (IsFileError(ex)) { errors.Add($"无法读取配置列表：{ex.Message}"); }
        return new(profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray(), errors);
    }

    public ProfileLoadResult Load(ProcessingProfileKind kind, string name)
    {
        if (!Enum.IsDefined(kind) || string.IsNullOrWhiteSpace(name)) return new(false, null, "请选择有效配置名称和功能。");
        return Read(ProfilePath(kind, name), kind);
    }

    public ProfileWriteResult Save(ProcessingProfile profile, bool overwrite = false)
    {
        var errors = ProcessingProfileValidator.ValidateStructure(profile);
        if (errors.Count > 0) return new(false, false, string.Join(Environment.NewLine, errors));
        var normalized = profile with { Name = profile.Name.Trim() };
        string? temporary = null;
        try
        {
            var folder = KindDirectory(normalized.Kind);
            Directory.CreateDirectory(folder);
            // Serialize cooperating processes without keeping user configuration files open.
            using var mutationLock = new FileStream(Path.Combine(folder, ".write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var target = ProfilePath(normalized.Kind, normalized.Name);
            if (File.Exists(target) && !overwrite) return new(false, true, "同名配置已存在，请确认是否覆盖。");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(normalized, JsonOptions);
            if (bytes.Length > MaxFileBytes) return new(false, false, "配置内容过大（最多 1 MiB）。");
            temporary = Path.Combine(folder, $".{Guid.NewGuid():N}.tmp");
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }
            // On failure the old file remains intact; never truncate the destination in place.
            if (overwrite && File.Exists(target)) File.Replace(temporary, target, null);
            else File.Move(temporary, target);
            temporary = null;
            return new(true, false, null);
        }
        catch (Exception ex) when (IsFileError(ex)) { return new(false, false, $"保存配置失败，原配置未被替换：{ex.Message}"); }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception ex) when (IsFileError(ex)) { /* Only this attempt's temporary file may remain. */ }
            }
        }
    }

    public ProfileWriteResult Delete(ProcessingProfileKind kind, string name)
    {
        if (!Enum.IsDefined(kind) || string.IsNullOrWhiteSpace(name)) return new(false, false, "请选择有效配置名称和功能。");
        try
        {
            var folder = KindDirectory(kind);
            if (!Directory.Exists(folder)) return new(false, false, "配置已不存在，请刷新列表。");
            using var mutationLock = new FileStream(Path.Combine(folder, ".write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var path = ProfilePath(kind, name);
            if (!File.Exists(path)) return new(false, false, "配置已不存在，请刷新列表。");
            File.Delete(path);
            return new(true, false, null);
        }
        catch (Exception ex) when (IsFileError(ex)) { return new(false, false, $"删除配置失败：{ex.Message}"); }
    }

    private ProfileLoadResult Read(string path, ProcessingProfileKind expectedKind)
    {
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (input.Length > MaxFileBytes) return new(false, null, "配置文件过大（最多 1 MiB）。");
            var profile = JsonSerializer.Deserialize<ProcessingProfile>(input, JsonOptions);
            var errors = ProcessingProfileValidator.ValidateStructure(profile);
            if (errors.Count > 0) return new(false, null, string.Join(Environment.NewLine, errors));
            if (profile!.Kind != expectedKind || profile.Name != profile.Name.Trim() ||
                !string.Equals(Path.GetFullPath(path), ProfilePath(expectedKind, profile.Name), StringComparison.OrdinalIgnoreCase))
                return new(false, null, "配置名称、功能或存储文件不一致。");
            return new(true, profile, null);
        }
        catch (Exception ex) when (IsFileError(ex)) { return new(false, null, $"读取配置失败：{ex.Message}"); }
    }

    private string KindDirectory(ProcessingProfileKind kind) => Path.Combine(directory, kind.ToString());
    private string ProfilePath(ProcessingProfileKind kind, string name)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name.Trim().ToUpperInvariant())));
        return Path.Combine(KindDirectory(kind), hash + ".json");
    }
    private static bool IsFileError(Exception ex) => ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException or System.Security.SecurityException;
}
