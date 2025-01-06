using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Force.Halo.Checkpoints.Linux.Views
{
    public partial class ErrorWindow : Window
    {
        public ErrorWindow()
        {
            InitializeComponent();
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}