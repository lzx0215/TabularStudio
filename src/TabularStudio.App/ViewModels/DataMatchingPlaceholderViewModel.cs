using CommunityToolkit.Mvvm.ComponentModel;

namespace TabularStudio.App.ViewModels;

public sealed partial class DataMatchingPlaceholderViewModel : ObservableObject
{
    [ObservableProperty]
    private string? _pendingMasterFilePath;

    [ObservableProperty]
    private bool _hasPendingMaster;

    public DataMatchingPlaceholderViewModel()
    {
    }

    public void ReceiveMasterFilePath(string filePath)
    {
        PendingMasterFilePath = filePath;
        HasPendingMaster = !string.IsNullOrWhiteSpace(filePath);
    }
}
