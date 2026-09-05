using System.Windows;
using System.Windows.Controls;
using TabularStudio.App.ViewModels;

namespace TabularStudio.App.Views;

public partial class FormatStandardizationView : UserControl
{
    public FormatStandardizationView()
    {
        InitializeComponent();
    }

    private void OnPreviewAutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (DataContext is FormatStandardizationViewModel vm && vm.PreviewDataTable != null)
        {
            var col = vm.PreviewDataTable.Columns[e.PropertyName];
            if (col != null && !string.IsNullOrEmpty(col.Caption))
            {
                e.Column.Header = col.Caption;
            }
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                if (DataContext is FormatStandardizationViewModel vm && vm.CanBrowseInput)
                {
                    await vm.LoadFileAsync(files[0]);
                }
            }
        }
    }
}
