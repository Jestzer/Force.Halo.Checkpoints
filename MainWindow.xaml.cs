using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Halo.MCC.Force.Checkpoints
{
    public partial class MainWindow : Window
    {
        private uint currentHotkey = 0;

        // Any unique ID will do for now.
        private const int HOTKEY_ID = 9000; 
        private const uint MOD_NONE = 0x0000;

        // P/Invoke declarations for RegisterHotKey and UnregisterHotKey.
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public MainWindow()
        {
            InitializeComponent();
        }

        private void RegisterCurrentHotkey()
        {
            var helper = new WindowInteropHelper(this);
            string hotkeyName = keybindingTextBox.Text.ToUpper();

            // Convert the key name to a Key enumeration, so it can actually be used.
            Key key = (Key)Enum.Parse(typeof(Key), hotkeyName);
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);

            // Register the hotkey.
            if (!RegisterHotKey(helper.Handle, HOTKEY_ID, MOD_NONE, vk))
            {
                MessageBox.Show("Failed to register hotkey.");
            }
            else
            {
                // Store the current hotkey.
                currentHotkey = vk;
            }
        }
        private void UnregisterCurrentHotkey()
        {
            if (currentHotkey != 0)
            {
                var helper = new WindowInteropHelper(this);
                UnregisterHotKey(helper.Handle, HOTKEY_ID);
                // Reset the current hotkey.
                currentHotkey = 0; 
            }
        }
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var source = PresentationSource.FromVisual(this) as HwndSource;
            source.AddHook(HwndHook);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                OnHotKeyPressed();
                handled = true;
            }
            return IntPtr.Zero;
        }
        private void OnHotKeyPressed()
        {
            ForceCheckpointButton_Click(this, new RoutedEventArgs());
        }
        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {

            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            // Toggle between maximized and not.
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        // Changing the window border size when maximized so that it doesn't go past the screen borders.
        public void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                BorderThickness = new Thickness(5);
            }
            else
            {
                BorderThickness = new Thickness(0);
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void HaloCEButton_Click(object sender, RoutedEventArgs e)
        {
            gameSelectedLabel.Content = "Game selected: Halo CE";
        }

        private void Halo2Button_Click(object sender, RoutedEventArgs e)
        {
            gameSelectedLabel.Content = "Game selected: Halo 2";
        }

        private void Halo3Button_Click(object sender, RoutedEventArgs e)
        {
            gameSelectedLabel.Content = "Game selected: Halo 3";
        }

        private void Halo3ODSTButton_Click(object sender, RoutedEventArgs e)
        {
            gameSelectedLabel.Content = "Game selected: Halo 3: ODST";
        }

        private void HaloReachButton_Click(object sender, RoutedEventArgs e)
        {
            gameSelectedLabel.Content = "Game selected: Halo: Reach";
        }

        private void Halo4Button_Click(object sender, RoutedEventArgs e)
        {
            gameSelectedLabel.Content = "Game selected: Halo 4";
        }

        private void ForceCheckpointButton_Click(object sender, RoutedEventArgs e)
        {
            // For testing.
            MessageBox.Show("Hi there.");
        }

        private bool isRecordingInput = false;
        private void RecordInputButton_Click(object sender, RoutedEventArgs e)
        {
            // Toggle the recording state.
            isRecordingInput = !isRecordingInput;

            if (isRecordingInput)
            {
                keybindingTextBox.Text = String.Empty;

                // Start recording.
                KeyDown += OnKeyDownHandler;
                recordInputButton.Content = "Stop Recording Input";
            }
            else
            {
                // Stop recording.
                KeyDown -= OnKeyDownHandler;
                recordInputButton.Content = "Start Recording Input";
            }
        }

        private void OnKeyDownHandler(object sender, KeyEventArgs e)
        {
            // Find out what the key being pressed is.
            Key key = e.Key;

            // I found adding the system modify keys was annoying since it's basically already there.
            // Append the key pressed to the TextBox
            keybindingTextBox.Text += key.ToString();

            // Stop recording after the key is pressed
            isRecordingInput = false;
            KeyDown -= OnKeyDownHandler;

            // Unregister the previous hotkey and register the new one
            UnregisterCurrentHotkey();
            RegisterCurrentHotkey();

            recordInputButton.Content = "Start Recording Input";
        }
    }
}
