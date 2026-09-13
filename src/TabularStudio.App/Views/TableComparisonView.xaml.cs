using System.Windows;
using System.Windows.Controls;

namespace TabularStudio.App.Views;

public partial class TableComparisonView : UserControl
{
    private const double CompactWidth = 1100;

    public TableComparisonView()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyResponsiveLayout(ActualWidth);
    }

    private void OnViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
        {
            ApplyResponsiveLayout(e.NewSize.Width);
        }
    }

    private void ApplyResponsiveLayout(double width)
    {
        var worksheetWidth = width < CompactWidth ? 120 : 172;
        LeftWorksheetColumn.Width = new GridLength(worksheetWidth);
        RightWorksheetColumn.Width = new GridLength(worksheetWidth);
    }
}
