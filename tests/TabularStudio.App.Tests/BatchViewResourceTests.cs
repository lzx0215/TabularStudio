using System.Runtime.ExceptionServices;
using System.Threading;
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
