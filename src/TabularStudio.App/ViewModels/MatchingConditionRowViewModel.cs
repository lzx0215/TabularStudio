using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TabularStudio.App.Models;

namespace TabularStudio.App.ViewModels;

/// <summary>
/// 单条匹配条件配置 ViewModel (MasterColumn = ReferenceColumn, 固定 AND).
/// </summary>
public sealed partial class MatchingConditionRowViewModel : ObservableObject
{
    private readonly Action<MatchingConditionRowViewModel>? _onDelete;
    private readonly Action? _onConditionChanged;

    public ObservableCollection<AvailableColumnItem> MasterColumns { get; }

    public ObservableCollection<AvailableColumnItem> ReferenceColumns { get; }

    [ObservableProperty]
    private AvailableColumnItem? _selectedMasterColumn;

    [ObservableProperty]
    private AvailableColumnItem? _selectedReferenceColumn;

    [ObservableProperty]
    private bool _canDelete = true;

    [ObservableProperty]
    private int _index = 1;

    [ObservableProperty]
    private bool _showAndSeparator;

    public MatchingConditionRowViewModel(
        ObservableCollection<AvailableColumnItem> masterColumns,
        ObservableCollection<AvailableColumnItem> referenceColumns,
        Action<MatchingConditionRowViewModel>? onDelete = null,
        Action? onConditionChanged = null)
    {
        MasterColumns = masterColumns;
        ReferenceColumns = referenceColumns;
        _onDelete = onDelete;
        _onConditionChanged = onConditionChanged;
    }

    [RelayCommand]
    public void Delete()
    {
        _onDelete?.Invoke(this);
    }

    partial void OnSelectedMasterColumnChanged(AvailableColumnItem? value)
    {
        _onConditionChanged?.Invoke();
    }

    partial void OnSelectedReferenceColumnChanged(AvailableColumnItem? value)
    {
        _onConditionChanged?.Invoke();
    }
}
