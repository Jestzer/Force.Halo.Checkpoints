using System.Windows;
using System.Windows.Input;

namespace Force.Halo.Checkpoints
{
    public partial class DisclaimerWindow : Window
    {
        public DisclaimerWindow()
        {
            InitializeComponent();
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void IAgreeButton_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.IsDisclaimerAgreed = true;
            Properties.Settings.Default.Save();
            Close();
        }
    }
}
