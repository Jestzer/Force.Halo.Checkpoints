using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Force.Halo.Checkpoints.Linux.Views;

public partial class NoticeWindow : Window
{
    public NoticeWindow()
    {
        InitializeComponent();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void DontShowAgainButton_Click(object sender, RoutedEventArgs e)
    {
        Close(true);
    }
}
