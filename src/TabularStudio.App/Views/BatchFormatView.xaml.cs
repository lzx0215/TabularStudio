using System.Windows;
using System.Windows.Controls;
using TabularStudio.App.ViewModels;

namespace TabularStudio.App.Views;

public partial class BatchFormatView : UserControl
{
    public BatchFormatView() => InitializeComponent();
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
