using System.Windows;
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

        // 构造 ViewModel 并注入接口
        var mainWindowViewModel = new MainWindowViewModel(inspectionService, formatService, matchingService);

        // 构造并显示 MainWindow
        var mainWindow = new MainWindow(mainWindowViewModel);
        MainWindow = mainWindow;
        mainWindow.Show();
    }
}
