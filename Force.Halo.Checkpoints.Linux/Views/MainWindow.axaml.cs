using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace Force.Halo.Checkpoints.Linux.Views;

public partial class MainWindow : Window
{
    private string gameSelected = "none";
    private const int PTRACE_ATTACH = 16;
    private const int PTRACE_DETACH = 17;
    private const int PTRACE_POKEDATA = 5;

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
                Console.Write("The error window broke somehow. Please make an issue for this in GitHub.");
            }
        });
    }

    private void ModifyGameMemory(string processName, string moduleName, long offset, byte value)
    {
        int pid = GetProcessIdByName(processName);
        if (pid == -1)
        {
            ShowErrorWindow($"Process '{processName}' not found.");
            return;
        }

        IntPtr baseAddress = GetModuleBaseAddress(pid, moduleName);
        if (baseAddress == IntPtr.Zero)
        {
            ShowErrorWindow($"Failed to find the base address of {moduleName}.");
            return;
        }

        IntPtr targetAddress = IntPtr.Add(baseAddress, (int)offset);

        if (ptrace(PTRACE_ATTACH, pid, IntPtr.Zero, IntPtr.Zero) == -1)
        {
            ShowErrorWindow($"Failed to attach to process {pid}. Ensure you are running as root/sudo.");
            return;
        }

        try
        {
            IntPtr valuePtr = new(value);
            if (ptrace(PTRACE_POKEDATA, pid, targetAddress, valuePtr) == -1)
            {
                ShowErrorWindow("Failed to write to process memory. Try again.");
                return;
            }

            ShowErrorWindow("Checkpoint successfully forced!");
        }
        finally
        {
            _ = ptrace(PTRACE_DETACH, pid, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private static int GetProcessIdByName(string processName)
    {
        int lastPid = -1; // For whatever reason, this seems to be reliable for Proton. Don't know about anything else. I should probably find a better method.
        string[] procEntries = Directory.GetDirectories("/proc");

        foreach (string entry in procEntries)
        {
            if (int.TryParse(Path.GetFileName(entry), out int pid))
            {
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
                    // Ignore processes we can't read and hope it's not important.
                }
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
            if (line.Contains(moduleName))
            {
                var parts = line.Split('-');
                if (parts.Length > 0 && long.TryParse(parts[0], System.Globalization.NumberStyles.HexNumber, null, out long address))
                {
                    return new IntPtr(address);
                }
            }
        }

        return IntPtr.Zero;
    }

    private void ForceCheckpointButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (gameSelected == "none")
        {
            ShowErrorWindow("No game selected.");
        }
        else if (gameSelected == "Halo")
        {
            try
            {
                ModifyGameMemory("halo.exe", "halo.exe", 0x31973F, 1);
            }
            catch (Exception ex)
            {
                ShowErrorWindow($"Failed to force checkpoint: {ex.Message}");
                return;
            }
        }
    }

    private void HaloCEButton_OnClick(object? sender, RoutedEventArgs e)
    {
        gameSelected = "Halo";
    }
}