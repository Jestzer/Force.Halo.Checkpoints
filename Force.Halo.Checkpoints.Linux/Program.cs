using Avalonia;
using System;
using System.Runtime.InteropServices;
using SDL2;

namespace Force.Halo.Checkpoints.Linux;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        ConfigureSdlDllResolver();
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void ConfigureSdlDllResolver()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }

        NativeLibrary.SetDllImportResolver(typeof(SDL).Assembly, (libraryName, assembly, searchPath) =>
        {
            if (!string.Equals(libraryName, "SDL2.dll", StringComparison.OrdinalIgnoreCase))
            {
                return IntPtr.Zero;
            }

            string[] candidates =
            {
                "libSDL2-2.0.so.0",
                "libSDL2.so",
                "SDL2"
            };

            foreach (string candidate in candidates)
            {
                if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out IntPtr handle))
                {
                    return handle;
                }
            }

            return IntPtr.Zero;
        });
    }
}
