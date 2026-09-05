namespace TabularStudio.App.Models;

/// <summary>
/// 数据匹配页面的生命周期 UI 状态枚举 (Approved UI Baseline 5.1/5.3).
/// 纯 UI 状态，不属于 Core Contract.
/// </summary>
public enum DataMatchingPageState
{
    /// <summary>
    /// 初始空状态（主表与对照表均未载入）
    /// </summary>
    Initial,

    /// <summary>
    /// 文件已载入，正在解析表头/配置未满足 Ready
    /// </summary>
    FileLoaded,

    /// <summary>
    /// 配置完整就绪（主表/对照表/条件/带回字段/输出均就绪），等待用户触发执行
    /// </summary>
    Ready,

    /// <summary>
    /// 正在离线处理，展示进度反馈并阻断配置改动
    /// </summary>
    Processing,

    /// <summary>
    /// 处理圆满完成，展示五项统计面板与文件快捷操作
    /// </summary>
    Success,

    /// <summary>
    /// 处理失败或输入校验阻断，展示明确错误原因与引导
    /// </summary>
    Error
}
