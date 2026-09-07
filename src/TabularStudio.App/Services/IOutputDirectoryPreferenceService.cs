namespace TabularStudio.App.Services;

/// <summary>
/// 负责输出目录的本机持久化记忆与解析。
/// 格式统一与数据匹配共用同一个本机输出目录记忆（输出目录方案 A）。
/// </summary>
public interface IOutputDirectoryPreferenceService
{
    /// <summary>
    /// 获取当前记住的本机输出目录。若尚未指定过或无法读取则返回 null。
    /// </summary>
    string? GetRememberedDirectory();

    /// <summary>
    /// 记录用户最新指定的输出目录并持久化保存至本机。
    /// </summary>
    void SetRememberedDirectory(string? directory);

    /// <summary>
    /// 根据当前偏好与回退目录解析出有效输出目录：
    /// 1. 若用户未指定过输出目录，使用 fallbackDirectory；
    /// 2. 若记住的输出目录存在，使用记住的目录；
    /// 3. 若记住的输出目录在磁盘上不存在，回退使用 fallbackDirectory。
    /// </summary>
    string ResolveOutputDirectory(string fallbackDirectory);
}
