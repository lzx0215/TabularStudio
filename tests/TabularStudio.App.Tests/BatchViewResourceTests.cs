using System.Runtime.ExceptionServices;
using System.Threading;
using System.IO;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using TabularStudio.App.Services;
using TabularStudio.App.ViewModels;
using TabularStudio.App.Views;
using TabularStudio.Core.Services;

namespace TabularStudio.App.Tests;

public sealed class BatchViewResourceTests
{
    [Fact]
    public void BatchViewResolvesResourcesAndLaysOutWithoutShowingAWindow()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application();
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri("/TabularStudio.App;component/Styles/Theme.xaml", UriKind.Relative) });
                var view = new BatchFormatView
                {
                    DataContext = new BatchFormatViewModel(new WorkbookInspectionService(), new FormatStandardizationService()),
                    Width = 800, Height = 640
                };
                view.Measure(new Size(800, 640));
                view.Arrange(new Rect(0, 0, 800, 640));
                view.UpdateLayout();
                Assert.IsType<BooleanToVisibilityConverter>(view.FindResource("BoolToVisibilityConverter"));
                var directory = Path.Combine(Path.GetTempPath(), "TabularStudio-52-view-" + Guid.NewGuid());
                Directory.CreateDirectory(directory);
                try
                {
                    var first = Path.Combine(directory, "first.csv"); var second = Path.Combine(directory, "second.csv");
                    File.WriteAllText(first, "FirstHeader,Value\r\nfirst,1\r\n");
                    File.WriteAllText(second, "SecondHeader,Value\r\nsecond,2\r\n");
                    var vm = (BatchFormatViewModel)view.DataContext;
                    SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(view.Dispatcher));
                    var loading = vm.LoadFilesAsync([first, second]);
                    var frame = new System.Windows.Threading.DispatcherFrame();
                    _ = loading.ContinueWith(_ => frame.Continue = false, TaskScheduler.FromCurrentSynchronizationContext());
                    System.Windows.Threading.Dispatcher.PushFrame(frame);
                    loading.GetAwaiter().GetResult();
                    view.UpdateLayout();
                    var selector = (ComboBox)view.FindName("PreviewFileSelector");
                    var grid = (DataGrid)view.FindName("FilePreviewGrid");
                    selector.SelectedIndex = 1;
                    view.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                    view.UpdateLayout();
                    Assert.Same(vm.Files[1], vm.SelectedFile);
                    Assert.Equal("second", ((DataView)grid.ItemsSource)[0][0]);
                    Assert.Equal("SecondHeader", grid.Columns[0].Header);
                    selector.SelectedIndex = 0;
                    view.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                    view.UpdateLayout();
                    Assert.Equal("first", ((DataView)grid.ItemsSource)[0][0]);
                    Assert.Equal("FirstHeader", grid.Columns[0].Header);
                }
                finally { Directory.Delete(directory, true); }
                TableComparisonViewResourceCheck.Verify();
                app.Shutdown();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "WPF resource test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
