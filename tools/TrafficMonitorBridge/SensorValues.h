#pragma once
#include "upstream/PluginInterface.h"

static bool Equal(const wchar_t* a,const wchar_t* b) {
    if(!a||!b)return false;
    while(*a && *a==*b){++a;++b;} return *a==*b;
}
static const wchar_t* After(const wchar_t* text,const wchar_t* prefix) {
    if(!text||!prefix)return nullptr;
    while(*prefix){if(*text++!=*prefix++)return nullptr;} return text;
}
static bool Space(wchar_t c){return c==L' '||c==L'\t'||c==0xa0;}
static double ParseMHz(const wchar_t* text) {
    // HardwareMonitor F-format is ungrouped, locale-dependent decimal text.
    // Require MHz: this plugin version's GHz conversion uses 1024, not 1000.
    if(!text)return -1;
    unsigned length=0;while(text[length] && length<64)++length;
    if(length==64)return -1;
    while(Space(*text))++text;
    double value=0,scale=1;bool decimal=false,digit=false,lastDigit=false;
    for(;*text;++text){
        if(*text>=L'0'&&*text<=L'9'){
            digit=true;lastDigit=true;
            if(decimal){scale*=10;value+=(*text-L'0')/scale;}else value=value*10+(*text-L'0');
            if(value>20000)return -1;
        }else if((*text==L'.'||*text==L',')&&!decimal&&digit){decimal=true;lastDigit=false;}
        else break;
    }
    if(!digit||!lastDigit)return -1;
    while(Space(*text))++text;
    text=After(text,L"MHz");if(!text)return -1;
    while(Space(*text))++text;
    return *text ? -1 : value;
}
static double NumericItem(IPluginItem* item) {
    // A null sensor value becomes graph value 0; use the public text to reject
    // the plugin's missing-value marker instead of claiming an idle load.
    const wchar_t* text=item->GetItemValueText();
    if(!text)return -1;while(Space(*text))++text;
    if(*text<L'0'||*text>L'9'||!item->IsDrawResourceUsageGraph())return -1;
    return item->GetResourceUsageGraphValue()*100.0;
}
struct PluginValues { double cpu=-1,gpuTemperature=-1,gpuLoad=-1,gpuClock=-1; };
struct PluginItems { IPluginItem* cpu=nullptr;IPluginItem* gpuTemperature=nullptr;IPluginItem* gpuCoreLoad=nullptr;IPluginItem* gpuD3dLoad=nullptr;IPluginItem* gpuClock=nullptr; };
static PluginItems BindPluginItems(ITMPlugin* plugin,const wchar_t* gpuRoot) {
    PluginItems result;
    if(!plugin||plugin->GetAPIVersion()<6)return result;
    // Call ONLY from OnInitialize on the host's startup/UI thread. GetItemId
    // mutates a shared std::wstring also used by the UI: calling it on the
    // acquisition thread can corrupt the heap. Items live until host restart;
    // HardwareMonitor applies item-list changes on restart too.
    for(int i=0;i<1024;++i){
        IPluginItem* item=plugin->GetItem(i);if(!item)break;
        const wchar_t* id=item->GetItemId();
        if(Equal(id,L"7KrUeFVl_/intelcpu/0/Temperature/Core Average") || Equal(id,L"7KrUeFVl_/amdcpu/0/Temperature/Core Average")) result.cpu=item;
        const wchar_t* path=After(id,L"7KrUeFVl_");
        const wchar_t* field=path&&gpuRoot&&*gpuRoot ? After(path,gpuRoot) : nullptr;
        if(!field)continue;
        if(Equal(field,L"/Temperature/GPU Core"))result.gpuTemperature=item;
        else if(Equal(field,L"/Load/GPU Core"))result.gpuCoreLoad=item;
        else if(Equal(field,L"/Load/D3D 3D"))result.gpuD3dLoad=item;
        else if(Equal(field,L"/Clock/GPU Core"))result.gpuClock=item;
    }
    return result;
}
static PluginValues ReadPluginValues(const PluginItems& items) {
    PluginValues result;
    // Host calls HardwareMonitor and this bridge sequentially on the same
    // acquisition thread. Read its completed cache; do not poll or query IDs.
    if(items.cpu)result.cpu=NumericItem(items.cpu);
    if(items.gpuTemperature)result.gpuTemperature=NumericItem(items.gpuTemperature);
    if(items.gpuClock)result.gpuClock=ParseMHz(items.gpuClock->GetItemValueText());
    double coreLoad=items.gpuCoreLoad?NumericItem(items.gpuCoreLoad):-1;
    double d3dLoad=items.gpuD3dLoad?NumericItem(items.gpuD3dLoad):-1;
    result.gpuLoad=coreLoad>=0&&coreLoad<=100 ? coreLoad : d3dLoad;
    return result;
}
