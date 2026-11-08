using System;
using System.Runtime.InteropServices;
using System.Text;
public static class DriverStoreReadOnly {
  [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool SetupGetInfDriverStoreLocationW(string FileName,IntPtr AlternatePlatformInfo,string LocaleName,StringBuilder ReturnBuffer,uint ReturnBufferSize,out uint RequiredSize);
  [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool SetupGetInfPublishedNameW(string DriverStoreLocation,StringBuilder ReturnBuffer,uint ReturnBufferSize,out uint RequiredSize);
  public static string Locate(string inf){ var b=new StringBuilder(32768); uint n; bool ok=SetupGetInfDriverStoreLocationW(inf,IntPtr.Zero,null,b,(uint)b.Capacity,out n); return ok?b.ToString():"ERROR 0x"+Marshal.GetLastWin32Error().ToString("X8"); }
  public static string Published(string inf){ var b=new StringBuilder(32768); uint n; bool ok=SetupGetInfPublishedNameW(inf,b,(uint)b.Capacity,out n); return ok?b.ToString():"ERROR 0x"+Marshal.GetLastWin32Error().ToString("X8"); }
}
