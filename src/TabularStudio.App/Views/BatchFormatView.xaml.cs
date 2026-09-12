using System.Windows;
using System.Windows.Controls;
using TabularStudio.App.ViewModels;

namespace TabularStudio.App.Views;

public partial class BatchFormatView : UserControl
{
    private const double CompactWidth = 1000;
    private bool _compact;

    public BatchFormatView()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyResponsiveLayout(ActualWidth);
    }

    private void OnViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged || e.HeightChanged)
            ApplyResponsiveLayout(e.NewSize.Width);
    }

    private void ApplyResponsiveLayout(double width)
    {
        var compact = width < CompactWidth;
        _compact = compact;

        if (compact)
        {
            FilesColumn.Width = new GridLength(188);
            RulesSplitter.Width = new GridLength(0);
            RulesColumn.Width = new GridLength(0);
            RulesColumn.MinWidth = 0;
            RulesRowSplitter.Height = new GridLength(0);
            RulesRow.Height = new GridLength(2, GridUnitType.Star);
            WorkbenchRow.Height = new GridLength(3, GridUnitType.Star);
            RulesPanel.ClearValue(MaxHeightProperty);
            Grid.SetColumn(RulesPanel, 2);
            Grid.SetRow(RulesPanel, 2);
            Grid.SetColumnSpan(RulesPanel, 1);
            Grid.SetRowSpan(FilesPanel, 3);
            Grid.SetRowSpan(FilesColumnLine, 3);
            RulesColumnLine.Visibility = Visibility.Collapsed;
            RulesRowLine.Visibility = Visibility.Visible;
        }
        else
        {
            FilesColumn.Width = new GridLength(220);
            RulesSplitter.Width = new GridLength(16);
            RulesColumn.Width = new GridLength(260);
            RulesColumn.MinWidth = 220;
            RulesRowSplitter.Height = new GridLength(0);
            RulesRow.Height = new GridLength(0);
            WorkbenchRow.Height = new GridLength(1, GridUnitType.Star);
            RulesPanel.ClearValue(MaxHeightProperty);
            Grid.SetColumn(RulesPanel, 4);
            Grid.SetRow(RulesPanel, 0);
            Grid.SetColumnSpan(RulesPanel, 1);
            Grid.SetRowSpan(FilesPanel, 1);
            Grid.SetRowSpan(FilesColumnLine, 1);
            RulesColumnLine.Visibility = Visibility.Visible;
            RulesRowLine.Visibility = Visibility.Collapsed;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DataContext is BatchFormatViewModel { CanConfigure: true } && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is BatchFormatViewModel vm && e.Data.GetData(DataFormats.FileDrop) is string[] files)
            await vm.LoadFilesAsync(files);
        e.Handled = true;
    }
    private void OnPreviewAutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (DataContext is BatchFormatViewModel vm && vm.SelectedFile?.Editor.PreviewDataTable?.Columns[e.PropertyName] is { } column)
            e.Column.Header = column.Caption;
    }
}
