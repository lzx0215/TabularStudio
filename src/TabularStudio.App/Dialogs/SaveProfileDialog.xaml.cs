using System.Windows;
using System.Windows.Input;

namespace TabularStudio.App.Dialogs;

public partial class SaveProfileDialog : Window
{
    public string? ProfileName { get; private set; }

    public SaveProfileDialog(string promptText = "保存当前配置", string? initialName = null)
    {
        InitializeComponent();
        PromptTextBlock.Text = promptText;
        if (!string.IsNullOrWhiteSpace(initialName))
        {
            NameTextBox.Text = initialName;
            NameTextBox.SelectAll();
        }
    }

    private void OnNameTextBoxLoaded(object sender, RoutedEventArgs e)
    {
        NameTextBox.Focus();
    }

    private void OnNameTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        ErrorTextBlock.Visibility = Visibility.Collapsed;
        if (e.Key == Key.Enter)
        {
            TrySave();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        TrySave();
    }

    private void TrySave()
    {
        string text = NameTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            ErrorTextBlock.Text = "配置名称不能为空。";
            ErrorTextBlock.Visibility = Visibility.Visible;
            NameTextBox.Focus();
            return;
        }

        ProfileName = text;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
