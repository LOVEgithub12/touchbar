using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

public static class InboxDriverBinding
{
    public const string ParentId = @"USB\VID_05AC&PID_8302\0000000000000000";
    const uint SPDIT_CLASSDRIVER = 1, DI_ENUMSINGLEINF = 0x00010000;
    const uint DI_FLAGSEX_ALLOWEXCLUDEDDRVS = 0x00000800;
    const uint DI_FLAGSEX_RESTART_DEVICE_ONLY = 0x20000000;
    const uint DI_NOWRITE_IDS = 0x80000000;
    const uint SPDRP_LOWERFILTERS = 0x12;
    const uint DICS_FLAG_GLOBAL = 1, DIREG_DEV = 1, KEY_QUERY_SET = 3;
    const int ERROR_NO_MORE_ITEMS = 259, ERROR_INSUFFICIENT_BUFFER = 122;

    [StructLayout(LayoutKind.Sequential)] struct DeviceInfo {
        public uint Size; public Guid ClassGuid; public uint DevInst; public UIntPtr Reserved;
        public static DeviceInfo New() { return new DeviceInfo { Size = (uint)Marshal.SizeOf(typeof(DeviceInfo)) }; }
    }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] struct InstallParams {
        public uint Size, Flags, FlagsEx;
        public IntPtr Parent, Handler, HandlerContext, Queue;
        public UIntPtr ClassReserved; public uint Reserved;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=260)] public string DriverPath;
        public static InstallParams New() { return new InstallParams { Size=(uint)Marshal.SizeOf(typeof(InstallParams)), DriverPath="" }; }
    }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] internal struct DriverInfo {
        public uint Size, DriverType; public UIntPtr Reserved;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=256)] public string Description;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=256)] public string Manufacturer;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=256)] public string Provider;
        public System.Runtime.InteropServices.ComTypes.FILETIME Date; public ulong Version;
        public static DriverInfo New() { return new DriverInfo { Size=(uint)Marshal.SizeOf(typeof(DriverInfo)) }; }
    }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] struct DriverDetail {
        public uint Size; public System.Runtime.InteropServices.ComTypes.FILETIME Date;
        public uint CompatIdsOffset, CompatIdsLength; public UIntPtr Reserved;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=256)] public string Section;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=260)] public string InfPath;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=256)] public string Description;
        public ushort HardwareId;
    }
    public sealed class Candidate {
        public string Description { get; set; }
        public string Provider { get; set; }
        public string Section { get; set; }
        public string InfPath { get; set; }
        public string HardwareId { get; set; }
        public string DeviceClass { get; set; }
        internal DriverInfo Data;
    }
    public sealed class BindResult {
        public string InstanceId { get; set; }
        public string InfPath { get; set; }
        public string Section { get; set; }
        public bool NeedReboot { get; set; }
    }

    [DllImport("setupapi.dll", SetLastError=true)] static extern IntPtr SetupDiCreateDeviceInfoList(IntPtr classGuid, IntPtr parent);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool SetupDiOpenDeviceInfoW(IntPtr set, string id, IntPtr parent, uint flags, ref DeviceInfo data);
    [DllImport("setupapi.dll", SetLastError=true)] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool SetupDiGetDeviceInstallParamsW(IntPtr set, ref DeviceInfo data, ref InstallParams parameters);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool SetupDiSetDeviceInstallParamsW(IntPtr set, ref DeviceInfo data, ref InstallParams parameters);
    [DllImport("setupapi.dll", SetLastError=true)] static extern bool SetupDiBuildDriverInfoList(IntPtr set, ref DeviceInfo data, uint type);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool SetupDiEnumDriverInfoW(IntPtr set, ref DeviceInfo data, uint type, uint index, ref DriverInfo driver);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool SetupDiGetDriverInfoDetailW(IntPtr set, ref DeviceInfo data, ref DriverInfo driver, IntPtr detail, uint bytes, out uint needed);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool SetupDiSetSelectedDriverW(IntPtr set, ref DeviceInfo data, ref DriverInfo driver);
    [DllImport("setupapi.dll", SetLastError=true)] static extern bool SetupDiDestroyDriverInfoList(IntPtr set, ref DeviceInfo data, uint type);
    [DllImport("newdev.dll", SetLastError=true)] static extern bool DiInstallDevice(IntPtr parent, IntPtr set, ref DeviceInfo data, ref DriverInfo driver, uint flags, out bool needReboot);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool SetupDiSetDeviceRegistryPropertyW(IntPtr set, ref DeviceInfo data, uint property, byte[] value, uint size);
    [DllImport("setupapi.dll", SetLastError=true)] static extern IntPtr SetupDiOpenDevRegKey(IntPtr set, ref DeviceInfo data, uint scope, uint profile, uint keyType, uint access);

    [DllImport("setupapi.dll", EntryPoint="SetupDiGetDeviceInstallParamsW", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool GetGlobalParams(IntPtr set, IntPtr data, ref InstallParams parameters);
    [DllImport("setupapi.dll", EntryPoint="SetupDiSetDeviceInstallParamsW", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool SetGlobalParams(IntPtr set, IntPtr data, ref InstallParams parameters);
    [DllImport("setupapi.dll", EntryPoint="SetupDiBuildDriverInfoList", SetLastError=true)] static extern bool BuildGlobalList(IntPtr set, IntPtr data, uint type);
    [DllImport("setupapi.dll", EntryPoint="SetupDiEnumDriverInfoW", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool EnumGlobalDriver(IntPtr set, IntPtr data, uint type, uint index, ref DriverInfo driver);
    [DllImport("setupapi.dll", EntryPoint="SetupDiGetDriverInfoDetailW", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool GetGlobalDetail(IntPtr set, IntPtr data, ref DriverInfo driver, IntPtr detail, uint bytes, out uint needed);
    [DllImport("setupapi.dll", EntryPoint="SetupDiDestroyDriverInfoList", SetLastError=true)] static extern bool DestroyGlobalList(IntPtr set, IntPtr data, uint type);

    public static Candidate[] ListGlobalCandidates(string inf) {
        IntPtr set=SetupDiCreateDeviceInfoList(IntPtr.Zero,IntPtr.Zero);
        if(set==new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try {
            var parameters=InstallParams.New();
            Check(GetGlobalParams(set,IntPtr.Zero,ref parameters),"Get global parameters");
            parameters.Flags|=DI_ENUMSINGLEINF;
            parameters.FlagsEx|=DI_FLAGSEX_ALLOWEXCLUDEDDRVS;
            parameters.DriverPath=Path.GetFullPath(inf);
            Check(SetGlobalParams(set,IntPtr.Zero,ref parameters),"Set global parameters");
            Check(BuildGlobalList(set,IntPtr.Zero,SPDIT_CLASSDRIVER),"Build global INF list");
            var results=new List<Candidate>();
            for(uint index=0;;index++) {
                var driver=DriverInfo.New();
                if(!EnumGlobalDriver(set,IntPtr.Zero,SPDIT_CLASSDRIVER,index,ref driver)) {
                    int error=Marshal.GetLastWin32Error(); if(error==ERROR_NO_MORE_ITEMS) break; throw new Win32Exception(error);
                }
                uint needed;
                GetGlobalDetail(set,IntPtr.Zero,ref driver,IntPtr.Zero,0,out needed);
                int errorDetail=Marshal.GetLastWin32Error();
                if(errorDetail!=ERROR_INSUFFICIENT_BUFFER) throw new Win32Exception(errorDetail);
                IntPtr buffer=Marshal.AllocHGlobal(checked((int)needed));
                try {
                    Marshal.WriteInt32(buffer,Marshal.SizeOf(typeof(DriverDetail)));
                    Check(GetGlobalDetail(set,IntPtr.Zero,ref driver,buffer,needed,out needed),"Get global detail");
                    var detail=(DriverDetail)Marshal.PtrToStructure(buffer,typeof(DriverDetail));
                    int offset=(int)Marshal.OffsetOf(typeof(DriverDetail),"HardwareId");
                    results.Add(new Candidate{ Description=driver.Description,Provider=driver.Provider,Section=detail.Section,InfPath=detail.InfPath,HardwareId=Marshal.PtrToStringUni(IntPtr.Add(buffer,offset)),Data=driver });
                } finally { Marshal.FreeHGlobal(buffer); }
            }
            return results.ToArray();
        } finally { DestroyGlobalList(set,IntPtr.Zero,SPDIT_CLASSDRIVER); SetupDiDestroyDeviceInfoList(set); }
    }
    public static string ProbeGlobalSelection(string instanceId,string inf,string section,string hardwareId) { return GlobalModelOperation(instanceId,inf,section,hardwareId,false); }
    // Install phase only: callers must have an external rollback plan. This path has not been executed.
    public static string BindGlobalCandidate(string instanceId,string inf,string section,string hardwareId) {
        ValidateGlobalInstall(instanceId,inf,section,hardwareId);
        return GlobalModelOperation(instanceId,inf,section,hardwareId,true);
    }
    static string GlobalModelOperation(string instanceId,string inf,string section,string hardwareId,bool install) {
        DeviceInfo data; IntPtr set=Open(instanceId,out data);
        try {
            var deviceParameters=InstallParams.New();
            Check(SetupDiGetDeviceInstallParamsW(set,ref data,ref deviceParameters),"Get exact-device parameters");
            deviceParameters.Flags|=DI_NOWRITE_IDS;
            deviceParameters.FlagsEx|=DI_FLAGSEX_RESTART_DEVICE_ONLY;
            Check(SetupDiSetDeviceInstallParamsW(set,ref data,ref deviceParameters),"Set exact-device installation bounds");
            var parameters=InstallParams.New();
            Check(GetGlobalParams(set,IntPtr.Zero,ref parameters),"Get global parameters");
            parameters.Flags|=DI_ENUMSINGLEINF;
            parameters.FlagsEx|=DI_FLAGSEX_ALLOWEXCLUDEDDRVS;
            parameters.DriverPath=Path.GetFullPath(inf);
            Check(SetGlobalParams(set,IntPtr.Zero,ref parameters),"Set global parameters");
            Check(BuildGlobalList(set,IntPtr.Zero,SPDIT_CLASSDRIVER),"Build global list");
            var selected=DriverInfo.New();
            int matches=0;
            for(uint index=0;;index++) {
                var driver=DriverInfo.New();
                if(!EnumGlobalDriver(set,IntPtr.Zero,SPDIT_CLASSDRIVER,index,ref driver)) {
                    int error=Marshal.GetLastWin32Error(); if(error==ERROR_NO_MORE_ITEMS) break; throw new Win32Exception(error);
                }
                uint needed; GetGlobalDetail(set,IntPtr.Zero,ref driver,IntPtr.Zero,0,out needed);
                int detailError=Marshal.GetLastWin32Error();
                if(detailError!=ERROR_INSUFFICIENT_BUFFER) throw new Win32Exception(detailError,"Get global detail size");
                IntPtr buffer=Marshal.AllocHGlobal(checked((int)needed));
                try {
                    Marshal.WriteInt32(buffer,Marshal.SizeOf(typeof(DriverDetail)));
                    Check(GetGlobalDetail(set,IntPtr.Zero,ref driver,buffer,needed,out needed),"Get global detail");
                    var detail=(DriverDetail)Marshal.PtrToStructure(buffer,typeof(DriverDetail));
                    int offset=(int)Marshal.OffsetOf(typeof(DriverDetail),"HardwareId");
                    string hid=Marshal.PtrToStringUni(IntPtr.Add(buffer,offset));
                    if(detail.Section.Equals(section,StringComparison.OrdinalIgnoreCase) && hid.Equals(hardwareId,StringComparison.OrdinalIgnoreCase)
                        && Path.GetFullPath(detail.InfPath).Equals(Path.GetFullPath(inf),StringComparison.OrdinalIgnoreCase)) { selected=driver; matches++; }
                } finally { Marshal.FreeHGlobal(buffer); }
            }
            if(matches!=1) throw new InvalidOperationException("Expected one exact global model; found "+matches);
            var originalClass=data.ClassGuid;
            bool reboot=false;
            bool succeeded=install ? DiInstallDevice(IntPtr.Zero,set,ref data,ref selected,0,out reboot) : SetupDiSetSelectedDriverW(set,ref data,ref selected);
            int selectionError=succeeded?0:Marshal.GetLastWin32Error();
            if(install && !succeeded) throw new Win32Exception(selectionError,"DiInstallDevice on exact Touch Bar instance failed; inspect device and restore previous binding if needed.");
            return "Install="+install+" Succeeded="+succeeded+" Error=0x"+selectionError.ToString("X8")+" NeedReboot="+reboot+" OriginalClass="+originalClass+" ReturnedClass="+data.ClassGuid;
        } finally { DestroyGlobalList(set,IntPtr.Zero,SPDIT_CLASSDRIVER); SetupDiDestroyDeviceInfoList(set); }
    }
    static void ValidateGlobalInstall(string instanceId,string inf,string section,string hardwareId) {
        ValidateDevice(instanceId);
        string systemInf=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"INF");
        string allowedUsb=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),@"WinSxS\amd64_dual_usb.inf_31bf3856ad364e35_10.0.19041.5794_none_b96e696680cd53f1\usb.inf");
        string path=Path.GetFullPath(inf);
        bool composite=instanceId==ParentId && (path.Equals(allowedUsb,StringComparison.OrdinalIgnoreCase) || path.Equals(Path.Combine(systemInf,"usb.inf"),StringComparison.OrdinalIgnoreCase)) && section=="Composite.Dev" && hardwareId==@"USB\COMPOSITE";
        bool rollback=instanceId==ParentId && path.Equals(Path.Combine(systemInf,"oem171.inf"),StringComparison.OrdinalIgnoreCase) && section=="AppleDFR_Device" && hardwareId==@"USB\VID_05AC&PID_8302";
        if(!composite && !rollback) throw new ArgumentException("Global installation is restricted to the exact original OS composite INF or known Apple rollback model.");
        if(composite) {
            using(var sha=System.Security.Cryptography.SHA256.Create())
            using(var original=File.OpenRead(Path.Combine(systemInf,"usb.inf")))
            using(var source=File.OpenRead(allowedUsb)) {
                if(!Convert.ToBase64String(sha.ComputeHash(original)).Equals(Convert.ToBase64String(sha.ComputeHash(source))))
                    throw new InvalidOperationException("WinSxS USB INF is not identical to the Windows original.");
            }
        }
    }
    public static Candidate[] ListInheritedCandidates(string instanceId,string inf) {
        ValidateDevice(instanceId);
        IntPtr set=SetupDiCreateDeviceInfoList(IntPtr.Zero,IntPtr.Zero);
        if(set==new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var data=DeviceInfo.New(); bool opened=false;
        try {
            var parameters=InstallParams.New();
            Check(GetGlobalParams(set,IntPtr.Zero,ref parameters),"Get global parameters");
            parameters.Flags|=DI_ENUMSINGLEINF;
            parameters.FlagsEx|=DI_FLAGSEX_ALLOWEXCLUDEDDRVS;
            parameters.DriverPath=Path.GetFullPath(inf);
            Check(SetGlobalParams(set,IntPtr.Zero,ref parameters),"Set global parameters");
            Check(BuildGlobalList(set,IntPtr.Zero,SPDIT_CLASSDRIVER),"Build global list");
            Check(SetupDiOpenDeviceInfoW(set,instanceId,IntPtr.Zero,2,ref data),"Open device inheriting global class drivers");
            opened=true;
            return ReadAssociatedCandidates(set,ref data).ToArray();
        } finally {
            if(opened) SetupDiDestroyDriverInfoList(set,ref data,SPDIT_CLASSDRIVER);
            DestroyGlobalList(set,IntPtr.Zero,SPDIT_CLASSDRIVER); SetupDiDestroyDeviceInfoList(set);
        }
    }
    // Supported cross-class path: inherit a class driver list before selection.
    // SetupDiSetSelectedDriver performs the documented class migration at install time.
    public static BindResult BindInherited(string instanceId,string inf,string section,string hardwareId) {
        ValidateGlobalInstall(instanceId,inf,section,hardwareId);
        IntPtr set=SetupDiCreateDeviceInfoList(IntPtr.Zero,IntPtr.Zero);
        if(set==new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var data=DeviceInfo.New(); bool opened=false;
        try {
            var parameters=InstallParams.New();
            Check(GetGlobalParams(set,IntPtr.Zero,ref parameters),"Get global parameters");
            parameters.Flags|=DI_ENUMSINGLEINF;
            parameters.FlagsEx|=DI_FLAGSEX_ALLOWEXCLUDEDDRVS;
            parameters.DriverPath=Path.GetFullPath(inf);
            Check(SetGlobalParams(set,IntPtr.Zero,ref parameters),"Set global parameters");
            Check(BuildGlobalList(set,IntPtr.Zero,SPDIT_CLASSDRIVER),"Build global list");
            Check(SetupDiOpenDeviceInfoW(set,instanceId,IntPtr.Zero,2,ref data),"Open exact device inheriting global class drivers");
            opened=true;
            var candidates=ReadAssociatedCandidates(set,ref data);
            var matches=candidates.FindAll(c => c.Section.Equals(section,StringComparison.OrdinalIgnoreCase)
                && c.HardwareId.Equals(hardwareId,StringComparison.OrdinalIgnoreCase)
                && Path.GetFullPath(c.InfPath).Equals(Path.GetFullPath(inf),StringComparison.OrdinalIgnoreCase));
            if(matches.Count!=1) throw new InvalidOperationException("Expected one exact inherited driver model, found "+matches.Count);
            var deviceParameters=InstallParams.New();
            Check(SetupDiGetDeviceInstallParamsW(set,ref data,ref deviceParameters),"Get exact-device parameters");
            deviceParameters.Flags|=DI_NOWRITE_IDS;
            deviceParameters.FlagsEx|=DI_FLAGSEX_RESTART_DEVICE_ONLY;
            Check(SetupDiSetDeviceInstallParamsW(set,ref data,ref deviceParameters),"Set exact-device installation bounds");
            var selected=matches[0].Data;
            Check(SetupDiSetSelectedDriverW(set,ref data,ref selected),"Select exact inherited driver model");
            bool reboot;
            Check(DiInstallDevice(IntPtr.Zero,set,ref data,ref selected,0,out reboot),"Install exact inherited driver model");
            return new BindResult { InstanceId=instanceId,InfPath=inf,Section=section,NeedReboot=reboot };
        } finally {
            if(opened) SetupDiDestroyDriverInfoList(set,ref data,SPDIT_CLASSDRIVER);
            DestroyGlobalList(set,IntPtr.Zero,SPDIT_CLASSDRIVER); SetupDiDestroyDeviceInfoList(set);
        }
    }
    static List<Candidate> ReadAssociatedCandidates(IntPtr set,ref DeviceInfo data) {
        var results=new List<Candidate>();
        for(uint index=0;;index++) {
            var driver=DriverInfo.New();
            if(!SetupDiEnumDriverInfoW(set,ref data,SPDIT_CLASSDRIVER,index,ref driver)) {
                int error=Marshal.GetLastWin32Error(); if(error==ERROR_NO_MORE_ITEMS) break; throw new Win32Exception(error,"Enumerate inherited driver");
            }
            uint needed; SetupDiGetDriverInfoDetailW(set,ref data,ref driver,IntPtr.Zero,0,out needed);
            int detailError=Marshal.GetLastWin32Error();
            if(detailError!=ERROR_INSUFFICIENT_BUFFER) throw new Win32Exception(detailError,"Read inherited detail size");
            IntPtr buffer=Marshal.AllocHGlobal(checked((int)needed));
            try {
                Marshal.WriteInt32(buffer,Marshal.SizeOf(typeof(DriverDetail)));
                Check(SetupDiGetDriverInfoDetailW(set,ref data,ref driver,buffer,needed,out needed),"Read inherited detail");
                var detail=(DriverDetail)Marshal.PtrToStructure(buffer,typeof(DriverDetail));
                int offset=(int)Marshal.OffsetOf(typeof(DriverDetail),"HardwareId");
                results.Add(new Candidate { Description=driver.Description,Provider=driver.Provider,Section=detail.Section,InfPath=detail.InfPath,HardwareId=Marshal.PtrToStringUni(IntPtr.Add(buffer,offset)),DeviceClass=data.ClassGuid.ToString(),Data=driver });
            } finally { Marshal.FreeHGlobal(buffer); }
        }
        return results;
    }
    static void Check(bool result, string action) { if(!result) throw new Win32Exception(Marshal.GetLastWin32Error(),action); }
    static void ValidateDevice(string id) {
        if(id!=ParentId && !id.StartsWith(@"USB\VID_05AC&PID_8302&MI_01\",StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only the known Touch Bar root or display MI_01 is allowed.");
    }
    static IntPtr Open(string id, out DeviceInfo data) {
        ValidateDevice(id);
        if(IntPtr.Size!=8) throw new PlatformNotSupportedException("Run in 64-bit PowerShell.");
        data=DeviceInfo.New();
        IntPtr set=SetupDiCreateDeviceInfoList(IntPtr.Zero,IntPtr.Zero);
        if(set==new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if(!SetupDiOpenDeviceInfoW(set,id,IntPtr.Zero,0,ref data)) {
            int error=Marshal.GetLastWin32Error(); SetupDiDestroyDeviceInfoList(set); throw new Win32Exception(error,"Open exact Touch Bar instance");
        }
        return set;
    }
    static List<Candidate> Build(IntPtr set, ref DeviceInfo data, string inf) {
        string full=Path.GetFullPath(inf);
        if(!File.Exists(full)) throw new FileNotFoundException("INF must exist",full);
        var parameters=InstallParams.New();
        Check(SetupDiGetDeviceInstallParamsW(set,ref data,ref parameters),"Get install parameters");
        parameters.Flags |= DI_ENUMSINGLEINF|DI_NOWRITE_IDS;
        parameters.FlagsEx |= DI_FLAGSEX_ALLOWEXCLUDEDDRVS|DI_FLAGSEX_RESTART_DEVICE_ONLY;
        parameters.DriverPath=full;
        Check(SetupDiSetDeviceInstallParamsW(set,ref data,ref parameters),"Set in-memory INF enumeration parameters");
        Check(SetupDiBuildDriverInfoList(set,ref data,SPDIT_CLASSDRIVER),"Build class driver list for exact INF");
        var results=new List<Candidate>();
        for(uint index=0;;index++) {
            var driver=DriverInfo.New();
            if(!SetupDiEnumDriverInfoW(set,ref data,SPDIT_CLASSDRIVER,index,ref driver)) {
                int error=Marshal.GetLastWin32Error(); if(error==ERROR_NO_MORE_ITEMS) break; throw new Win32Exception(error,"Enumerate driver");
            }
            uint needed;
            SetupDiGetDriverInfoDetailW(set,ref data,ref driver,IntPtr.Zero,0,out needed);
            int detailError=Marshal.GetLastWin32Error();
            if(detailError!=ERROR_INSUFFICIENT_BUFFER) throw new Win32Exception(detailError,"Get driver detail size");
            IntPtr buffer=Marshal.AllocHGlobal(checked((int)needed));
            try {
                Marshal.WriteInt32(buffer,Marshal.SizeOf(typeof(DriverDetail)));
                Check(SetupDiGetDriverInfoDetailW(set,ref data,ref driver,buffer,needed,out needed),"Read driver detail");
                var detail=(DriverDetail)Marshal.PtrToStructure(buffer,typeof(DriverDetail));
                int offset=(int)Marshal.OffsetOf(typeof(DriverDetail),"HardwareId");
                results.Add(new Candidate { Description=driver.Description,Provider=driver.Provider,Section=detail.Section,
                    InfPath=detail.InfPath, HardwareId=Marshal.PtrToStringUni(IntPtr.Add(buffer,offset)),DeviceClass=data.ClassGuid.ToString(),Data=driver });
            } finally { Marshal.FreeHGlobal(buffer); }
        }
        return results;
    }
    public static Candidate[] ListCandidates(string instanceId,string inf) {
        DeviceInfo data; IntPtr set=Open(instanceId,out data);
        try { return Build(set,ref data,inf).ToArray(); }
        finally { SetupDiDestroyDriverInfoList(set,ref data,SPDIT_CLASSDRIVER); SetupDiDestroyDeviceInfoList(set); }
    }

    // This performs an installation. The caller must already have saved and reviewed
    // per-device properties and the prior driver. No registry IDs or system INF are edited.
    public static BindResult Bind(string instanceId,string inf,string section,string hardwareId) {
        ValidateDevice(instanceId);
        string name=Path.GetFileName(inf);
        bool allowed=(instanceId==ParentId && name.Equals("usb.inf",StringComparison.OrdinalIgnoreCase) && section=="Composite.Dev" && hardwareId==@"USB\COMPOSITE")
            || (instanceId!=ParentId && name.Equals("winusb.inf",StringComparison.OrdinalIgnoreCase) && section=="WINUSB" && hardwareId==@"USB\MS_COMP_WINUSB")
            || (instanceId==ParentId && name.Equals("oem171.inf",StringComparison.OrdinalIgnoreCase) && section.StartsWith("AppleDFR_Device",StringComparison.Ordinal) && hardwareId==@"USB\VID_05AC&PID_8302");
        if(!allowed || !Path.GetFullPath(inf).Equals(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"INF",name),StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only the untouched system usb.inf, winusb.inf, or known Apple rollback oem171.inf and exact model may be installed.");
        DeviceInfo data; IntPtr set=Open(instanceId,out data);
        try {
            var candidates=Build(set,ref data,inf);
            var matches=candidates.FindAll(c => c.Section.Equals(section,StringComparison.OrdinalIgnoreCase) && c.HardwareId.Equals(hardwareId,StringComparison.OrdinalIgnoreCase) && Path.GetFullPath(c.InfPath).Equals(Path.GetFullPath(inf),StringComparison.OrdinalIgnoreCase));
            if(matches.Count!=1) throw new InvalidOperationException("Expected one exact driver model, found "+matches.Count);
            var selected=matches[0].Data;
            Check(SetupDiSetSelectedDriverW(set,ref data,ref selected),"Select exact driver model");
            bool reboot;
            Check(DiInstallDevice(IntPtr.Zero,set,ref data,ref selected,2,out reboot),"Install selected driver on exact Touch Bar instance");
            return new BindResult { InstanceId=instanceId, InfPath=inf,Section=section,NeedReboot=reboot };
        } finally { SetupDiDestroyDriverInfoList(set,ref data,SPDIT_CLASSDRIVER); SetupDiDestroyDeviceInfoList(set); }
    }
    public static void SetLowerFilters(string instanceId,string[] filters) {
        if(instanceId!=ParentId) throw new ArgumentException("Only the Touch Bar root filter may be changed.");
        DeviceInfo data; IntPtr set=Open(instanceId,out data);
        try {
            byte[] bytes=filters==null || filters.Length==0 ? null : Encoding.Unicode.GetBytes(String.Join("\0",filters)+"\0\0");
            Check(SetupDiSetDeviceRegistryPropertyW(set,ref data,SPDRP_LOWERFILTERS,bytes,bytes==null ? 0 : (uint)bytes.Length),"Set exact-device LowerFilters");
        } finally { SetupDiDestroyDeviceInfoList(set); }
    }
    public static void SetParameter(string instanceId,string name,object value,RegistryValueKind kind) {
        ValidateDevice(instanceId);
        if(!((instanceId==ParentId && name=="OriginalConfigurationValue" && kind==RegistryValueKind.DWord)
          || (instanceId!=ParentId && name=="DeviceInterfaceGUIDs" && kind==RegistryValueKind.MultiString))) throw new ArgumentException("Unexpected per-device parameter");
        DeviceInfo data; IntPtr set=Open(instanceId,out data);
        try {
            IntPtr raw=SetupDiOpenDevRegKey(set,ref data,DICS_FLAG_GLOBAL,0,DIREG_DEV,KEY_QUERY_SET);
            if(raw==new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error(),"Open exact-device hardware key");
            using(var handle=new SafeRegistryHandle(raw,true))
            using(var key=RegistryKey.FromHandle(handle)) {
                if(value==null) key.DeleteValue(name,false); else key.SetValue(name,value,kind);
            }
        } finally { SetupDiDestroyDeviceInfoList(set); }
    }
    public static string InspectParameterKey(string instanceId) {
        DeviceInfo data; IntPtr set=Open(instanceId,out data);
        try {
            IntPtr raw=SetupDiOpenDevRegKey(set,ref data,DICS_FLAG_GLOBAL,0,DIREG_DEV,1);
            if(raw==new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error(),"Open read-only exact-device key");
            using(var handle=new SafeRegistryHandle(raw,true))
            using(var key=RegistryKey.FromHandle(handle)) { return "Values="+String.Join(",",key.GetValueNames())+";Config="+Convert.ToString(key.GetValue("OriginalConfigurationValue")); }
        } finally { SetupDiDestroyDeviceInfoList(set); }
    }
    public static string StructSizes() { return "Device="+Marshal.SizeOf(typeof(DeviceInfo))+" Install="+Marshal.SizeOf(typeof(InstallParams))+" Driver="+Marshal.SizeOf(typeof(DriverInfo))+" Detail="+Marshal.SizeOf(typeof(DriverDetail))+" HardwareIdOffset="+Marshal.OffsetOf(typeof(DriverDetail),"HardwareId"); }
}













