#include "WinApi.h"
#include "SensorValues.h"
extern "C" { int _fltused=0; }
#ifndef TOUCHBAR_BRIDGE_MAPPING
#define TOUCHBAR_BRIDGE_MAPPING L"Local\\TouchBar.TrafficMonitor.Cpu.v1"
#endif
#ifndef TOUCHBAR_TELEMETRY_MAPPING
#define TOUCHBAR_TELEMETRY_MAPPING L"Local\\TouchBar.TrafficMonitor.Telemetry.v2"
#endif

struct SharedCpu {
    volatile LONG sequence;
    DWORD magic, version, size;
    long long sampledAt, frequency;
    DWORD processId, valid;
    double temperature, load, clockMHz;
    U64 callbackTicks, totalCallbackTicks, publications;
    DWORD nativeCoreAverage, reserved;
};
static_assert(sizeof(SharedCpu)==96, "IPC layout changed");
static HANDLE mapping;
static SharedCpu* output;
static bool coreAverage;
static wchar_t configPath[1024];

struct SharedTelemetry {
    volatile LONG sequence;
    DWORD magic, version, size;
    long long sampledAt, frequency;
    DWORD processId, valid;
    double temperature, load, clockMHz, gpuTemperature, gpuLoad, gpuClockMHz;
    U64 callbackTicks, totalCallbackTicks, publications;
    DWORD nativeCoreAverage, gpuSource;
    U64 reserved;
};
static_assert(sizeof(SharedTelemetry)==128, "Telemetry IPC layout changed");
static HANDLE telemetryMapping;
static SharedTelemetry* telemetry;
static wchar_t gpuRoot[128], bridgeConfigPath[1024];
static ITMPlugin* HardwarePlugin() {
#ifdef TOUCHBAR_TEST_PLUGIN
    return TOUCHBAR_TEST_PLUGIN;
#else
    // Reuse its existing public cache; never call DataRequired or load a second DLL.
    HANDLE module=GetModuleHandleW(L"HardwareMonitor.dll");
    if(!module)return nullptr;
    auto instance=(ITMPlugin*(*)())GetProcAddress(module,"TMPluginGetInstance");
    return instance?instance():nullptr;
#endif
}
static void CheckTemperatureSource() {
    wchar_t source[128];
    GetPrivateProfileStringW(L"general",L"cpu_core_name",L"",source,128,configPath);
    coreAverage=Equal(source,L"Core Average");
}
class CpuBridge final : public ITMPlugin {
    ITrafficMonitor* host = nullptr;
    PluginItems items;
    unsigned count=0;
public:
    IPluginItem* GetItem(int) override { return nullptr; }
    const wchar_t* GetInfo(PluginInfoIndex index) override {
        switch(index){
        case TMI_NAME:return L"Touch Bar Hardware Bridge";
        case TMI_DESCRIPTION:return L"Shares existing CPU/GPU readings with Touch Bar. No extra hardware polling.";
        case TMI_AUTHOR:return L"TouchBar project";
        case TMI_VERSION:return L"2.0.1";
        default:return L"";
        }
    }
    void OnInitialize(ITrafficMonitor* app) override {
        // Host API 0 is the version supplied by TrafficMonitor 1.86.
        if(!app || app->GetAPIVersion()<0)return;
        host=app;
        const wchar_t* dir=host->GetPluginConfigDir();
        unsigned pos=0;
        while(dir&&dir[pos]&&pos<980){bridgeConfigPath[pos]=dir[pos];++pos;}
        if(pos&&bridgeConfigPath[pos-1]!=L'\\'&&bridgeConfigPath[pos-1]!=L'/')bridgeConfigPath[pos++]=L'\\';
        const wchar_t* file=L"TouchBarCpuBridge.ini";
        for(unsigned i=0;file[i];++i)bridgeConfigPath[pos++]=file[i];
        bridgeConfigPath[pos]=0;
        unsigned len=0;
        while(dir && dir[len] && len<980){configPath[len]=dir[len];++len;}
        // Host returns <config-directory>\plugins\; locate its parent config.ini.
        while(len && (configPath[len-1]==L'\\'||configPath[len-1]==L'/'))--len;
        while(len && configPath[len-1]!=L'\\' && configPath[len-1]!=L'/')--len;
        const wchar_t* tail=L"config.ini";
        for(unsigned i=0;tail[i];++i)configPath[len++]=tail[i];
        configPath[len]=0;
        CheckTemperatureSource();
        GetPrivateProfileStringW(L"bridge",L"gpu_identifier",L"",gpuRoot,128,bridgeConfigPath);
        items=BindPluginItems(HardwarePlugin(),gpuRoot);
        telemetryMapping=CreateFileMappingW((HANDLE)(long long)-1,nullptr,4,0,sizeof(SharedTelemetry),TOUCHBAR_TELEMETRY_MAPPING);
        if(telemetryMapping)telemetry=(SharedTelemetry*)MapViewOfFile(telemetryMapping,2,0,0,sizeof(SharedTelemetry));
        if(telemetry){
            _InterlockedExchange(&telemetry->sequence,1);
            telemetry->magic=0x5442544d;telemetry->version=2;telemetry->size=sizeof(SharedTelemetry);
            QueryPerformanceFrequency(&telemetry->frequency);telemetry->processId=GetCurrentProcessId();
            telemetry->valid=0;telemetry->sampledAt=0;telemetry->publications=0;telemetry->totalCallbackTicks=0;
            telemetry->reserved=0;_InterlockedIncrement(&telemetry->sequence);
        }
        mapping=CreateFileMappingW((HANDLE)(long long)-1,nullptr,4,0,sizeof(SharedCpu),TOUCHBAR_BRIDGE_MAPPING);
        if(!mapping)return;
        output=(SharedCpu*)MapViewOfFile(mapping,2,0,0,sizeof(SharedCpu));
        if(!output){CloseHandle(mapping);mapping=nullptr;return;}
        // A reader can still own the old mapping after TrafficMonitor restarts.
        _InterlockedExchange(&output->sequence,1);
        output->magic=0x54424350;output->version=1;output->size=sizeof(SharedCpu);
        QueryPerformanceFrequency(&output->frequency);output->processId=GetCurrentProcessId();
        output->valid=0;output->sampledAt=0;output->publications=0;output->totalCallbackTicks=0;
        _InterlockedIncrement(&output->sequence);
    }
    void DataRequired() override {
        if(!host||(!output&&!telemetry))return;
        long long start,end;QueryPerformanceCounter(&start);
        if(++count%5==0)CheckTemperatureSource();
        PluginValues values=ReadPluginValues(items);
        double temperature=values.cpu;
        DWORD temperatureSource=2;
        if(temperature<=0){temperature=coreAverage?host->GetMonitorValue(ITrafficMonitor::MI_CPU_TEMP):-1;temperatureSource=1;}
        const double load=host->GetMonitorValue(ITrafficMonitor::MI_CPU);
        const double clock=host->GetMonitorValue(ITrafficMonitor::MI_CPU_FREQ)*1000.0;
        DWORD valid=0;
        if(temperature>0 && temperature<150)valid|=1;
        if(load>=0 && load<=100)valid|=2;
        if(clock>0 && clock<20000)valid|=4;
        if(telemetry){
            DWORD allValid=valid;
            if(values.gpuTemperature>0&&values.gpuTemperature<150)allValid|=8;
            if(values.gpuLoad>=0&&values.gpuLoad<=100)allValid|=16;
            if(values.gpuClock>=0&&values.gpuClock<=20000)allValid|=32;
            _InterlockedIncrement(&telemetry->sequence);
            telemetry->temperature=temperature;telemetry->load=load;telemetry->clockMHz=clock;
            telemetry->gpuTemperature=values.gpuTemperature;telemetry->gpuLoad=values.gpuLoad;telemetry->gpuClockMHz=values.gpuClock;
            telemetry->valid=allValid;telemetry->sampledAt=start;telemetry->nativeCoreAverage=(valid&1)?temperatureSource:0;
            telemetry->gpuSource=(allValid&56)?1:0;
            QueryPerformanceCounter(&end);telemetry->callbackTicks=end-start;
            telemetry->totalCallbackTicks+=end-start;++telemetry->publications;
            _InterlockedIncrement(&telemetry->sequence);
        }
        // Keep v1 CPU readers working during staged rollout and rollback.
        if(!output)return;
        _InterlockedIncrement(&output->sequence);
        output->temperature=temperature;output->load=load;output->clockMHz=clock;
        output->valid=valid;output->sampledAt=start;output->nativeCoreAverage=(valid&1)?temperatureSource:0;
        QueryPerformanceCounter(&end);output->callbackTicks=end-start;
        output->totalCallbackTicks+=end-start;++output->publications;
        _InterlockedIncrement(&output->sequence);
    }
};
static CpuBridge bridge;
extern "C" __declspec(dllexport) ITMPlugin* TMPluginGetInstance(){return &bridge;}
extern "C" BOOL __stdcall DllMain(HANDLE,DWORD reason,void*) {
    if(reason==0){
        if(telemetry){_InterlockedIncrement(&telemetry->sequence);telemetry->valid=0;telemetry->sampledAt=0;_InterlockedIncrement(&telemetry->sequence);UnmapViewOfFile(telemetry);telemetry=nullptr;}
        if(telemetryMapping){CloseHandle(telemetryMapping);telemetryMapping=nullptr;}
        if(output){_InterlockedIncrement(&output->sequence);output->valid=0;output->sampledAt=0;_InterlockedIncrement(&output->sequence);UnmapViewOfFile(output);output=nullptr;}
        if(mapping){CloseHandle(mapping);mapping=nullptr;}
    }
    return 1;
}
