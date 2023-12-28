using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Halo.MCC.Force.Checkpoints
{
    public partial class MainWindow : Window
    {
        private uint currentHotkey = 0;
        public string gameSelected = string.Empty;
        public string friendlyGameName = string.Empty;

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

            // For printing the version number.
            DataContext = this;

            if (Properties.Settings.Default.HotKeyPreference != string.Empty)
            {
                KeyBindingTextBox.Text = Properties.Settings.Default.HotKeyPreference;               
            }

            if (Properties.Settings.Default.LastGameSelected != string.Empty)
            {
                gameSelected = Properties.Settings.Default.LastGameSelected;
                GameSelectedLabel.Content = Properties.Settings.Default.LastGameSelectedLabel;
                friendlyGameName = Properties.Settings.Default.LastGameFriendlyName;
                StatusTextBlock.Text = "Status: Awaiting input";
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (KeyBindingTextBox.Text != string.Empty)
            {
                Properties.Settings.Default.HotKeyPreference = KeyBindingTextBox.Text;
            }

            if (gameSelected != string.Empty)
            {
                Properties.Settings.Default.LastGameSelected = gameSelected;
                Properties.Settings.Default.LastGameFriendlyName = friendlyGameName;
                Properties.Settings.Default.LastGameSelectedLabel = (string)GameSelectedLabel.Content;
            }

            Properties.Settings.Default.Save();
            base.OnClosed(e);
        }

        private void ShowErrorWindow(string errorMessage)
        {
            ErrorWindow errorWindow = new ErrorWindow();
            errorWindow.ErrorTextBlock.Text = errorMessage;
            errorWindow.Owner = this;
            errorWindow.ShowDialog();
        }

        private void ShowUpdateWindow(string errorMessage, string customTitle)
        {
            ErrorWindow errorWindow = new ErrorWindow();
            errorWindow.ErrorTextBlock.Text = errorMessage;
            errorWindow.Owner = this;
            errorWindow.Title = customTitle;
            errorWindow.ShowDialog();
        }

        public static string PackageVersion
        {
            get
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                if (assembly != null)
                {
                    var version = assembly.GetName().Version;
                    if (version != null)
                    {
                        return version.ToString();
                    }
                }
                return "Error getting version number.";
            }
        }

        private async void CheckforUpdateButton_Click(object sender, EventArgs e)
        {
            Version currentVersion = new(PackageVersion);

            // GitHub API URL for the latest release.
            string latestReleaseUrl = "https://api.github.com/repos/Jestzer/Halo.Force.Checkpoints/releases/latest";

            // Use HttpClient to fetch the latest release data.
            using HttpClient client = new();

            // GitHub API requires a user-agent. I'm adding the extra headers to reduce HTTP error 403s.
            client.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("Halo.Force.Checkpoints", PackageVersion));
            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            try
            {
                try
                {
                    // Make the latest release a JSON string.
                    string jsonString = await client.GetStringAsync(latestReleaseUrl);

                    // Parse the JSON to get the tag_name (version number).
                    using JsonDocument doc = JsonDocument.Parse(jsonString);
                    JsonElement root = doc.RootElement;
                    string latestVersionString = root.GetProperty("tag_name").GetString()!;

                    // Remove 'v' prefix if present in the tag name.
                    latestVersionString = latestVersionString.TrimStart('v');

                    // Parse the version string.
                    Version latestVersion = new Version(latestVersionString);

                    // Compare the current version with the latest version.
                    if (currentVersion.CompareTo(latestVersion) < 0)
                    {
                        // A newer version is available!
                        ErrorWindow errorWindow = new();
                        errorWindow.Owner = this;
                        errorWindow.ErrorTextBlock.Text = "";
                        errorWindow.URLTextBlock.Visibility = Visibility.Visible;
                        errorWindow.Title = "Check for updates";
                        errorWindow.ShowDialog();
                    }
                    else
                    {
                        ShowUpdateWindow("You are using the latest release available.", "Check for updates");
                    }
                }
                catch (JsonException ex)
                {
                    ShowUpdateWindow("The Json code in this program didn't work. Here's the automatic error message it made: \"" + ex.Message + "\"", "Check for updates");
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    ShowUpdateWindow("HTTP error 403: GitHub is saying you're sending them too many requests, so... slow down, I guess? " +
                        "Here's the automatic error message: \"" + ex.Message + "\"", "Check for updates");
                }
                catch (HttpRequestException ex)
                {
                    ShowUpdateWindow("HTTP error. Here's the automatic error message: \"" + ex.Message + "\"", "Check for updates");
                }
            }
            catch (Exception ex)
            {
                ShowUpdateWindow("Oh dear, it looks this program had a hard time making the needed connection to GitHub. Make sure you're connected to the internet " +
                    "and your lousy firewall/VPN isn't blocking the connection. Here's the automated error message: \"" + ex.Message + "\"", "Check for updates");
            }
        }

        private void RegisterCurrentHotkey()
        {
            var helper = new WindowInteropHelper(this);
            string hotkeyName = KeyBindingTextBox.Text.ToUpper();
            uint vk;

            // Convert the key name to a Key enumeration, so it can actually be used.
            try
            {
                Key key = (Key)Enum.Parse(typeof(Key), hotkeyName);
                vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            }
            catch
            {
                KeyBindingTextBox.Text = string.Empty;
                ShowErrorWindow($"You cannot use that key as your hotkey.");
                UnregisterCurrentHotkey();
                return;
            }

            // Register the hotkey.
            if (!RegisterHotKey(helper.Handle, HOTKEY_ID, MOD_NONE, vk))
            {
                ShowErrorWindow("Failed to register hotkey.");
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
            RegisterCurrentHotkey();
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

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void HaloCEButton_Click(object sender, RoutedEventArgs e)
        {
            GameSelectedLabel.Content = $"Game selected: Halo CE";
            gameSelected = "Halo CE";
            friendlyGameName = "Halo: Combat Evolved";
            StatusTextBlock.Text = "Status: Awaiting input";
        }

        private void Halo2Button_Click(object sender, RoutedEventArgs e)
        {
            GameSelectedLabel.Content = "Game selected: Halo 2";
            gameSelected = "Halo 2";
            friendlyGameName = gameSelected;
            StatusTextBlock.Text = "Status: Awaiting input";
        }

        private void Halo3Button_Click(object sender, RoutedEventArgs e)
        {
            GameSelectedLabel.Content = "Game selected: Halo 3";
            gameSelected = "Halo 3";
            friendlyGameName = gameSelected;
            StatusTextBlock.Text = "Status: Awaiting input";
        }

        private void Halo3ODSTButton_Click(object sender, RoutedEventArgs e)
        {
            GameSelectedLabel.Content = "Game selected: Halo 3: ODST";
            gameSelected = "Halo 3 ODST";
            friendlyGameName = "Halo 3: ODST";
            StatusTextBlock.Text = "Status: Awaiting input";
        }

        private void HaloReachButton_Click(object sender, RoutedEventArgs e)
        {
            GameSelectedLabel.Content = "Game selected: Halo: Reach";
            gameSelected = "Halo Reach";
            friendlyGameName = "Halo: Reach";
            StatusTextBlock.Text = "Status: Awaiting input";
        }

        private void Halo4Button_Click(object sender, RoutedEventArgs e)
        {
            GameSelectedLabel.Content = "Game selected: Halo 4";
            gameSelected = "Halo 4";
            friendlyGameName = gameSelected;
            StatusTextBlock.Text = "Status: Awaiting input";
        }

        private void HaloCEOGButton_Click(object sender, RoutedEventArgs e)
        {
            GameSelectedLabel.Content = "Game selected: Halo: CE (non-MCC)";
            gameSelected = "Halo CE OG";
            friendlyGameName = "The original Halo: Combat Evolved for PC";
            StatusTextBlock.Text = "Status: Awaiting input";
        }

        private void HaloCustomEditionButton_Click(object sender, RoutedEventArgs e)
        {
            GameSelectedLabel.Content = "Game selected: Halo: Custom Edition";
            gameSelected = "Halo Custom Edition";
            friendlyGameName = "Halo: Custom Edition";
            StatusTextBlock.Text = "Status: Awaiting input";
        }

        private void Halo2VistaButton_Click(object sender, RoutedEventArgs e)
        {
            GameSelectedLabel.Content = "Game selected: Halo 2: Vista";
            gameSelected = "Halo 2 Vista";
            friendlyGameName = "Halo 2: Vista";
            StatusTextBlock.Text = "Status: Awaiting input";
        }

        private void ForceCheckpoint(string gameSelected, string dllName, int offset)
        {
            try
            {
                string processName = string.Empty;

                if (gameSelected == "Halo CE OG")
                {
                    processName = "halo";
                }
                else if (gameSelected == "Halo Custom Edition")
                {
                    processName = "haloce";
                }
                else if (gameSelected == "Halo 2 Vista")
                {
                    processName = "halo2";
                }
                else
                {
                    processName = "MCC-Win64-Shipping";
                }

                int processId = GetProcessIdByName(processName);

                if (processId == -1)
                {
                    ShowErrorWindow($"{friendlyGameName} is not running.");
                    return;
                }

                const int PROCESS_WM_READ = 0x0010;
                const int PROCESS_WM_WRITE = 0x0020;
                const int PROCESS_VM_OPERATION = 0x0008;

                IntPtr processHandle = OpenProcess(PROCESS_WM_READ | PROCESS_WM_WRITE | PROCESS_VM_OPERATION, false, processId);

                // Get the base address of the DLL in the process's memory space.
                IntPtr dllBaseAddress = GetModuleBaseAddress(processId, dllName);

                if (dllBaseAddress == IntPtr.Zero)
                {
                    ShowErrorWindow($"Failed to find the base address of {dllName}.");
                    return;
                }

                // Calculate the address to write to by adding the offset to the base address.
                IntPtr addressToWriteTo = IntPtr.Add(dllBaseAddress, offset);

                // Define the value to write (1 byte.)
                byte valueToWrite = 1;

                // Allocate a buffer with the value to write.
                byte[] buffer = [valueToWrite];

                // Write the value to the calculated address.
                bool result = WriteProcessMemory(processHandle, addressToWriteTo, buffer, buffer.Length, out int bytesWritten);

                if (result && bytesWritten == buffer.Length)
                {
                    StatusTextBlock.Text = "Status: Checkpoint successfully forced!";
                }
                else
                {
                    StatusTextBlock.Text = "Status: Checkpoint unsuccessfully forced.";
                    ShowErrorWindow("Checkpoint unsuccessfully forced (Failed to write to process memory.)");
                    return;
                }
            }
            catch (Exception ex)
            {
                ShowErrorWindow(ex.ToString());
                return;
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
                ForceCheckpoint("Halo 3", "halo3.dll", 0x20B86AC);
            }
            else if (gameSelected == "Halo 4")
            {
                ForceCheckpoint("Halo 4", "halo4.dll", 0x293DE2F);
            }
            else if (gameSelected == "Halo Reach")
            {
                ForceCheckpoint("Halo Reach", "haloreach.dll", 0x263EB2E);
            }
            else if (gameSelected == "Halo 3 ODST")
            {
                ForceCheckpoint("Halo 3 ODST", "halo3odst.dll", 0x20FF6BC);
            }
            else if (gameSelected == "Halo CE OG")
            {
                ForceCheckpoint("Halo CE OG", "halo.exe", 0x31973F);
            }
            else if (gameSelected == "Halo Custom Edition")
            {
                ForceCheckpoint("Halo Custom Edition", "haloce.exe", 0x2B47CF);
            }
            else if (gameSelected == "Halo 2 Vista")
            {
                ForceCheckpoint("Halo 2 Vista", "halo2.exe", 0x482250);
            }
            else
            {
                StatusTextBlock.Text = "Status: Please select a game first!";
                ShowErrorWindow("Select a game first.");
            }
        }

        private bool isRecordingInput = false;

        private void RecordInputButton_Click(object sender, RoutedEventArgs e)
        {
            // Toggle the recording state.
            isRecordingInput = !isRecordingInput;

            if (isRecordingInput)
            {
                KeyBindingTextBox.Text = string.Empty;

                // Start recording.
                KeyDown += OnKeyDownHandler;
                RecordInputButton.Content = "Stop Recording Input";
            }
            else
            {
                // Stop recording.
                KeyDown -= OnKeyDownHandler;
                RecordInputButton.Content = "Start Recording Input";
            }
        }

        private void OnKeyDownHandler(object sender, KeyEventArgs e)
        {
            // Find out what the key being pressed is.
            Key key = e.Key;

            // Append the key pressed to the TextBox.
            KeyBindingTextBox.Text += key.ToString();

            // Stop recording after the key is pressed.
            isRecordingInput = false;
            KeyDown -= OnKeyDownHandler;

            // Unregister the previous hotkey and register the new one.
            UnregisterCurrentHotkey();
            RegisterCurrentHotkey();

            RecordInputButton.Content = "Start Recording Input";
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

        private static int GetProcessIdByName(string processName)
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

        private static IntPtr GetModuleBaseAddress(int processId, string moduleName)
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
