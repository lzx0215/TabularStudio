using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TabularStudio.Core.Contracts;

namespace TabularStudio.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private object? _currentViewViewModel;

    [ObservableProperty]
    private bool _isFormatStandardizationSelected = true;

    [ObservableProperty]
    private bool _isDataMatchingSelected;

    [ObservableProperty]
    private string _statusMessage = "就绪";

    public FormatStandardizationViewModel FormatStandardizationVm { get; }

    public DataMatchingPlaceholderViewModel DataMatchingPlaceholderVm { get; }

    public MainWindowViewModel(
        IWorkbookInspectionService inspectionService,
        IFormatStandardizationService formatService)
    {
        DataMatchingPlaceholderVm = new DataMatchingPlaceholderViewModel();

        FormatStandardizationVm = new FormatStandardizationViewModel(
            inspectionService,
            formatService,
            onSendToDataMatching: OnSendToDataMatching);

        // 默认显示格式统一
        CurrentViewViewModel = FormatStandardizationVm;
    }

    [RelayCommand]
    public void SelectFormatStandardization()
    {
        IsFormatStandardizationSelected = true;
        IsDataMatchingSelected = false;
        CurrentViewViewModel = FormatStandardizationVm;
    }

    [RelayCommand]
    public void SelectDataMatching()
    {
        IsFormatStandardizationSelected = false;
        IsDataMatchingSelected = true;
        CurrentViewViewModel = DataMatchingPlaceholderVm;
    }

    private void OnSendToDataMatching(string outputPath)
    {
        DataMatchingPlaceholderVm.ReceiveMasterFilePath(outputPath);
        SelectDataMatching();
        StatusMessage = "已接收主表文件，等待数据匹配功能载入";
    }
}
