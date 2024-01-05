using System.Diagnostics;
using System.Media;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace Force.Halo.Checkpoints
{
    public partial class MainWindow : Window
    {
        private readonly uint currentHotkey = 0;
        public string gameSelected = string.Empty;
        public string friendlyGameName = string.Empty;
        private readonly Thread inputThread;
        private readonly Thread antiCheatCheckThread;
        private ushort controllerButtonSelected = 0;
        public string controllerTriggerSelected = string.Empty;
        bool isControllerButtonSelected = false;
        bool isRecordControllerInputDone = true;
        bool isButtonCoolDownHappening = false;
        bool isProgramClosing = false;
        bool isErrorWindowOpen = false;
        int failureCount = 0;

        // I believe this it being set halfway depressed.
        const byte triggerThreshold = 128;

        // P/Invoke declarations for the hotkey hook, opening the process, reading/writing memory & and XInput.
        [DllImport("xinput1_4.dll")]
        public static extern int XInputGetState(int dwUserIndex, ref XInputState pState);

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

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private static LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using Process curProcess = Process.GetCurrentProcess();

            // Check if MainModule is not null before using it.
            ProcessModule? curModule = curProcess.MainModule ?? throw new InvalidOperationException("Main module could not be found.");

            // Now it's safe to use curModule.ModuleName, since we've checked if it's null.
            return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                GetModuleHandle(curModule.ModuleName), 0);
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                KBDLLHOOKSTRUCT kbStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                if (_instance != null && kbStruct.vkCode == _instance._currentHotkey)
                {
                    // Handle the key press
                    _instance.OnHotKeyPressed();
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        public static void InstallHook()
        {
            _hookID = SetHook(_proc);
        }

        public static void UninstallHook()
        {
            UnhookWindowsHookEx(_hookID);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct XInputState
        {
            public int dwPacketNumber;
            public XInputGamepad Gamepad;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct XInputGamepad
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        public const int XINPUT_GAMEPAD_DPAD_UP = 0x0001;
        public const int XINPUT_GAMEPAD_DPAD_DOWN = 0x0002;
        public const int XINPUT_GAMEPAD_DPAD_LEFT = 0x0004;
        public const int XINPUT_GAMEPAD_DPAD_RIGHT = 0x0008;
        public const int XINPUT_GAMEPAD_START = 0x0010;
        public const int XINPUT_GAMEPAD_BACK = 0x0020;
        public const int XINPUT_GAMEPAD_LEFT_THUMB = 0x0040;
        public const int XINPUT_GAMEPAD_RIGHT_THUMB = 0x0080;
        public const int XINPUT_GAMEPAD_LEFT_SHOULDER = 0x0100;
        public const int XINPUT_GAMEPAD_RIGHT_SHOULDER = 0x0200;
        public const int XINPUT_GAMEPAD_A = 0x1000;
        public const int XINPUT_GAMEPAD_B = 0x2000;
        public const int XINPUT_GAMEPAD_X = 0x4000;
        public const int XINPUT_GAMEPAD_Y = 0x8000;

        public MainWindow()
        {
            InitializeComponent();
            _instance = this;

            // For printing the version number.
            DataContext = this;

            InstallHook();

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

            if (Properties.Settings.Default.IsControllerButtonSelectedPreference == true)
            {
                isControllerButtonSelected = true;
                isRecordControllerInputDone = true;
                controllerButtonSelected = Properties.Settings.Default.ControllerButtonPreference;
                controllerTriggerSelected = Properties.Settings.Default.ControllerTriggerPreference;
                ControllerButtonBindingTextBlock.Text = Properties.Settings.Default.ControllerButtonPreferenceString;
            }

            // Start the input check on a background thread.
            inputThread = new Thread(CheckControllerInput);
            inputThread.IsBackground = true;
            inputThread.Start();

            antiCheatCheckThread = new Thread(CheckForAntiCheatRunning);
            antiCheatCheckThread.IsBackground = true;
            antiCheatCheckThread.Start();
        }

        private void CheckForAntiCheatRunning()
        {
            while (isProgramClosing == false)
            {
                if (gameSelected != "Halo 2 Vista" && gameSelected != "Halo Custom Edition" && gameSelected != "Halo CE OG")
                {
                    string processName = "EasyAntiCheat";
                    int processId = GetProcessIdByName(processName);

                    if (processId != -1)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            ForceCheckpointButton.Content = "Easy Anti-Cheat is running in the MCC!";
                            ForceCheckpointButton.IsEnabled = false;
                        });
                    }
                    else
                    {
                        Dispatcher.Invoke(() =>
                        {
                            ForceCheckpointButton.Content = "Force Checkpoint";
                            ForceCheckpointButton.IsEnabled = true;
                        });
                    }
                }
                else if (gameSelected == "Halo Custom Edition" || gameSelected == "Halo CE OG")
                {
                    Dispatcher.Invoke(() =>
                    {
                        ForceCheckpointButton.Content = "Force Checkpoint";
                        ForceCheckpointButton.IsEnabled = true;
                    });
                }
                else
                {
                    string processName = "halo2";
                    int processId = GetProcessIdByName(processName);

                    if (processId != -1)
                    {
                        const int PROCESS_WM_READ = 0x0010;
                        const int PROCESS_VM_OPERATION = 0x0008;
                        string dllName = "xlive.dll";
                        int offset = 0x4E68FF;
                        try
                        {
                            IntPtr processHandle = OpenProcess(PROCESS_WM_READ | PROCESS_VM_OPERATION, false, processId);
                            IntPtr dllBaseAddress = GetModuleBaseAddress(processId, dllName);

                            if (dllBaseAddress == IntPtr.Zero)
                            {
                                ShowErrorWindow($"Failed to find the base address of {dllName}.");
                                return;
                            }

                            IntPtr addressToCheck = IntPtr.Add(dllBaseAddress, offset);
                            byte[] buffer = new byte[1];

                            bool result = ReadProcessMemory(processHandle, addressToCheck, buffer, buffer.Length, out int bytesRead);

                            if (result && bytesRead == buffer.Length)
                            {
                                byte valueRead = buffer[0];

                                if (valueRead != 0)
                                {
                                    Dispatcher.Invoke(() =>
                                    {
                                        // Thanks for nothing, WPF.
                                        ForceCheckpointButton.Content = "Anti-Cheat is running in Silent Cartographer!\r\n                          (Halo 2: Vista)";
                                        ForceCheckpointButton.IsEnabled = false;
                                    });
                                }
                                else
                                {
                                    Dispatcher.Invoke(() =>
                                    {
                                        ForceCheckpointButton.Content = "Force Checkpoint";
                                        ForceCheckpointButton.IsEnabled = true;
                                    });
                                }
                            }
                            else
                            {
                                ShowErrorWindow("Failed to read memory.");
                            }
                        }
                        catch (Exception ex)
                        {
                            failureCount++;

                            if (failureCount < 3)
                            {
                                // Give 2 Halo 2 Vista in all its old, terribleness, a few seconds to breathe.
                                Thread.Sleep(1000);
                            }
                            else
                            {
                                ShowErrorWindow("There's an issue with your copy of Halo 2: Vista. Here's the full error message: " + ex.Message);
                                gameSelected = string.Empty;
                                StatusTextBlock.Text = "Status: Issue with H2V.";
                            }
                        }
                    }
                    else
                    {
                        failureCount = 0;

                        Dispatcher.Invoke(() =>
                        {
                            ForceCheckpointButton.Content = "Force Checkpoint";
                            ForceCheckpointButton.IsEnabled = true;
                        });
                    }
                }
            }
        }

        private async void CheckControllerInput()
        {
            while (true)
            {
                if (!isButtonCoolDownHappening)
                {

                    if (isControllerButtonSelected && isRecordControllerInputDone)
                    {
                        XInputState state = new();
                        int result = XInputGetState(0, ref state); // 0 is the first controller.

                        if (result == 0) // Controller is connected.
                        {
                            if ((state.Gamepad.wButtons & controllerButtonSelected) != 0)
                            {
                                _ = Dispatcher.Invoke(() =>
                                {
                                    ForceCheckpointButton_Click(this, new RoutedEventArgs());
                                    return Task.CompletedTask;
                                });
                            }
                            // Triggers need to be handled separately.
                            else if (controllerTriggerSelected == "Left Trigger")
                            {
                                if (state.Gamepad.bLeftTrigger > triggerThreshold)
                                {
                                    Dispatcher.Invoke(() =>
                                    {
                                        ForceCheckpointButton_Click(this, new RoutedEventArgs());
                                    });
                                }
                            }
                            else if (controllerTriggerSelected == "Right Trigger")
                            {
                                if (state.Gamepad.bRightTrigger > triggerThreshold)
                                {
                                    Dispatcher.Invoke(() =>
                                    {
                                        ForceCheckpointButton_Click(this, new RoutedEventArgs());
                                    });
                                }
                            }
                        }
                        // Sleep to prevent high CPU usage.
                        Thread.Sleep(100);
                    }
                }
                // Prevent the user from triggering a checkpoint while selecting their hotkeys. Wait 2 seconds.
                else
                {
                    Dispatcher.Invoke(() => // Gotta use these to play around with the UI.
                    {
                        RecordControllerInputButton.Content = "Please wait 2 seconds.";
                        RecordControllerInputButton.IsEnabled = false;
                    });

                    await Task.Delay(2000);
                    isButtonCoolDownHappening = false;

                    Dispatcher.Invoke(() =>
                    {
                        RecordControllerInputButton.Content = "Start Recording Input";
                        RecordControllerInputButton.IsEnabled = true;
                    });
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            isProgramClosing = true;

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

            if (isControllerButtonSelected)
            {
                Properties.Settings.Default.IsControllerButtonSelectedPreference = true;
                Properties.Settings.Default.ControllerButtonPreference = controllerButtonSelected;
                Properties.Settings.Default.ControllerTriggerPreference = controllerTriggerSelected;
                Properties.Settings.Default.ControllerButtonPreferenceString = ControllerButtonBindingTextBlock.Text;
            }

            Properties.Settings.Default.Save();

            if (_hookID != IntPtr.Zero)
            {
                UninstallHook();
            }
            _instance = null;
            base.OnClosed(e);
        }

        private void ShowErrorWindow(string errorMessage)
        {
            isErrorWindowOpen = true;
            ErrorWindow errorWindow = new ErrorWindow();
            errorWindow.ErrorTextBlock.Text = errorMessage;
            errorWindow.Owner = this;
            SystemSounds.Exclamation.Play();
            errorWindow.ShowDialog();
            isErrorWindowOpen = false;
        }

        private void ShowUpdateWindow(string errorMessage, string customTitle)
        {
            isErrorWindowOpen = true;
            ErrorWindow errorWindow = new ErrorWindow();
            errorWindow.ErrorTextBlock.Text = errorMessage;
            errorWindow.Owner = this;
            errorWindow.Title = customTitle;
            SystemSounds.Exclamation.Play();
            errorWindow.ShowDialog();
            isErrorWindowOpen = false;
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
            string latestReleaseUrl = "https://api.github.com/repos/Jestzer/Force.Halo.Checkpoints/releases/latest";

            // Use HttpClient to fetch the latest release data.
            using HttpClient client = new();

            // GitHub API requires a user-agent. I'm adding the extra headers to reduce HTTP error 403s.
            client.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("Force.Halo.Checkpoints", PackageVersion));
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
                    Version latestVersion = new(latestVersionString);

                    // Compare the current version with the latest version.
                    if (currentVersion.CompareTo(latestVersion) < 0)
                    {
                        // A newer version is available!
                        ErrorWindow errorWindow = new();
                        errorWindow.Owner = this;
                        errorWindow.ErrorTextBlock.Text = "";
                        errorWindow.URLTextBlock.IsEnabled = true;
                        errorWindow.URLTextBlock.Visibility = Visibility.Visible;
                        errorWindow.Title = "Check for updates";
                        errorWindow.ShowDialog();
                        errorWindow.URLTextBlock.IsEnabled = false;
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
            string hotkeyName = KeyBindingTextBox.Text.ToUpper();

            // Convert the key name to a Key enumeration, so it can actually be used.
            try
            {
                Key key = (Key)Enum.Parse(typeof(Key), hotkeyName);
                _currentHotkey = (uint)KeyInterop.VirtualKeyFromKey(key);
            }
            catch
            {
                KeyBindingTextBox.Text = string.Empty;
                ShowErrorWindow($"You cannot use that key as your hotkey.");
                UnregisterCurrentHotkey();
                return;
            }

            // Set up the hook if it's somehow not already set.
            if (_hookID == IntPtr.Zero)
            {
                InstallHook();
            }
        }
        private void UnregisterCurrentHotkey()
        {
            _currentHotkey = 0;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _ = PresentationSource.FromVisual(this) as HwndSource;

            if (Properties.Settings.Default.HotKeyPreference != string.Empty)
            {
                RegisterCurrentHotkey();
            }
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

        private void ForceCheckpointWithEnterKey(object sender, KeyEventArgs e)
        {
            if (ForceCheckpointButton.IsEnabled)
            {
                if (e.Key == Key.Enter)
                {
                    ForceCheckpointButton_Click(sender, e);
                }
            }
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
            GameSelectedLabel.Content = "Game selected: Halo: CE (not MCC)";
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

        private void CheckIfGameIsRunning(string gameSelected, out bool gameIsRunning)
        {
            gameIsRunning = true;

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

                if (processId == -1 && processName == "MCC-Win64-Shipping")
                {
                    ShowErrorWindow("Halo: The Master Chief Collection is not running.");
                    gameIsRunning = false;
                    return;
                }
                else if (processId == -1)
                {
                    ShowErrorWindow($"{friendlyGameName} is not running.");
                    gameIsRunning = false;
                    return;
                }
            }
            catch (Exception ex)
            {
                ShowErrorWindow(ex.Message);
            }
        }
        private void CheckIfMCCGameIsRunning(string gameSelected, string dllName, int offset, string expectedValue, out bool mccGameIsRunning)
        {
            mccGameIsRunning = true;
            try
            {
                const int PROCESS_WM_READ = 0x0010;
                const int PROCESS_VM_OPERATION = 0x0008;
                string processName = "MCC-Win64-Shipping";
                int processId = GetProcessIdByName(processName);

                IntPtr processHandle = OpenProcess(PROCESS_WM_READ | PROCESS_VM_OPERATION, false, processId);
                IntPtr dllBaseAddress = GetModuleBaseAddress(processId, dllName);

                if (dllBaseAddress == IntPtr.Zero)
                {
                    ShowErrorWindow($"{gameSelected} is not running in the MCC.");
                    mccGameIsRunning = false;
                    return;
                }

                byte[] buffer = new byte[4];

                IntPtr addressToCheck = IntPtr.Add(dllBaseAddress, offset);
                if (gameSelected == "Halo Reach" || gameSelected == "Halo 2" || gameSelected == "Halo 3" || gameSelected == "Halo 4")
                {
                    buffer = new byte[1];
                }
                else if (gameSelected == "Halo CE")
                {
                    buffer = new byte[5];
                }
                else if (gameSelected == "Halo 3 ODST")
                {
                    // Use the default.
                }

                bool result = ReadProcessMemory(processHandle, addressToCheck, buffer, buffer.Length, out int bytesRead);

                if (result && bytesRead == buffer.Length)
                {
                    if (gameSelected == "Halo Reach" || gameSelected == "Halo 2" || gameSelected == "Halo 3" || gameSelected == "Halo 4")
                    {
                        byte valueRead = buffer[0];

                        if (valueRead == 0)
                        {
                            mccGameIsRunning = false;
                            Dispatcher.Invoke(() =>
                            {
                                ShowErrorWindow($"{gameSelected} is not running in the MCC.");
                            });
                            return;
                        }
                    }
                    else
                    {
                        string valueRead = System.Text.Encoding.ASCII.GetString(buffer);

                        if (valueRead != expectedValue)
                        {
                            mccGameIsRunning = false;
                            Dispatcher.Invoke(() =>
                            {
                                ShowErrorWindow($"{gameSelected} is not running in the MCC.");
                            });
                            return;
                        }
                    }
                }
                else
                {
                    ShowErrorWindow("Failed to read memory.");
                    mccGameIsRunning = false;
                    return;
                }
            }
            catch (Exception ex)
            {
                ShowErrorWindow(ex.Message);
            }
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

                if (processId == -1 && processName == "MCC-Win64-Shipping")
                {
                    ShowErrorWindow($"Halo: The Master Chief Collection is not running.");
                    return;
                }
                else if (processId == -1)
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
            if (isErrorWindowOpen == false)
            {
                if (ForceCheckpointButton.IsEnabled == true)
                {
                    if (gameSelected == "Halo CE")
                    {
                        CheckIfGameIsRunning("MCC", out bool gameIsRunning);
                        if (gameIsRunning)
                        {
                            CheckIfMCCGameIsRunning("Halo CE", "halo1.dll", 0x2DCF80B, "Halo1", out bool mccGameIsRunning);
                            if (mccGameIsRunning)
                            {
                                ForceCheckpoint("Halo CE", "halo1.dll", 0x2B23707);
                            }
                        }
                    }
                    else if (gameSelected == "Halo 2")
                    {
                        CheckIfGameIsRunning("MCC", out bool gameIsRunning);
                        if (gameIsRunning)
                        {
                            CheckIfMCCGameIsRunning("Halo 2", "mss64dsp.flt", 0x24690, "1", out bool mccGameIsRunning);
                            if (mccGameIsRunning)
                            {
                                ForceCheckpoint("Halo 2", "halo2.dll", 0xE6FD7E);
                            }
                        }
                    }
                    else if (gameSelected == "Halo 3")
                    {

                        CheckIfGameIsRunning("MCC", out bool gameIsRunning);
                        if (gameIsRunning)
                        {
                            CheckIfMCCGameIsRunning("Halo 3", "halo3.dll", 0x1FC56C4, "1", out bool mccGameIsRunning);
                            if (mccGameIsRunning)
                            {
                                ForceCheckpoint("Halo 3", "halo3.dll", 0x20B86AC);
                            }
                        }
                    }
                    else if (gameSelected == "Halo 4")
                    {

                        CheckIfGameIsRunning("MCC", out bool gameIsRunning);
                        if (gameIsRunning)
                        {
                            CheckIfMCCGameIsRunning("Halo 4", "halo4.dll", 0xE3B005, "1", out bool mccGameIsRunning);
                            if (mccGameIsRunning)
                            {
                                ForceCheckpoint("Halo 4", "halo4.dll", 0x293DE2F);
                            }
                        }
                    }
                    else if (gameSelected == "Halo Reach")
                    {
                        CheckIfGameIsRunning("MCC", out bool gameIsRunning);
                        if (gameIsRunning)
                        {
                            CheckIfMCCGameIsRunning("Halo Reach", "xaudio2_9.DLL", 0x889E2, "1", out bool mccGameIsRunning);
                            if (mccGameIsRunning)
                            {
                                ForceCheckpoint("Halo Reach", "haloreach.dll", 0x263EB2E);
                            }
                        }
                    }
                    else if (gameSelected == "Halo 3 ODST")
                    {
                        CheckIfGameIsRunning("MCC", out bool gameIsRunning);
                        if (gameIsRunning)
                        {
                            CheckIfMCCGameIsRunning("Halo 3 ODST", "halo3odst.dll", 0x2174F43, "ODST", out bool mccGameIsRunning);
                            if (mccGameIsRunning)
                            {
                                ForceCheckpoint("Halo 3 ODST", "halo3odst.dll", 0x20FF6BC);
                            }
                        }
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
                else
                {
                    if (gameSelected == "Halo 2 Vista")
                    {
                        ShowErrorWindow("Anti-Cheat is running!");
                    }
                    else
                    {
                        ShowErrorWindow("Easy Anti-Cheat is running!");
                    }
                }
            }
        }

        private bool isRecordingInput = false;
        private uint _currentHotkey;
        private static MainWindow? _instance;

        private void RecordInputButton_Click(object sender, RoutedEventArgs e)
        {
            // Prevent accidental checkpoint trigger.
            UnregisterCurrentHotkey();

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

        private readonly Dictionary<ushort, string> controllerButtonMappings = new Dictionary<ushort, string>
            {
                { XINPUT_GAMEPAD_DPAD_UP, "DPad Up" },
                { XINPUT_GAMEPAD_DPAD_DOWN, "DPad Down" },
                { XINPUT_GAMEPAD_DPAD_LEFT, "DPad Left" },
                { XINPUT_GAMEPAD_DPAD_RIGHT, "DPad Right" },
                { XINPUT_GAMEPAD_START, "Start" },
                { XINPUT_GAMEPAD_BACK, "Back" },
                { XINPUT_GAMEPAD_LEFT_THUMB, "Left Thumbstick" },
                { XINPUT_GAMEPAD_RIGHT_THUMB, "Right Thumbstick" },
                { XINPUT_GAMEPAD_LEFT_SHOULDER, "Left Bumper" },
                { XINPUT_GAMEPAD_RIGHT_SHOULDER, "Right Bumper" },
                { XINPUT_GAMEPAD_A, "A" },
                { XINPUT_GAMEPAD_B, "B" },
                { XINPUT_GAMEPAD_X, "X" },
                { XINPUT_GAMEPAD_Y, "Y" }
            };

        private CancellationTokenSource? cancellationTokenSource;
        private bool isRecordingControllerInput = false;
        private Thread? controllerInputCheckThread;

        private void RecordControllerInputButton_Click(object sender, RoutedEventArgs e)
        {
            if (!isRecordingControllerInput)
            {
                RecordControllerInputButton.Content = "Stop Recording Input";
                isRecordingControllerInput = true;

                cancellationTokenSource = new CancellationTokenSource();
                controllerInputCheckThread = new Thread(() => RecordControllerInput(cancellationTokenSource.Token))
                {
                    IsBackground = true
                };
                controllerInputCheckThread.Start();
            }
            else
            {
                // Signal the thread to stop.
                cancellationTokenSource?.Cancel();
                isRecordingControllerInput = false;
                RecordControllerInputButton.Content = "Start Recording Input";
            }
        }

        private void RecordControllerInput(CancellationToken token)
        {
            isControllerButtonSelected = false;
            isRecordControllerInputDone = false;
            isButtonCoolDownHappening = false;

            while (!isControllerButtonSelected && !token.IsCancellationRequested)
            {
                XInputState state = new();
                int result = XInputGetState(0, ref state);

                if (result == 0)
                {
                    foreach (var buttonMapping in controllerButtonMappings)
                    {
                        if ((state.Gamepad.wButtons & buttonMapping.Key) != 0)
                        {
                            ushort buttonValue = buttonMapping.Key;
                            string buttonName = buttonMapping.Value;
                            Dispatcher.Invoke(() =>
                            {
                                controllerButtonSelected = buttonValue;
                                ControllerButtonBindingTextBlock.Text = buttonName;
                                controllerTriggerSelected = string.Empty;
                                isControllerButtonSelected = true;
                                isButtonCoolDownHappening = true;
                            });
                            break;
                        }
                    }

                    // Triggers aren't in the dictionary because they aren't boolean values.
                    if (state.Gamepad.bLeftTrigger > triggerThreshold)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            controllerButtonSelected = 0;
                            controllerTriggerSelected = "Left Trigger";
                            ControllerButtonBindingTextBlock.Text = controllerTriggerSelected;
                            isControllerButtonSelected = true;
                            isButtonCoolDownHappening = true;
                        });
                        break;
                    }

                    if (state.Gamepad.bRightTrigger > triggerThreshold)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            controllerButtonSelected = 0;
                            controllerTriggerSelected = "Right Trigger";
                            ControllerButtonBindingTextBlock.Text = controllerTriggerSelected;
                            isControllerButtonSelected = true;
                            isButtonCoolDownHappening = true;
                        });
                        break;
                    }

                    if (token.IsCancellationRequested)
                    {
                        break;
                    }
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        ShowErrorWindow("Your controller is either disconnected or unsupported.");
                        RecordControllerInputButton.Content = "Start Recording Input";
                    });
                    cancellationTokenSource?.Cancel();
                    isRecordingControllerInput = false;
                    break;
                }
            }
            Dispatcher.Invoke(() =>
            {
                // If a button was selected, reset the recording flag.
                if (isControllerButtonSelected)
                {
                    isRecordingControllerInput = false;
                    RecordControllerInputButton.Content = "Please wait 2 seconds.";
                }
                else if (controllerButtonSelected != 0)
                {
                    isControllerButtonSelected = true;
                }
                isRecordControllerInputDone = true;
            });
        }
    }
}
