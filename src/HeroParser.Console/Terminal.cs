using System;
using System.Runtime.InteropServices;

namespace HeroParser.Console;

internal static class Terminal
{
    private const int STD_OUTPUT_HANDLE = -11;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    public static void EnableVirtualTerminalProcessing()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                var handle = GetStdHandle(STD_OUTPUT_HANDLE);
                if (handle != IntPtr.Zero && handle != new IntPtr(-1))
                {
                    if (GetConsoleMode(handle, out uint mode))
                    {
                        mode |= ENABLE_VIRTUAL_TERMINAL_PROCESSING;
                        SetConsoleMode(handle, mode);
                    }
                }
            }
            catch
            {
                // Gracefully ignore on failure (e.g. redirected stdout or environment constraints)
            }
        }
    }
}
