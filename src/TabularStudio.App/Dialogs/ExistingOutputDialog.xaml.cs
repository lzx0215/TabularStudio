using System.Windows;

namespace TabularStudio.App.Dialogs;

public partial class ExistingOutputDialog : Window
{
    public ExistingOutputChoice Choice { get; private set; } = ExistingOutputChoice.Cancel;

    public ExistingOutputDialog(string filePath)
    {
        InitializeComponent();
        FilePathTextBlock.Text = filePath;
    }

    private void OnOverwriteClick(object sender, RoutedEventArgs e)
    {
        Choice = ExistingOutputChoice.Overwrite;
        DialogResult = true;
        Close();
    }

    private void OnSaveAsClick(object sender, RoutedEventArgs e)
    {
        Choice = ExistingOutputChoice.SaveAs;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Choice = ExistingOutputChoice.Cancel;
        DialogResult = false;
        Close();
    }
}
