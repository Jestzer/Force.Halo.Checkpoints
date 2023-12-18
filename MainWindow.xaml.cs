using System.Runtime.InteropServices;
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

        private void ForceCheckpointButton_Click(object sender, RoutedEventArgs e)
        {
            // For testing.
            MessageBox.Show("Hi there.");
            int bytesWritten;
            if (gameSelected == "Halo CE")
            {
                try
                {
                    byte[] buffer;
                    buffer = [1];
                    if (WriteProcessMemory(HCMGlobal.GlobalProcessHandle, FindPointerAddress(HCMGlobal.GlobalProcessHandle, HCMGlobal.BaseAddress, HCMGlobal.LoadedOffsets.H1_CoreSave[Convert.ToInt32(HCMGlobal.WinFlag)]), buffer, buffer.Length, out bytesWritten))
                    {
                        MessageBox.Show("yay");
                    }
                    else
                    {
                        MessageBox.Show("sorry");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.ToString());
                }
            }
            else if (gameSelected == "Halo 2")
            {

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
        public static class HCMGlobal
        {
            public static Offsets LoadedOffsets;
            public static bool WinFlag = false; //false = Steam, used for knowing which offsets to use.
            public static IntPtr GlobalProcessHandle;
            public static IntPtr BaseAddress;
        }

        public class Offsets
        {
            //offsets are gonna be stored as 2-unit arrays, first position is winstore, second is Steam
            //that way when we're calling them from elsewhere we can just call Offsets.WhateverOffset[HCMGlobal.WinFlag] and it'll give us the one we want
            //the units will themselves be arbitary length arrays (each position for each offset in a multi-level pointer)

            //the actual values will be populated from the json file corrosponding to the attached mcc version

            //general
            public int[][] gameindicatormagic; //renamed from gameindicator > gameindicatormagicmagic. This is to 
                                               //deliberately break HCM versions before 0.9.3 since if they recieve
                                               //the 2028 offsets, they'll write junk data to cpmessage calls and crash MCC
            public int[][] menuindicator;
            public int[][] stateindicator;

            //h1
            public int[][] H1_LevelName; //45 33 c0 48 8b 50 28 8b 12 non writeable, third/last result, scroll up to mov that writes 01. that writes to revert
            public int[][] H1_CoreSave;
            public int[][] H1_CoreLoad;
            public int[][] H1_CheckString;
            public int[][] H1_TickCounter; //b0 01 4c 8b 7c 24 20 4c 8b 74 24 28. inc right above.
            public int[][] H1_Message; //8b 48 0c 89 0b. 89 0b writes to it (only when getting a message)

            public byte[] H1_MessageCode; //
            public int H1_CPData_LevelCode;
            public int H1_CPData_StartTick;
            public int H1_CPData_Difficulty;
            public int H1_CPData_Size;




            //hr
            public int[][] HR_LevelName; //0x3FB3D9 ahead of checkpoint
            public int[][] HR_CheckString;
            public int[][] HR_Checkpoint; //4a 8b 14 3b b8 a8 00 00 00, mov right above
            public int[][] HR_Revert; //rel
            public int[][] HR_DRflag; //b8 50 00 00 00 ** ** ** ** ** ** 48 8b 04 18, it's the ** when you get a cp
            public int[][] HR_CPLocation; //just scan lol
            public int[][] HR_LoadedSeed; //45 33 ff 84 db, take first. the mov above writes to a byte that's 0x2B before the loaded seed
            public int[][] HR_TickCounter; //8b c7 48 8b 0c 18 44 01 61 0c. the add writes to it.
            public int[][] HR_Message; //4B 8b 04 2e 8b 48 0c 89 4b 04. last mov writes to it (messageTC) when getting message.
            public int[][] HR_MessageCall; //48 89 6c 24 20 41 b9 01 00 00 00. should have "checkpoint save" next to it in disassembly. the call 7 instructions above the cp save is the cp message call ; E8 67 46 28 00


            public byte[] HR_MessageCode;
            public int HR_CPData_LevelCode;
            public int HR_CPData_StartTick;
            public int HR_CPData_Difficulty;
            public int HR_CPData_Seed;
            public int HR_CPData_DROffset1;
            public int HR_CPData_DROffset2;
            public int[] HR_CPData_SHA;
            public int HR_CPData_Size;
            public int[][] HR_CPData_PreserveLocations;



            public int[][] H2_LevelName; //take second last oldmombassa from; load outskirts then scan for, WRITEABLE: 30 33 61 5F 6F 6C 64 6D 6F 6D 62 61 73 61 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 73 63 65 6E 61 72 69 6F 73 5C 73 6F 6C 6F 5C 30 33 61 5F 6F 6C 64 6D 6F 6D 62 61 73
            public int[][] H2_CheckString;
            public int[][] H2_Checkpoint; //rel
            public int[][] H2_Revert; //bf ff ff ff ff ** ** 33 c9, the cmp immediately above accesses it
            public int[][] H2_DRflag; //c1 e0 05 8b d8 d1 ef 83 cb 40. movzx below accesses it on revert.
            public int[][] H2_CPLocation; //just scan lol
            public int[][] H2_CPLocation2; //just scan lol
            public int[][] H2_TickCounter; //f3 0f 5d c6 0f 28 74 24 40 40 84 f6, it's a mov like 10 instr below
            public int[][] H2_Message; // the 89 03 (mov [rbx], eax), above c6 83 82 00 00 00 01 66 c7
            public int[][] H2_MessageCall; //48 8b 10 ff 52 58 48 8b c8 48 8b 10, go a little down to the call that immd precedes a mov and cmp. E8 57 CA 18 00
            public int[][] H2_LoadedBSP1; //scan for writable, unicode, case sens "halo2\h2_m". two of the results will have "untracked version" a little above". 0x1F8 before the halo2 string is the bsp bytes.
            public int[][] H2_LoadedBSP2;


            public byte[] H2_MessageCode;
            public int H2_CPData_LevelCode;
            public int H2_CPData_StartTick;
            public int H2_CPData_Difficulty;
            public int[] H2_CPData_BSP;
            public int H2_CPData_DROffset1;
            public int H2_CPData_DROffset2;
            public int H2_CPData_Size;
            public int[][] H2_CPData_PreserveLocations;


            public int[][] H3_LevelName; //load up ark easy, scan for writeable 64 61 65 68 0B 00 00 00 00 E0 D4 22 00 00 00 00 84 D6 F5 8A 01 00 00 00 00 E0 9E 1C 00 00 36 06
            public int[][] H3_CheckString;
            public int[][] H3_Checkpoint; //rel
            public int[][] H3_Revert; //first halo3 result of 4c 8d 5c 24 60 49 8b 5b 10 49 8b 73 18 49 8b 7b 20 4d 8b 73 28, it's the mov below it
            public int[][] H3_DRflag; //ff c0 ** ** ** ** ** ** 99 b9 00 02 00 00. it's the mov edx when getting a checkpoint.
            public int[][] H3_CPLocation; //just scan lol
            public int[][] H3_TickCounter; //48 8b 04 c8 48 8b 0c 10 8b 59 0c,, movs below point to it
            public int[][] H3_Message; //4a 8b 0c c0 48 8b 04 31 8b 48 0c, first result, mov ecx writes to messageTC when getting cp
            public int[][] H3_MessageCall; //48 83 ec 68 33 c9  call immediately below. E8 31 DF 1F 00
            public int[][] H3_LoadedBSP1; //load into ark. two last results of scan for writeable 01 00 00 00 00 00 00 00 C0 09 68 14 01 10 00 00 01 10 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00
            public int[][] H3_LoadedBSP2;


            public byte[] H3_MessageCode;
            public int H3_CPData_LevelCode;
            public int H3_CPData_StartTick;
            public int H3_CPData_Difficulty;
            public int[] H3_CPData_BSP;
            public int[] H3_CPData_SHA;
            public int H3_CPData_DROffset1;
            public int H3_CPData_DROffset2;
            public int H3_CPData_Size;
            public int[][] H3_CPData_PreserveLocations;


            public int[][] OD_LevelName;
            public int[][] OD_CheckString;
            public int[][] OD_Checkpoint;
            public int[][] OD_Revert;
            public int[][] OD_DRflag;
            public int[][] OD_CPLocation;
            public int[][] OD_TickCounter;
            public int[][] OD_Message;
            public int[][] OD_MessageCall;
            public int[][] OD_LoadedBSP1;
            public int[][] OD_LoadedBSP2;

            public byte[] OD_MessageCode;
            public int OD_CPData_LevelCode;
            public int OD_CPData_StartTick;
            public int OD_CPData_Difficulty;
            public int[] OD_CPData_BSP;
            public int[] OD_CPData_SHA;
            public int OD_CPData_DROffset1;
            public int OD_CPData_DROffset2;
            public int OD_CPData_Size;
            public int[][] OD_CPData_PreserveLocations;

            public int[][] H4_LevelName;
            public int[][] H4_CheckString;
            public int[][] H4_Checkpoint;
            public int[][] H4_Revert;
            public int[][] H4_DRflag;
            public int[][] H4_CPLocation;
            public int[][] H4_TickCounter;
            public int[][] H4_Message;
            public int[][] H4_MessageCall;


            public byte[] H4_MessageCode;
            public int H4_CPData_LevelCode;
            public int H4_CPData_StartTick;
            public int H4_CPData_Difficulty;
            public int H4_CPData_DROffset1;
            public int H4_CPData_DROffset2;
            public int H4_CPData_Size;
            public int[][] H4_CPData_PreserveLocations;
        }
    }
}
