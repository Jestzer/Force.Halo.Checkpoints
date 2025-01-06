using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace Force.Halo.Checkpoints.Linux.Views;

public partial class MainWindow : Window
{
    private string gameSelected;

    private const int O_RDONLY = 0;
    private const int O_RDWR = 2;

    [DllImport("libc.so.6", SetLastError = true)]
    private static extern IntPtr open(string pathname, int flags);

    [DllImport("libc.so.6", SetLastError = true)]
    private static extern long pwrite(IntPtr fd, byte[] buf, ulong count, long offset);

    [DllImport("libc.so.6", SetLastError = true)]
    private static extern int close(IntPtr fd);

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

            // Check if VisualRoot is not null and is a Window before casting.
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
    
    private void ModifyGameMemory(string processName, long offset, byte value)
    {
        // Locate the process ID (PID)
        int pid = GetProcessIdByName(processName);
        if (pid == -1)
        {
            ShowErrorWindow($"Process '{processName}' not found.");
        }

        // Locate the memory file for the process
        string memPath = $"/proc/{pid}/mem";

        // Open the memory file
        IntPtr fd = open(memPath, O_RDWR);
        if (fd.ToInt64() == -1)
        {
            ShowErrorWindow("Failed to open process memory file. Ensure you have the necessary permissions.");
        }

        try
        {
            byte[] buffer = { value };
            long result = pwrite(fd, buffer, (ulong)buffer.Length, offset);

            if (result == -1)
            {
                ShowErrorWindow("Failed to write to memory.");
            }
        }
        finally
        {
            close(fd);
        }
    }

    private int GetProcessIdByName(string processName)
    {
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
                        return pid;
                    }
                }
                catch
                {
                    // Ignore processes we can't read
                }
            }
        }

        return -1;
    }
    
    private void ForceCheckpointButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (gameSelected == null)
        {
            ShowErrorWindow("test");
        }
        else if (gameSelected == "Halo")
        {
            try
            {
                ModifyGameMemory("halo", 0x31973F, 1);
                ShowErrorWindow("Checkpoint forced successfully.");
            }
            catch (Exception ex)
            {
                ShowErrorWindow($"Failed to force checkpoint: {ex.Message}");
            }

            // find the executable, then find the dll in memory and change the offset accordingly.
        }
    }

    private void HaloCEButton_OnClickButton_OnClick(object? sender, RoutedEventArgs e)
    {
        gameSelected = "Halo";
    }
}