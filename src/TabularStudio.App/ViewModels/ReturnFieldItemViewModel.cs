using CommunityToolkit.Mvvm.ComponentModel;
using TabularStudio.App.Models;
using TabularStudio.Core.Contracts;

namespace TabularStudio.App.ViewModels;

/// <summary>
/// 对照表带回字段候选项 ViewModel。
/// 支持多选与搜索过滤，保留物理列身份 ColumnReference。
/// </summary>
public sealed partial class ReturnFieldItemViewModel : ObservableObject
{
    private readonly Action? _onSelectionChanged;

    public ColumnReference Reference { get; }

    public int ColumnNumber => Reference.ColumnNumber;

    public string HeaderText => Reference.HeaderText ?? string.Empty;

    public string ColumnLetter { get; }

    public string DisplayName { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isVisible = true;

    public ReturnFieldItemViewModel(ColumnReference reference, Action? onSelectionChanged = null)
    {
        Reference = reference ?? throw new ArgumentNullException(nameof(reference));
        _onSelectionChanged = onSelectionChanged;
        ColumnLetter = AvailableColumnItem.ToExcelColumnLetter(reference.ColumnNumber);
        DisplayName = !string.IsNullOrWhiteSpace(reference.HeaderText)
            ? $"{reference.HeaderText} ({ColumnLetter}列)"
            : $"第 {ColumnLetter} 列";
    }

    partial void OnIsSelectedChanged(bool value)
    {
        _onSelectionChanged?.Invoke();
    }
}
