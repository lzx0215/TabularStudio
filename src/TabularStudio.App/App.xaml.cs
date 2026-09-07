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

        // 创建 UI Services
        IOutputDirectoryPreferenceService outputDirectoryPreferenceService = new OutputDirectoryPreferenceService();

        // 构造 ViewModel 并注入接口
        var mainWindowViewModel = new MainWindowViewModel(
            inspectionService,
            formatService,
            matchingService,
            outputDirectoryPreferenceService);

        // 构造并显示 MainWindow
        var mainWindow = new MainWindow(mainWindowViewModel);
        MainWindow = mainWindow;
        mainWindow.Show();
    }
}
