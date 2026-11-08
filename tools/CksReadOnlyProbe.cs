using System;
using System.Runtime.InteropServices;
public static class CksReadOnlyProbe {
    [StructLayout(LayoutKind.Sequential)]
    private struct UNICODE_STRING { public ushort Length; public ushort MaximumLength; public IntPtr Buffer; }
    [DllImport("ntdll.dll")]
    private static extern uint NtQueryLicenseValue(ref UNICODE_STRING name, out uint type, IntPtr data, uint dataSize, out uint resultSize);
    public static string Query(string name) {
        IntPtr buffer = Marshal.StringToHGlobalUni(name);
        IntPtr result = Marshal.AllocHGlobal(4);
        try {
            var valueName = new UNICODE_STRING { Length = checked((ushort)(name.Length * 2)), MaximumLength = checked((ushort)(name.Length * 2 + 2)), Buffer = buffer };
            uint type, size;
            uint status = NtQueryLicenseValue(ref valueName, out type, result, 4, out size);
            if (status != 0) return name + " : NTSTATUS 0x" + status.ToString("X8");
            if (type != 4 || size != 4) return name + " : unexpected type " + type + ", size " + size;
            return name + " : " + Marshal.ReadInt32(result);
        }
        finally { Marshal.FreeHGlobal(buffer); Marshal.FreeHGlobal(result); }
    }
}
