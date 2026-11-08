using System.ComponentModel;
using System.Runtime.InteropServices;
using WindowsTouchBar.Interop;

namespace WindowsTouchBar.Device;

public class Locator
{
    static Guid DfrDisplayInterfaceGuid = Guid.Parse("2003cacd-9e7c-477c-ab06-a5a8bbb1a63e");

    public static string? FindDfrDevice()
    {
        var devices = SetupAPI.SetupDiGetClassDevs(ref DfrDisplayInterfaceGuid,
            IntPtr.Zero, IntPtr.Zero, SetupAPI.DIGCF_PRESENT | SetupAPI.DIGCF_DEVICEINTERFACE);
        if (devices == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var item = new SP_DEVICE_INTERFACE_DATA();
            item.cbSize = (uint)Marshal.SizeOf(item);
            if (!SetupAPI.SetupDiEnumDeviceInterfaces(devices, IntPtr.Zero,
                ref DfrDisplayInterfaceGuid, 0, item))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == 259) return null; // ERROR_NO_MORE_ITEMS
                throw new Win32Exception(error);
            }

            SetupAPI.SetupDiGetDeviceInterfaceDetail(devices, item, IntPtr.Zero, 0,
                out uint requiredSize, null);
            int sizeError = Marshal.GetLastWin32Error();
            if (sizeError != 122) throw new Win32Exception(sizeError);

            var detail = Marshal.AllocHGlobal(checked((int)requiredSize));
            try
            {
                Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 4 + Marshal.SystemDefaultCharSize);
                if (!SetupAPI.SetupDiGetDeviceInterfaceDetail(devices, item, detail,
                    requiredSize, out _, null)) throw new Win32Exception(Marshal.GetLastWin32Error());
                return Marshal.PtrToStringAuto(IntPtr.Add(detail, 4));
            }
            finally { Marshal.FreeHGlobal(detail); }
        }
        finally { SetupAPI.SetupDiDestroyDeviceInfoList(devices); }
    }
}
