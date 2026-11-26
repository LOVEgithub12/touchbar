#include "WinApi.h"
#include "upstream/PluginInterface.h"
extern "C" { int _fltused=0;
__declspec(dllimport) HANDLE __stdcall LoadLibraryW(const wchar_t*);
__declspec(dllimport) BOOL __stdcall FreeLibrary(HANDLE);
__declspec(dllimport) void __stdcall ExitProcess(DWORD);
}
#include "SensorValueChecks.h"
class Host:public ITrafficMonitor{
public:
 int GetAPIVersion()override{return 0;}
 const wchar_t* GetVersion()override{return L"1.86";}
 double GetMonitorValue(MonitorItem m)override{return m==MI_CPU_TEMP?72.25:m==MI_CPU?17.0:m==MI_CPU_FREQ?3.125:-1;}
 const wchar_t* GetMonitorValueString(MonitorItem,int)override{return L"";}
 void ShowNotifyMessage(const wchar_t*)override{ExitProcess(11);}
 unsigned short GetLanguageId()const override{return 2052;}
 const wchar_t* GetPluginConfigDir()const override{return L".\\plugins\\";}
 int GetDPI(DPIType)const override{return 96;}
 unsigned int GetThemeColor()const override{return 0;}
 const wchar_t* GetStringRes(const wchar_t*,const wchar_t*)override{return L"";}
};
int main(){
 int sensorCheck=CheckSensorValues();if(sensorCheck)return sensorCheck;
 HANDLE dll=LoadLibraryW(L".\\build\\FixtureBridge.dll");if(!dll)return 1;
 auto instance=(ITMPlugin*(*)())GetProcAddress(dll,"TMPluginGetInstance");if(!instance)return 2;
 ITMPlugin* p=instance();if(!p||p->GetAPIVersion()!=7||p->GetItem(0)!=nullptr)return 3;
 FakeHardware hardware;
 auto setPlugin=(void(*)(ITMPlugin*))GetProcAddress(dll,"SetTestPlugin");if(!setPlugin)return 31;
 setPlugin(&hardware);
 Host host;p->OnInitialize(&host);metadataAllowed=false;p->DataRequired();
 HANDLE v2=CreateFileMappingW((HANDLE)(long long)-1,nullptr,4,0,128,L"Local\\TouchBar.TrafficMonitor.Telemetry.AbiTest");
 auto all=(char*)MapViewOfFile(v2,4,0,0,128);if(!all)return 32;
 if(*(DWORD*)(all+4)!=0x5442544d||*(DWORD*)(all+8)!=2||*(DWORD*)(all+12)!=128||*(DWORD*)(all+36)!=63)return 33;
 if(!Near(*(double*)(all+40),72.25)||!Near(*(double*)(all+64),65)||*(double*)(all+72)!=0||*(double*)(all+80)!=1250.5)return 34;
 if(*(DWORD*)(all+112)!=2||*(DWORD*)(all+116)!=1||(*(DWORD*)all&1))return 35;
 HANDLE map=CreateFileMappingW((HANDLE)(long long)-1,nullptr,4,0,96,L"Local\\TouchBar.TrafficMonitor.Cpu.AbiTest");
 auto bytes=(char*)MapViewOfFile(map,4,0,0,96);if(!bytes)return 4;
 if(*(DWORD*)(bytes+4)!=0x54424350 || *(DWORD*)(bytes+36)!=7)return 5;
 if(!Near(*(double*)(bytes+40),72.25) || *(double*)(bytes+48)!=17 || *(double*)(bytes+56)!=3125)return 6;
 for(int i=0;i<10000;++i)p->DataRequired();
 if(*(U64*)(bytes+80)!=10001 || (*(DWORD*)bytes&1))return 7;
 FreeLibrary(dll);if(*(DWORD*)(bytes+36)!=0||*(DWORD*)(all+36)!=0)return 8;
 dll=LoadLibraryW(L".\\build\\FixtureBridge.dll");
 instance=(ITMPlugin*(*)())GetProcAddress(dll,"TMPluginGetInstance");p=instance();
 setPlugin=(void(*)(ITMPlugin*))GetProcAddress(dll,"SetTestPlugin");setPlugin(&hardware);
 metadataAllowed=true;p->OnInitialize(&host);metadataAllowed=false;p->DataRequired();
 if(*(DWORD*)(bytes+36)!=7 || (*(DWORD*)bytes&1)||*(DWORD*)(all+36)!=63||*(double*)(all+80)!=1250.5)return 9;
 FreeLibrary(dll);UnmapViewOfFile(bytes);CloseHandle(map);UnmapViewOfFile(all);CloseHandle(v2);return 0;
}
