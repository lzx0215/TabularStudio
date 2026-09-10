using System.IO;
using System.Windows;
using TabularStudio.App.Services;
using TabularStudio.App.ViewModels;
using TabularStudio.Core.Contracts;
using TabularStudio.Core.Services;

namespace TabularStudio.App;

/// <summary>
/// 应用程序入口与轻量 Composition Root。
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 创建 Core Services (Composition Root)
        IWorkbookInspectionService inspectionService = new WorkbookInspectionService();
        IFormatStandardizationService formatService = new FormatStandardizationService();
        IDataMatchingService matchingService = new DataMatchingService();

#if DEBUG
        string? testDataDir = Environment.GetEnvironmentVariable("TABULARSTUDIO_TEST_DATA_DIRECTORY");
        string? profileDir = !string.IsNullOrWhiteSpace(testDataDir) ? System.IO.Path.Combine(testDataDir, "processing-profiles") : null;
        string? preferenceFilePath = !string.IsNullOrWhiteSpace(testDataDir) ? System.IO.Path.Combine(testDataDir, "preferences.json") : null;
        IOutputDirectoryPreferenceService outputDirectoryPreferenceService = new OutputDirectoryPreferenceService(preferenceFilePath);
#else
        string? profileDir = null;
        IOutputDirectoryPreferenceService outputDirectoryPreferenceService = new OutputDirectoryPreferenceService();
#endif

        IProcessingProfileStore profileStore = new JsonProcessingProfileStore(profileDir);
        IProcessingProfileValidator profileValidator = new ProcessingProfileValidator(inspectionService);

        // 构造 ViewModel 并注入接口
        var mainWindowViewModel = new MainWindowViewModel(
            inspectionService,
            formatService,
            matchingService,
            outputDirectoryPreferenceService,
            profileStore,
            profileValidator);

        // 构造并显示 MainWindow
        var mainWindow = new MainWindow(mainWindowViewModel);
        MainWindow = mainWindow;
        mainWindow.Show();
    }
}
