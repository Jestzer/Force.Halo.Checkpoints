using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Halo.MCC.Force.Checkpoints
{
    public partial class MainWindow : Window
    {
        private uint currentHotkey = 0;
        public string gameSelected = string.Empty;

        // Any unique ID will do for now.
        private const int HOTKEY_ID = 9000;
        private const uint MOD_NONE = 0x0000;

        // P/Invoke declarations for RegisterHotKey, UnregisterHotKey, opening the process, and reading/writing memory.
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);
        [DllImport("kernel32.dll")]
        public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out int lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out int lpNumberOfBytesWritten);

        [DllImport("psapi.dll", SetLastError = true)]
        public static extern bool EnumProcessModulesEx(IntPtr hProcess, [Out] IntPtr[] lphModule, int cb, out int lpcbNeeded, uint dwFilterFlag);

        [DllImport("psapi.dll")]
        public static extern uint GetModuleBaseName(IntPtr hProcess, IntPtr hModule, StringBuilder lpBaseName, int nSize);
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
            source?.AddHook(HwndHook);
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
            gameSelected = "Halo CE";
        }

        private void Halo2Button_Click(object sender, RoutedEventArgs e)
        {
            gameSelectedLabel.Content = "Game selected: Halo 2";
            gameSelected = "Halo 2";
        }

        private void Halo3Button_Click(object sender, RoutedEventArgs e)
        {
            gameSelectedLabel.Content = "Game selected: Halo 3";
            gameSelected = "Halo 3";
        }

        private void Halo3ODSTButton_Click(object sender, RoutedEventArgs e)
        {
            gameSelectedLabel.Content = "Game selected: Halo 3: ODST";
            gameSelected = "Halo 3 ODST";
        }

        private void HaloReachButton_Click(object sender, RoutedEventArgs e)
        {
            gameSelectedLabel.Content = "Game selected: Halo: Reach";
            gameSelected = "Halo Reach";
        }

        private void Halo4Button_Click(object sender, RoutedEventArgs e)
        {
            gameSelectedLabel.Content = "Game selected: Halo 4";
            gameSelected = "Halo 4";
        }

        private void ForceCheckpoint(string gameName, string dllName, int offset)
        {
            try
            {
                string processName = "MCC-Win64-Shipping";
                int processId = GetProcessIdByName(processName);

                if (processId == -1)
                {
                    MessageBox.Show($"{gameName} is not running.");
                    return;
                }

                const int PROCESS_WM_READ = 0x0010;
                const int PROCESS_WM_WRITE = 0x0020;
                const int PROCESS_VM_OPERATION = 0x0008;

                IntPtr processHandle = OpenProcess(PROCESS_WM_READ | PROCESS_WM_WRITE | PROCESS_VM_OPERATION, false, processId);

                // Get the base address of the DLL in the process's memory space
                IntPtr dllBaseAddress = GetModuleBaseAddress(processId, dllName);

                if (dllBaseAddress == IntPtr.Zero)
                {
                    MessageBox.Show($"Failed to find the base address of {dllName}.");
                    return;
                }

                // Calculate the address to write to by adding the offset to the base address
                IntPtr addressToWriteTo = IntPtr.Add(dllBaseAddress, offset);

                // Define the value to write (1 byte)
                byte valueToWrite = 1;

                // Allocate a buffer with the value to write
                byte[] buffer = new byte[] { valueToWrite };

                // Write the value to the calculated address
                bool result = WriteProcessMemory(processHandle, addressToWriteTo, buffer, buffer.Length, out int bytesWritten);

                if (result && bytesWritten == buffer.Length)
                {
                    MessageBox.Show("Checkpoint forced successfully.");
                }
                else
                {
                    MessageBox.Show("Failed to write to process memory.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        private void ForceCheckpointButton_Click(object sender, RoutedEventArgs e)
        {
            if (gameSelected == "Halo CE")
            {
                ForceCheckpoint("Halo CE", "halo1.dll", 0x2B23707);
            }
            else if (gameSelected == "Halo 2")
            {
                ForceCheckpoint("Halo 2", "halo2.dll", 0xE6FD7E);
            }
            else if (gameSelected == "Halo 3")
            {
                ForceCheckpoint("Halo 3", "halo3.dll", 0xE6FD7E);
            }
            else if (gameSelected == "Halo 4")
            {
                ForceCheckpoint("Halo 4", "halo4.dll", 0xE6FD7E);
            }
            else if (gameSelected == "Halo Reach")
            {
                ForceCheckpoint("Halo Reach", "haloreach.dll", 0xE6FD7E);
            }
            else if (gameSelected == "Halo 3 ODST")
            {
                ForceCheckpoint("Halo 3 ODST", "halo3odst.dll", 0xE6FD7E);
            }
            else
            {
                MessageBox.Show("Select a game first.");
            }
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

            // Only accept function keys.
            if (key < Key.F1 || key > Key.F12)
            {
                MessageBox.Show("Only function keys are accepted.");
                return;
            }

            // Append the key pressed to the TextBox.
            keybindingTextBox.Text += key.ToString();


            // Stop recording after the key is pressed.
            isRecordingInput = false;
            KeyDown -= OnKeyDownHandler;

            // Unregister the previous hotkey and register the new one.
            UnregisterCurrentHotkey();
            RegisterCurrentHotkey();

            recordInputButton.Content = "Start Recording Input";
        }
        public static IntPtr FindPointerAddress(IntPtr hProc, IntPtr ptr, int[] offsets)
        {
            var buffer = new byte[IntPtr.Size];

            ptr += offsets[0];
            if (offsets.Length == 1)
            {
                return ptr;
            }

            offsets = offsets.Skip(1).ToArray();

            foreach (int i in offsets)
            {
                ReadProcessMemory(hProc, ptr, buffer, buffer.Length, out int read);

                ptr = (IntPtr.Size == 4)
                ? IntPtr.Add(new IntPtr(BitConverter.ToInt32(buffer, 0)), i)
                : ptr = IntPtr.Add(new IntPtr(BitConverter.ToInt64(buffer, 0)), i);
            }
            return ptr;
        }

        private int GetProcessIdByName(string processName)
        {
            // Get all processes with the specified name.
            Process[] processes = Process.GetProcessesByName(processName);
            if (processes.Length > 0)
            {
                // Return the PID of the first process found.
                return processes[0].Id;
            }
            else
            {
                // No process found with the specified name.
                return -1;
            }
        }

        private IntPtr GetModuleBaseAddress(int processId, string moduleName)
        {
            IntPtr modBaseAddr = IntPtr.Zero;
            IntPtr[] moduleHandles = new IntPtr[1024];

            if (EnumProcessModulesEx(Process.GetProcessById(processId).Handle, moduleHandles, IntPtr.Size * moduleHandles.Length, out int bytesNeeded, 0x03))
            {
                int numModules = bytesNeeded / IntPtr.Size;
                for (int i = 0; i < numModules; i++)
                {
                    StringBuilder modName = new StringBuilder(255);
                    if (GetModuleBaseName(Process.GetProcessById(processId).Handle, moduleHandles[i], modName, modName.Capacity) > 0)
                    {
                        if (modName.ToString().Equals(moduleName, StringComparison.OrdinalIgnoreCase))
                        {
                            modBaseAddr = moduleHandles[i];
                            break;
                        }
                    }
                }
            }
            return modBaseAddr;
        }
    }
}
