namespace TabularStudio.App.Dialogs;

/// <summary>
/// 当输出文件已存在时用户的选择
/// </summary>
public enum ExistingOutputChoice
{
    /// <summary>
    /// 取消操作
    /// </summary>
    Cancel,

    /// <summary>
    /// 覆盖已有文件
    /// </summary>
    Overwrite,

    /// <summary>
    /// 另存为新文件
    /// </summary>
    SaveAs
}
