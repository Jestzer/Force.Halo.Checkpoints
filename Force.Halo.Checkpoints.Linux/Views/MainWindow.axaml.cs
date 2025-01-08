using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Avalonia.Threading;

namespace Force.Halo.Checkpoints.Linux.Views;

public partial class MainWindow : Window
{
    private string _gameSelected = "none";

    private const int PtracePeekdata = 2;
    private const int PtracePokedata = 5;
    private const int PtraceAttach = 16;
    private const int PtraceDetach = 17;

    [DllImport("libc", SetLastError = true)]
    private static extern int ptrace(int request, int pid, IntPtr addr, IntPtr data);

    public MainWindow()
    {
        InitializeComponent();
    }

    private void ShowErrorWindow(string errorMessage)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            ErrorWindow errorWindow = new();
            errorWindow.ErrorTextBlock.Text = errorMessage;

            if (VisualRoot is Window window)
            {
                await errorWindow.ShowDialog(window);
            }
            else
            {
                Console.Write("The error window broke somehow. Please submit an Issue for this in GitHub.");
            }
        });
    }

    private void ModifyGameMemory(string gameName, string processName, string moduleName, long offset, byte value)
    {
        int pid = GetProcessIdByName(processName);
        if (pid == -1)
        {
            ShowErrorWindow($"{gameName} does not appear to be running.");
            return;
        }

        IntPtr baseAddress = GetModuleBaseAddress(pid, moduleName);
        if (baseAddress == IntPtr.Zero)
        {
            ShowErrorWindow($"Failed to find the base address of {moduleName}.");
            ptrace(PtraceDetach, pid, IntPtr.Zero, IntPtr.Zero);
            return;
        }

        IntPtr targetAddress = IntPtr.Add(baseAddress, (int)offset);

        int attachAttempts = 1;
        while (attachAttempts < 1000)
        {
            if (ptrace(PtraceAttach, pid, IntPtr.Zero, IntPtr.Zero) == -1)
            {
                attachAttempts++;
                ptrace(PtraceDetach, pid, IntPtr.Zero, IntPtr.Zero);
                Thread.Sleep(5);
            }
            else
            {
                break;
            }
        }

        if (attachAttempts == 1000)
        {
            ShowErrorWindow(
                $"Failed to attach to process {pid}. Ensure this program is running as root/sudo. Number of attempts reached: {attachAttempts}.");
            ptrace(PtraceDetach, pid, IntPtr.Zero, IntPtr.Zero);
            return;
        }

        try
        {
            // For whatever reason, it seems that the first attempt tends to fail. I don't know why it's an issue on Linux, but it seems...
            // ... reattempting fixes the issue. I've seen it take up to 15 attempts, so I think it's safe to call quits at 30.
            int attempts = 1;
            bool successfullyWroteToMemory = false;
            while (attempts < 60 && !successfullyWroteToMemory)
            {
                IntPtr valuePtr = new(value);
                if (ptrace(PtracePokedata, pid, targetAddress, valuePtr) != -1)
                {
                    successfullyWroteToMemory = true;
                    break;
                }

                attempts++;
                if (attempts >= 60)
                {
                    ShowErrorWindow("Failed to force the checkpoint after 60 attempts. " +
                                    "Make sure the game is running and you are running this program as root/sudo.");
                    ptrace(PtraceDetach, pid, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
            }

            if (successfullyWroteToMemory)
            {
                ShowErrorWindow($"Checkpoint successfully forced! Number of attempts it to succeed: {attempts}.");
            }
        }
        finally
        {
            _ = ptrace(PtraceDetach, pid, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private static int GetProcessIdByName(string processName)
    {
        int
            lastPid = -1; // For whatever reason, this seems to be reliable for Proton. Don't know about anything else. I should probably find a better method.
        string[] procEntries = Directory.GetDirectories("/proc");

        foreach (string entry in procEntries)
        {
            if (!int.TryParse(Path.GetFileName(entry), out int pid)) continue;
            try
            {
                string cmdline = File.ReadAllText($"/proc/{pid}/cmdline");
                if (cmdline.Contains(processName))
                {
                    lastPid = pid;
                }
            }
            catch
            {
                // Ignore processes we can't read and hope it's not important. :)
            }
        }

        return lastPid;
    }

    private static IntPtr GetModuleBaseAddress(int pid, string moduleName)
    {
        string mapsPath = $"/proc/{pid}/maps";
        if (!File.Exists(mapsPath))
        {
            return IntPtr.Zero;
        }

        var lines = File.ReadAllLines(mapsPath);
        foreach (var line in lines)
        {
            if (!line.Contains(moduleName)) continue;
            var parts = line.Split('-');
            if (parts.Length > 0 && long.TryParse(parts[0], System.Globalization.NumberStyles.HexNumber, null,
                    out long address))
            {
                return new IntPtr(address);
            }
        }

        return IntPtr.Zero;
    }

    private void ForceCheckpointButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_gameSelected == "none")
        {
            ShowErrorWindow("No game selected.");
        }
        else
        {
            try
            {
                if (_gameSelected == "OGHaloCE")
                {
                    ModifyGameMemory("Halo CE (non-MCC)", "halo.exe", "halo.exe", 0x31973F, 1);
                }
                else if (_gameSelected == "MCC")
                {
                    string gameName = CheckWhichMCCGameIsRunning();
                    switch (gameName)
                    {
                        case "Halo CE":
                            ModifyGameMemory("MCC", "MCC-Win64-Shipping.exe", "halo1.dll", 0x2B23707, 1);
                            break;
                        case "Halo 2":
                            ModifyGameMemory("MCC", "MCC-Win64-Shipping.exe", "halo2.dll", 0xE6FD7E, 1);
                            break;
                        case "Halo 3":
                            ModifyGameMemory("MCC", "MCC-Win64-Shipping.exe", "halo3.dll", 0x20B86AC, 1);
                            break;
                        case "Halo 4":
                            ModifyGameMemory("MCC", "MCC-Win64-Shipping.exe", "halo4.dll", 0x293DE2F, 1);
                            break;
                        case "Halo 3: ODST":
                            ModifyGameMemory("MCC", "MCC-Win64-Shipping.exe", "halo3odst.dll", 0x20FF6BC, 1);
                            break;
                        case "Halo: Reach":
                            ModifyGameMemory("MCC", "MCC-Win64-Shipping.exe", "haloreach.dll", 0x263EB2E, 1);
                            break;
                        case "none":
                            break;
                        default:
                            ShowErrorWindow($"What's the meaning of this? Who are you? How did you get in here?");
                            return;
                    }
                }
            }
            catch (Exception ex)
            {
                ShowErrorWindow($"Failed to force checkpoint: {ex.Message}");
            }
        }
    }

    private void HaloCEButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _gameSelected = "OGHaloCE";
    }

    private void MCCButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _gameSelected = "MCC";
    }

    private string CheckWhichMCCGameIsRunning()
    {
        int attempts = 1;
        int pid = GetProcessIdByName("MCC-Win64-Shipping.exe");
        if (pid == -1)
        {
            ShowErrorWindow($"The Master Chief Collection does not appear to be running.");
            return "none";
        }
        
        while (attempts < 31)
        {
            int gameNumber = 0;
            while (gameNumber < 6)
            {
                gameNumber++;
                string gameName = "gameNameEmpty";
                string moduleName = "moduleNameEmpty";
                int offset = 0;
                int expectedIntValue = 1;

                switch (gameNumber)
                {
                    case 1:
                        gameName = "Halo CE";
                        moduleName = "halo1.dll";
                        offset = 0x2DCF80B;
                        expectedIntValue = 1550281062;
                        break;
                    case 2:
                        gameName = "Halo 2";
                        moduleName = "mss64dsp.flt";
                        offset = 0x24690;
                        break;
                    case 3:
                        gameName = "Halo 3";
                        moduleName = "halo3.dll";
                        offset = 0x1FC56C4;
                        break;
                    case 4:
                        gameName = "Halo 4";
                        moduleName = "halo4.dll";
                        offset = 0xE3B005;
                        break;
                    case 5:
                        gameName = "Halo 3: ODST";
                        moduleName = "halo3odst.dll";
                        offset = 0x2174F43;
                        expectedIntValue = 862940257;
                        break;
                    case 6:
                        gameName = "Halo: Reach";
                        moduleName = "haloreach.dll";
                        offset = 0xB87AE7;
                        expectedIntValue = 1869373768;
                        break;
                    default:
                        ShowErrorWindow($"No games in The Master Chief Collection appear to be running.");
                        ptrace(PtraceDetach, pid, IntPtr.Zero, IntPtr.Zero);
                        return "none";
                }
                
                // Get boring address stuff blah blah blah
                IntPtr baseAddress = GetModuleBaseAddress(pid, moduleName);
                if (baseAddress == IntPtr.Zero)
                {
                    ptrace(PtraceDetach, pid, IntPtr.Zero, IntPtr.Zero);
                    continue;
                }

                IntPtr targetAddress = IntPtr.Add(baseAddress, offset);
                try
                {
                    if (ptrace(PtraceAttach, pid, IntPtr.Zero, IntPtr.Zero) == -1)
                    {
                        ptrace(PtraceDetach, pid, IntPtr.Zero, IntPtr.Zero);
                        continue;
                    }
                    
                    // Actually check to make sure we're getting the value from the address provided AKA is the game actually running??
                    IntPtr result = ptrace(PtracePeekdata, pid, targetAddress, IntPtr.Zero);
                    long int32Value = result.ToInt64();
                    byte[] bytes = BitConverter.GetBytes(result.ToInt64());
                    int boolValue = bytes[0];
                    if (result.ToInt64() == 1 || int32Value == expectedIntValue || boolValue == expectedIntValue)
                    {
                        ptrace(PtraceDetach, pid, IntPtr.Zero, IntPtr.Zero);
                        return gameName;
                    }
                    else
                    {
                        Thread.Sleep(2);
                    }
                }
                finally
                {
                    ptrace(PtraceDetach, pid, IntPtr.Zero, IntPtr.Zero);
                }

                ptrace(PtraceDetach, pid, IntPtr.Zero, IntPtr.Zero);
            }
            attempts++;
            ptrace(PtraceDetach, pid, IntPtr.Zero, IntPtr.Zero);
        }

        ShowErrorWindow(
            $"No games in The Master Chief Collection appear to be running or the game process is occupied by another program, such as PINCE.");
        ptrace(PtraceDetach, pid, IntPtr.Zero, IntPtr.Zero);

        return "none";
    }
}