using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TabularStudio.App.Services;
using TabularStudio.App.ViewModels;
using TabularStudio.App.Views;
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
            var vm = new TableComparisonViewModel(new WorkbookInspectionService(), new TableComparisonService(),
                new OutputDirectoryPreferenceService(Path.Combine(directory, "preferences.json")));
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
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void Pump(Task task)
    {
        var frame = new DispatcherFrame();
        _ = task.ContinueWith(_ => frame.Continue = false, TaskScheduler.FromCurrentSynchronizationContext());
        Dispatcher.PushFrame(frame); task.GetAwaiter().GetResult();
    }
}
