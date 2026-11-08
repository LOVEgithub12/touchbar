using System.Runtime.InteropServices;

namespace WindowsTouchBar.Interop;

class NativeMethods
{
    public const uint GenericRead = 0x80000000;
    public const uint GenericWrite = 0x40000000;

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr CreateFile(
        string filename,
        uint desiredAccess,
        FileShare sharing,
        IntPtr SecurityAttributes,
        FileMode mode,
        FileOptions options,
        IntPtr template
    );

    [DllImport("kernel32.dll")]
    public static extern void CloseHandle(IntPtr hdl);
}
