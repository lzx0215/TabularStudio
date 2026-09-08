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
                var matching = new DataMatchingView
                {
                    DataContext = new DataMatchingViewModel(new WorkbookInspectionService(), new DataMatchingService())
                };
                foreach (var size in new[] { new Size(960, 540), new Size(1280, 740), new Size(1600, 940) })
                {
                    foreach (var page in new UserControl[] { view, matching })
                    {
                        page.Width = size.Width; page.Height = size.Height;
                        page.Measure(size); page.Arrange(new Rect(size)); page.UpdateLayout();
                        var scroll = Assert.IsType<ScrollViewer>(page.Content);
                        Assert.True(scroll.ViewportWidth > 0);
                        Assert.True(((FrameworkElement)scroll.Content).ActualWidth <= Math.Max(1010, scroll.ViewportWidth) + 1, $"Unexpected content width: {((FrameworkElement)scroll.Content).ActualWidth}, viewport: {scroll.ViewportWidth}");
                        Assert.True(scroll.ViewportHeight > 0);
                        var output = Environment.GetEnvironmentVariable("TABULAR_UI_RENDER_DIR");
                        if (!string.IsNullOrEmpty(output))
                        {
                            Directory.CreateDirectory(output);
                            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                            bitmap.Render(page);
                            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                            using var file = File.Create(Path.Combine(output, $"{page.GetType().Name}-{size.Width}.png"));
                            encoder.Save(file);
                        }
                    }
                }
                var masterGrid = (DataGrid)matching.FindName("MasterPreviewDataGrid");
                var referenceGrid = (DataGrid)matching.FindName("ReferencePreviewDataGrid");
                var masterOrigin = masterGrid.TranslatePoint(new Point(), matching);
                var referenceOrigin = referenceGrid.TranslatePoint(new Point(), matching);
                Assert.Equal(masterOrigin.Y, referenceOrigin.Y, precision: 1);
                Assert.True(referenceOrigin.X > masterOrigin.X + masterGrid.ActualWidth);
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
