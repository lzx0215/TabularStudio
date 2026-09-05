using System.Windows;
using System.Windows.Controls;
using TabularStudio.App.ViewModels;

namespace TabularStudio.App.Views;

public partial class DataMatchingView : UserControl
{
    public DataMatchingView()
    {
        InitializeComponent();
    }

    private void OnMasterPreviewAutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (DataContext is DataMatchingViewModel vm && vm.MasterPreviewDataTable != null)
        {
            var col = vm.MasterPreviewDataTable.Columns[e.PropertyName];
            if (col != null && !string.IsNullOrEmpty(col.Caption))
            {
                e.Column.Header = col.Caption;
            }
        }
    }

    private void OnReferencePreviewAutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (DataContext is DataMatchingViewModel vm && vm.ReferencePreviewDataTable != null)
        {
            var col = vm.ReferencePreviewDataTable.Columns[e.PropertyName];
            if (col != null && !string.IsNullOrEmpty(col.Caption))
            {
                e.Column.Header = col.Caption;
            }
        }
    }

    private void OnMasterDragOver(object sender, DragEventArgs e)
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

    private async void OnMasterDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                if (DataContext is DataMatchingViewModel vm && vm.CanBrowseMaster)
                {
                    await vm.LoadMasterFileAsync(files[0]);
                }
            }
        }
    }

    private void OnReferenceDragOver(object sender, DragEventArgs e)
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

    private async void OnReferenceDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                if (DataContext is DataMatchingViewModel vm && vm.CanBrowseReference)
                {
                    await vm.LoadReferenceFileAsync(files[0]);
                }
            }
        }
    }
}
