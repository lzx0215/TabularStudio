using System.Windows;
using System.Windows.Media;

namespace TabularStudio.App.Dialogs;

public partial class ConfirmProfileDialog : Window
{
    public bool Confirmed { get; private set; }

    public ConfirmProfileDialog(
        string title,
        string mainMessage,
        string subMessage,
        string confirmButtonText = "确认",
        bool isDestructive = false)
    {
        InitializeComponent();
        Title = title;
        TitleTextBlock.Text = title;
        MainMessageTextBlock.Text = mainMessage;
        SubMessageTextBlock.Text = subMessage;
        ConfirmButton.Content = confirmButtonText;

        if (!isDestructive)
        {
            ConfirmButton.Background = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)); // Primary blue
        }
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
        Close();
    }
}
