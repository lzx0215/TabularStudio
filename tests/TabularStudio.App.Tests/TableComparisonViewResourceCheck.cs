using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TabularStudio.App.Services;
using TabularStudio.App.ViewModels;
using TabularStudio.App.Views;
using TabularStudio.Core.Contracts;
using TabularStudio.Core.Services;

namespace TabularStudio.App.Tests;

// Runs in the existing resource test's single WPF Application/STA host; never shows a desktop window.
internal static class TableComparisonViewResourceCheck
{
    public static void Verify()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ComparisonView-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var inspection = new WorkbookInspectionService();
            var profiles = new JsonProcessingProfileStore(Path.Combine(directory, "profiles"));
            Assert.True(profiles.Save(new(1, "格式配置", ProcessingProfileKind.FormatStandardization,
                new(true, false, false, false, false, false), null)).Success);
            Assert.True(profiles.Save(new(1, "匹配配置", ProcessingProfileKind.DataMatching, null,
                new([new(new(1, "A"), new(1, "A"))], [new(2, "B")], false,
                    new(false, "匹配状态"), null))).Success);
            var shellVm = new MainWindowViewModel(inspection, new FormatStandardizationService(), new DataMatchingService(),
                new OutputDirectoryPreferenceService(Path.Combine(directory, "preferences.json")),
                comparisonService: new TableComparisonService(), profileStore: profiles,
                profileValidator: new ProcessingProfileValidator(inspection));
            var vm = shellVm.TableComparisonVm;
            var view = new TableComparisonView { DataContext = vm, Width = 740, Height = 580 };
            var left = Path.Combine(directory, "left.csv"); var right = Path.Combine(directory, "right.csv");
            File.WriteAllText(left, "A,B\r\nx,1\r\n"); File.WriteAllText(right, "A,B\r\nx,2\r\n");
            Pump(vm.Left.LoadAsync(left)); Pump(vm.Right.LoadAsync(right)); Pump(vm.StartAsync());
            view.Measure(new Size(740, 580)); view.Arrange(new Rect(0, 0, 740, 580));
            view.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); view.UpdateLayout();
            view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); view.UpdateLayout();
            Assert.Contains("不一致", ((TextBlock)view.FindName("ResultSummary")).Text);
            Assert.Equal("left.csv", ((TextBox)view.FindName("LeftFileName")).Text);
            Assert.Equal("right.csv", ((TextBox)view.FindName("RightFileName")).Text);
            var grid = (DataGrid)view.FindName("DifferencesGrid");
            Assert.Single(grid.Items.Cast<object>()); Assert.Equal(5, grid.Columns.Count);
            Assert.True(grid.Columns[0].ActualWidth >= 80, "Coordinate column must remain readable.");
            Assert.True(grid.ActualHeight >= 80);
            Assert.True(grid.TransformToAncestor(view).Transform(new Point(0, grid.ActualHeight)).Y <= view.ActualHeight,
                "Difference table must fit within the minimum workspace height.");
            var background = new DrawingVisual();
            using (var drawing = background.RenderOpen())
                drawing.DrawRectangle((Brush)view.FindResource("AppBackgroundBrush"), null, new Rect(0, 0, 740, 580));
            var bitmap = new RenderTargetBitmap(740, 580, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(background);
            bitmap.Render(view);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var image = File.Create(Path.Combine(AppContext.BaseDirectory, "table-comparison-preview.png"));
            encoder.Save(image);

            // Verify the comparison route in the current top-navigation shell after integration.
            shellVm.SelectTableComparisonCommand.Execute(null);
            Assert.True(shellVm.IsTableComparisonSelected);
            Assert.False(shellVm.IsFormatStandardizationSelected);
            Assert.False(shellVm.IsDataMatchingSelected);
            Assert.IsType<TableComparisonViewModel>(shellVm.CurrentViewViewModel);
            shellVm.CurrentViewViewModel = vm;
            var shell = new MainWindow(shellVm);
            var client = (FrameworkElement)shell.Content;
            client.Width = 944; client.Height = 601;
            client.Measure(new Size(944, 601)); client.Arrange(new Rect(0, 0, 944, 601)); client.UpdateLayout();
            client.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); client.UpdateLayout();
            var hosted = Descendants(client).OfType<TableComparisonView>().Single();
            var hostedGrid = (DataGrid)hosted.FindName("DifferencesGrid");
            Assert.Single(hostedGrid.Items.Cast<object>());
            Assert.True(hostedGrid.ActualHeight >= 80);
            Assert.True(hostedGrid.TransformToAncestor(hosted).Transform(new Point(0, hostedGrid.ActualHeight)).Y <= hosted.ActualHeight,
                "Comparison results must fit above the global status bar.");
            var shellBitmap = new RenderTargetBitmap(944, 601, 96, 96, PixelFormats.Pbgra32);
            var shellBackground = new DrawingVisual();
            using (var drawing = shellBackground.RenderOpen())
                drawing.DrawRectangle(shell.Background, null, new Rect(0, 0, 944, 601));
            shellBitmap.Render(shellBackground);
            shellBitmap.Render(client);
            var shellEncoder = new PngBitmapEncoder(); shellEncoder.Frames.Add(BitmapFrame.Create(shellBitmap));
            using var shellImage = File.Create(Path.Combine(AppContext.BaseDirectory, "table-comparison-shell.png"));
            shellEncoder.Save(shellImage);

            var format = shellVm.FormatStandardizationVm;
            format.SelectedProfile = format.Profiles[0];
            format.ApplyProfile();
            Pump(format.LoadFilesAsync([left]));
            var matching = shellVm.DataMatchingVm;
            Pump(matching.LoadMasterFileAsync(left));
            Pump(matching.LoadReferenceFileAsync(right));
            matching.SelectedProfile = matching.Profiles[0];
            Pump(matching.ApplyProfileAsync());
            Assert.Equal("已应用：匹配配置", matching.ProfileStatusMessage);
            foreach (var size in new[] { new Size(944, 601), new Size(1264, 681) })
            {
                shellVm.SelectFormatStandardization();
                LayoutAndRender(client, size, "profiles-format");
                var formatView = Descendants(client).OfType<BatchFormatView>().Single();
                Assert.Same(format, formatView.DataContext);
                Assert.Single(Descendants(formatView).OfType<ComboBox>()
                    .Single(c => ReferenceEquals(c.ItemsSource, format.Profiles)).Items);
                shellVm.SelectDataMatching();
                LayoutAndRender(client, size, "profiles-matching");
                var matchingView = Descendants(client).OfType<DataMatchingView>().Single();
                Assert.Same(matching, matchingView.DataContext);
                Assert.Single(Descendants(matchingView).OfType<ComboBox>()
                    .Single(c => ReferenceEquals(c.ItemsSource, matching.Profiles)).Items);
                shellVm.SelectTableComparison();
                LayoutAndRender(client, size, "profiles-comparison");
                Assert.Contains("不一致", Descendants(client).OfType<TableComparisonView>()
                    .Single().FindName("ResultSummary") is TextBlock summary ? summary.Text : "");
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void LayoutAndRender(FrameworkElement client, Size size, string name)
    {
        client.Width = size.Width;
        client.Height = size.Height;
        client.Measure(size);
        client.Arrange(new Rect(size));
        client.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        client.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(client);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var image = File.Create(Path.Combine(AppContext.BaseDirectory, $"{name}-{size.Width}.png"));
        encoder.Save(image);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Pump(Task task)
    {
        var frame = new DispatcherFrame();
        _ = task.ContinueWith(_ => frame.Continue = false, TaskScheduler.FromCurrentSynchronizationContext());
        Dispatcher.PushFrame(frame); task.GetAwaiter().GetResult();
    }
}
