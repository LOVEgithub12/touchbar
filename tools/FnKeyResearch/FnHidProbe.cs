using HidSharp;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
if(args.Contains("--probe-keyboard"))
{
    var keyboard=DeviceList.Local.GetHidDevices(0x05ac,0x0340).Single(d=>d.GetReportDescriptor().DeviceItems.Any(i=>i.InputReports.SelectMany(r=>r.DataItems).Any(f=>f.Usages.ContainsValue(0x00ff0003))));
    foreach(var path in new[]{keyboard.DevicePath,keyboard.DevicePath.EndsWith(@"\kbd")?keyboard.DevicePath[..^4]:keyboard.DevicePath})
    foreach(var access in new uint[]{0,0x80000000})
    {
        using var h=Native.CreateFile(path,access,3,0,3,0,0);
        Console.WriteLine($"Open suffix={path.EndsWith(@"\kbd")} access={access:X} invalid={h.IsInvalid} error={Marshal.GetLastWin32Error()}");
        if(h.IsInvalid)continue;
        var b=new byte[10]; b[0]=1;
        var ok=Native.HidD_GetInputReport(h,b,b.Length);
        Console.WriteLine($"GET_INPUT_REPORT ok={ok} error={Marshal.GetLastWin32Error()} id={b[0]} fn={(b[9]&2)!=0}");
    }
    return;
}
foreach(var d in DeviceList.Local.GetHidDevices(0x05ac))
{
    try {
        var descriptor=d.GetReportDescriptor();
        Console.WriteLine(JsonSerializer.Serialize(new {
            d.DevicePath, d.ProductID,
            InputLength=d.GetMaxInputReportLength(),
            Items=descriptor.DeviceItems.Select(i=>new {
                Usages=i.Usages.GetAllValues().Select(u=>$"{u:X8}"),
                Reports=i.InputReports.Select(r=>new { r.ReportID,r.Length,
                    Fields=r.DataItems.Select(f=>new {Usages=f.Usages.GetAllValues().Select(u=>$"{u:X8}"), f.ElementBits,f.ElementCount}) })
            })
        }));
    } catch(Exception ex) { Console.WriteLine(JsonSerializer.Serialize(new {d.DevicePath,Error=ex.Message})); }
}
static class Native {
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern SafeFileHandle CreateFile(string name,uint access,uint share,nint security,uint creation,uint flags,nint template);
    [DllImport("hid.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool HidD_GetInputReport(SafeFileHandle device,[In,Out] byte[] data,int length);
}
