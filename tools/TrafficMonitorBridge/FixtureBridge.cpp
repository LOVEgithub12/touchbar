#define TOUCHBAR_BRIDGE_MAPPING L"Local\\TouchBar.TrafficMonitor.Cpu.AbiTest"
#define TOUCHBAR_TELEMETRY_MAPPING L"Local\\TouchBar.TrafficMonitor.Telemetry.AbiTest"
#include "upstream/PluginInterface.h"
static ITMPlugin* testPlugin;
#define TOUCHBAR_TEST_PLUGIN testPlugin
#include "TouchBarCpuBridge.cpp"
extern "C" __declspec(dllexport) void SetTestPlugin(ITMPlugin* plugin){testPlugin=plugin;}
