using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TabularStudio.App.Services;
using TabularStudio.Core.Contracts;
using TabularStudio.Core.Services;

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
    private bool _isTableComparisonSelected;

    [ObservableProperty]
    private string _statusMessage = "就绪";

    public BatchFormatViewModel FormatStandardizationVm { get; }

    public DataMatchingViewModel DataMatchingVm { get; }

    public TableComparisonViewModel TableComparisonVm { get; }

    public DataMatchingViewModel DataMatchingPlaceholderVm => DataMatchingVm;

    public MainWindowViewModel(
        IWorkbookInspectionService inspectionService,
        IFormatStandardizationService formatService,
        IDataMatchingService matchingService,
        IOutputDirectoryPreferenceService? outputDirectoryPreferenceService = null,
        ITableComparisonService? comparisonService = null)
    {
        var preferenceService = outputDirectoryPreferenceService ?? new OutputDirectoryPreferenceService();
        TableComparisonVm = new(inspectionService, comparisonService ?? new TableComparisonService(), preferenceService);

        DataMatchingVm = new DataMatchingViewModel(
            inspectionService,
            matchingService,
            outputDirectoryPreferenceService: preferenceService);

        FormatStandardizationVm = new BatchFormatViewModel(
            inspectionService,
            formatService,
            sendToMatching: OnSendToDataMatching,
            preferences: preferenceService);

        // 默认显示格式统一
        CurrentViewViewModel = FormatStandardizationVm;
    }

    [RelayCommand]
    public void SelectFormatStandardization()
    {
        IsFormatStandardizationSelected = true;
        IsDataMatchingSelected = false;
        IsTableComparisonSelected = false;
        CurrentViewViewModel = FormatStandardizationVm;
    }

    [RelayCommand]
    public void SelectDataMatching()
    {
        IsFormatStandardizationSelected = false;
        IsDataMatchingSelected = true;
        IsTableComparisonSelected = false;
        CurrentViewViewModel = DataMatchingVm;
    }

    [RelayCommand]
    public void SelectTableComparison()
    {
        IsFormatStandardizationSelected = false;
        IsDataMatchingSelected = false;
        IsTableComparisonSelected = true;
        CurrentViewViewModel = TableComparisonVm;
    }

    private void OnSendToDataMatching(string outputPath)
    {
        DataMatchingVm.ReceiveMasterFilePath(outputPath);
        SelectDataMatching();
        StatusMessage = "已将格式统一结果作为主表送入数据匹配";
    }
}
