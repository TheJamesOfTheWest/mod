// ReShade add-on entry point: SotfPassthrough.addon64 (goes next to SonsOfTheForest.exe).
#include "compositor.h"
#include <windows.h>
#include <reshade.hpp>

extern "C" __declspec(dllexport) extern const char *NAME = "Minecraft Passthrough";
extern "C" __declspec(dllexport) const char *DESCRIPTION = "Draws Minecraft (Fabric passthrough mod) into Sons of the Forest, depth-tested against the game's depth buffer.";

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
	switch (reason)
	{
	case DLL_PROCESS_ATTACH:
		if (!compositor::try_register(module))
			return FALSE;
		break;
	case DLL_PROCESS_DETACH:
		compositor::unregister(module);
		break;
	}
	return TRUE;
}
