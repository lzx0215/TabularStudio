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
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri("/TabularStudio.App;component/Styles/DesktopTheme.xaml", UriKind.Relative) });
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
                var matching = new DataMatchingView
                {
                    DataContext = new DataMatchingViewModel(new WorkbookInspectionService(), new DataMatchingService())
                };
                var matchVm = (DataMatchingViewModel)matching.DataContext;
                var fixtureDirectory = Path.Combine(Path.GetTempPath(), "TabularStudio-54-matching-" + Guid.NewGuid());
                Directory.CreateDirectory(fixtureDirectory);
                var master = Path.Combine(fixtureDirectory, "master.csv");
                var reference = Path.Combine(fixtureDirectory, "reference.csv");
                File.WriteAllText(master, "员工编号,部门,是否参与\r\nE001,财务,是\r\nE002,研发,是\r\nE003,销售,是\r\n,财务,是\r\nE004,运营,否\r\nE005,财务,是\r\n");
                File.WriteAllText(reference, "员工编号,部门,岗位,备注\r\nE001,财务,会计,正式\r\nE002,研发,工程师A,正式\r\nE002,研发,工程师B,正式\r\nE004,运营,专员,正式\r\nE005,财务,出纳,正式\r\n");
                void Await(Task task)
                {
                    var frame = new System.Windows.Threading.DispatcherFrame();
                    _ = task.ContinueWith(_ => frame.Continue = false, TaskScheduler.FromCurrentSynchronizationContext());
                    System.Windows.Threading.Dispatcher.PushFrame(frame);
                    task.GetAwaiter().GetResult();
                }
                Await(matchVm.LoadMasterFileAsync(master));
                Await(matchVm.LoadReferenceFileAsync(reference));
                matchVm.Conditions[0].SelectedMasterColumn = matchVm.MasterAvailableColumns[0];
                matchVm.Conditions[0].SelectedReferenceColumn = matchVm.ReferenceAvailableColumns[0];
                matchVm.AddConditionCommand.Execute(null);
                matchVm.Conditions[1].SelectedMasterColumn = matchVm.MasterAvailableColumns[1];
                matchVm.Conditions[1].SelectedReferenceColumn = matchVm.ReferenceAvailableColumns[1];
                matchVm.ReturnFields[2].IsSelected = true; matchVm.ReturnFields[3].IsSelected = true;
                matchVm.IsMasterFilterEnabled = true;
                matchVm.SelectedMasterFilterColumn = matchVm.MasterAvailableColumns[2];
                Await(matchVm.MasterFilterValuesLoadTask);
                matching.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                var valueSelector = (ComboBox)matching.FindName("MasterFilterValueSelector");
                Assert.False(valueSelector.IsEditable);
                Assert.Equal(2, valueSelector.Items.Count);
                Assert.Null(valueSelector.SelectedItem);
                valueSelector.SelectedItem = matchVm.MasterFilterValues.Single(v => v.Value.RawValue == "是");
                matching.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                Assert.Equal("是", matchVm.SelectedMasterFilterValue!.Value.RawValue);
                matchVm.OutputFilePath = Path.Combine(fixtureDirectory, "result.csv");
                Await(matchVm.StartAsync());
                Assert.True(matchVm.HasSuccess, matchVm.ErrorMessage);
                Assert.Equal(6, matchVm.ResultTotalMasterDataRowCount);
                Assert.Equal(2, matchVm.ResultMatchedCount);
                Assert.Equal(1, matchVm.ResultUnmatchedCount);
                Assert.Equal(1, matchVm.ResultDuplicateCount);
                Assert.Equal(1, matchVm.ResultEmptyKeyCount);
                Assert.Equal(1, matchVm.ResultSkippedCount);
                foreach (var size in new[] { new Size(936, 520), new Size(1256, 600), new Size(1576, 780) })
                {
                    foreach (var page in new UserControl[] { view, matching })
                    {
                        page.Width = size.Width; page.Height = size.Height;
                        page.Measure(size); page.Arrange(new Rect(size)); page.UpdateLayout();
                        Assert.IsType<Grid>(page.Content);
                        var footer = (Border)page.FindName("FixedFooter");
                        var startButton = (Button)page.FindName("StartButton");
                        var position = startButton.TranslatePoint(new Point(), page);
                        Assert.True(position.Y >= 0 && position.Y + startButton.ActualHeight <= size.Height);
                        Assert.True(position.X >= 0 && position.X + startButton.ActualWidth <= size.Width);
                        var footerY = footer.TranslatePoint(new Point(), page).Y;
                        if (page is DataMatchingView)
                        {
                            var preview = (DataGrid)page.FindName("MasterPreviewDataGrid");
                            Assert.True(preview.ActualHeight >= 60,
                                "A short workspace must retain a header and a readable data row; source panels may scroll.");
                        }
                        if (page.DataContext is BatchFormatViewModel batchVm)
                        {
                            for (var index = 0; index < 20; index++)
                                batchVm.Failures.Add(new("failed.csv", "真实错误占位（组件fixture）", "Sheet / Header / Output", new string('详', 1000)));
                            page.UpdateLayout();
                            Assert.Equal(footerY, footer.TranslatePoint(new Point(), page).Y, precision: 1);
                            batchVm.Failures.Clear();
                            page.UpdateLayout();
                        }
                        var output = Environment.GetEnvironmentVariable("TABULAR_UI_RENDER_DIR");
                        if (!string.IsNullOrEmpty(output))
                        {
                            Directory.CreateDirectory(output);
                            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                            var background = new System.Windows.Media.DrawingVisual();
                            using (var drawing = background.RenderOpen())
                                drawing.DrawRectangle((System.Windows.Media.Brush)page.FindResource("ContentPanelBrush"), null, new System.Windows.Rect(size));
                            bitmap.Render(background);
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
                Assert.Equal(masterOrigin.X, referenceOrigin.X, precision: 1);
                Assert.True(referenceOrigin.Y > masterOrigin.Y + masterGrid.ActualHeight);

                // Scheme B: Styles and responsive breakpoint assertions
                Assert.NotNull(view.FindResource("TopNavTabStyle"));
                Assert.NotNull(view.FindResource("WorkbenchExpanderStyle"));

                // BatchFormatView breakpoint at 1000 DIP
                view.Width = 1100; view.Measure(new Size(1100, 700)); view.Arrange(new Rect(0, 0, 1100, 700)); view.UpdateLayout();
                var rulesCol = (ColumnDefinition)view.FindName("RulesColumn");
                var filesCol = (ColumnDefinition)view.FindName("FilesColumn");
                Assert.Equal(216, rulesCol.Width.Value);
                Assert.Equal(220, filesCol.Width.Value);

                view.Width = 960; view.Measure(new Size(960, 700)); view.Arrange(new Rect(0, 0, 960, 700)); view.UpdateLayout();
                Assert.Equal(0, rulesCol.Width.Value);
                Assert.Equal(220, filesCol.Width.Value);

                // Moving the per-file output row must retain the batch processing guard.
                var guardVm = (BatchFormatViewModel)view.DataContext;
                var outputRow = (DockPanel)view.FindName("SelectedOutputRow");
                guardVm.IsBusy = true;
                view.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                Assert.False(outputRow.IsEnabled);
                guardVm.IsBusy = false;
                view.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                Assert.Equal(guardVm.CanConfigure, outputRow.IsEnabled);
                Assert.Equal(32, ((Button)view.FindName("StartButton")).ActualHeight);
                Assert.Equal(32, ((DataGrid)view.FindName("FilePreviewGrid")).ColumnHeaderHeight);
                Assert.Equal(28, ((DataGrid)view.FindName("FilePreviewGrid")).RowHeight);

                // DataMatchingView breakpoint at 1040 DIP
                matching.Width = 1100; matching.Measure(new Size(1100, 700)); matching.Arrange(new Rect(0, 0, 1100, 700)); matching.UpdateLayout();
                var settingsCol = (ColumnDefinition)matching.FindName("SettingsColumn");
                Assert.Equal(340, settingsCol.Width.Value);

                matching.Width = 980; matching.Measure(new Size(980, 700)); matching.Arrange(new Rect(0, 0, 980, 700)); matching.UpdateLayout();
                Assert.Equal(300, settingsCol.Width.Value);
                // Render the complete real WPF shell (offscreen component evidence, not desktop QA).
                var renderDirectory = Environment.GetEnvironmentVariable("TABULAR_UI_RENDER_DIR");
                if (!string.IsNullOrEmpty(renderDirectory))
                {
                    var shellVm = new MainWindowViewModel(new WorkbookInspectionService(), new FormatStandardizationService(), new DataMatchingService());
                    var window = new MainWindow(shellVm);
                    var client = (FrameworkElement)window.Content;
                    foreach (var pair in new[] { (Name: "format", Vm: (object)view.DataContext), (Name: "matching", Vm: (object)matchVm) })
                    {
                        shellVm.CurrentViewViewModel = pair.Vm;
                        shellVm.IsFormatStandardizationSelected = pair.Name == "format";
                        shellVm.IsDataMatchingSelected = pair.Name == "matching";
                        client.Width = 1264; client.Height = 681;
                        client.Measure(new Size(1264, 681)); client.Arrange(new Rect(0, 0, 1264, 681)); client.UpdateLayout();
                        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1264, 681, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                        bitmap.Render(client);
                        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                        using var file = File.Create(Path.Combine(renderDirectory, $"shell-component-{pair.Name}.png"));
                        encoder.Save(file);
                    }
                }
                if (!string.IsNullOrEmpty(renderDirectory))
                {
                    var evidence = Path.Combine(renderDirectory, "matching-fixture"); Directory.CreateDirectory(evidence);
                    foreach (var source in Directory.GetFiles(fixtureDirectory))
                        File.Copy(source, Path.Combine(evidence, Path.GetFileName(source)), true);
                }
                Directory.Delete(fixtureDirectory, true);
                app.Shutdown();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(45)), "WPF resource test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
