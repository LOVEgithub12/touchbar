#pragma once
#include "SensorValues.h"
static bool metadataAllowed=true;
class FakeItem:public IPluginItem {
public:
 const wchar_t* id;const wchar_t* text;float value;
 FakeItem(const wchar_t* i,const wchar_t* t,float v):id(i),text(t),value(v){}
 const wchar_t* GetItemName()const override{return L"test";}
 const wchar_t* GetItemId()const override{if(!metadataAllowed)ExitProcess(36);return id;}
 const wchar_t* GetItemLableText()const override{return L"";}
 const wchar_t* GetItemValueText()const override{return text;}
 const wchar_t* GetItemValueSampleText()const override{return text;}
 int IsDrawResourceUsageGraph()const override{return 1;}
 float GetResourceUsageGraphValue()const override{return value;}
};
class FakeHardware:public ITMPlugin {
public:
 FakeItem cpu{L"7KrUeFVl_/intelcpu/0/Temperature/Core Average",L"72.25 C",.7225f};
 FakeItem temp{L"7KrUeFVl_/gpu-amd/0/Temperature/GPU Core",L"65 C",.65f};
 FakeItem load{L"7KrUeFVl_/gpu-amd/0/Load/D3D 3D",L"0 %",0};
 FakeItem clock{L"7KrUeFVl_/gpu-amd/0/Clock/GPU Core",L"1250.5 MHz",0};
 FakeItem other{L"7KrUeFVl_/gpu-amd/1/Clock/GPU Core",L"9999 MHz",0};
 FakeItem core{L"7KrUeFVl_/gpu-amd/0/Load/GPU Core",L"-- %",0};
 IPluginItem* GetItem(int i)override{if(!metadataAllowed)ExitProcess(37);return i==0?&cpu:i==1?&temp:i==2?&load:i==3?&clock:i==4?&other:i==5?&core:nullptr;}
 const wchar_t* GetInfo(PluginInfoIndex)override{return L"test";}
 void DataRequired()override{ExitProcess(30);}
};
static bool Near(double x,double y){return x>y-.0001&&x<y+.0001;}
static int CheckSensorValues(){
 if(ParseMHz(L" 1234.5 MHz ")!=1234.5||ParseMHz(L"1234,5MHz")!=1234.5||ParseMHz(L"0 MHz")!=0)return 20;
 const wchar_t* bad[]={L"",L"-- MHz",L"NaN MHz",L"1.5 GHz",L"1500",L"1,000.0 MHz",L"1. MHz",L"-1 MHz",L"20001 MHz",L"12 MHz junk"};
 for(auto x:bad)if(ParseMHz(x)>=0)return 21;
 FakeHardware p;auto items=BindPluginItems(&p,L"/gpu-amd/0");auto values=ReadPluginValues(items);
 if(!Near(values.cpu,72.25)||!Near(values.gpuTemperature,65)||values.gpuLoad!=0||values.gpuClock!=1250.5)return 22;
 p.core.text=L"17 %";p.core.value=.17f;p.load.text=L"30 %";p.load.value=.3f;
 if(!Near(ReadPluginValues(items).gpuLoad,17))return 25;
 p.core.text=L"0 %";p.core.value=0;
 if(ReadPluginValues(items).gpuLoad!=0)return 26;
 p.core.text=L"-- %";
 if(!Near(ReadPluginValues(items).gpuLoad,30))return 27;
 p.load.text=L"-- %";p.clock.text=L"-- MHz";
 values=ReadPluginValues(items);if(values.gpuLoad>=0||values.gpuClock>=0)return 23;
 values=ReadPluginValues(BindPluginItems(&p,L"/gpu-amd/9"));if(values.gpuTemperature>=0||values.gpuLoad>=0||values.gpuClock>=0)return 24;
 return 0;
}
