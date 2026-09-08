using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace XboxMetroLauncher.Utilities;

internal static class ProcessPaths
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, int flags, StringBuilder name, ref int length);

    public static string? TryRead(Process process)
    {
        try
        {
            // Query the process image without depending on the loader's module list.
            var length = 32768;
            var name = new StringBuilder(length);
            return QueryFullProcessImageName(process.SafeHandle, 0, name, ref length) ? name.ToString() : null;
        }
        catch { return null; }
    }
}
