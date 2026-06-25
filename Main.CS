using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Runs;

namespace BaiMod;

[ModInitializer("Init")]
public static class Main
{
	
	private const string ModId = "BaiMod";

	private static bool _initialized;

	private static Harmony? _harmony;

	public static void Init()
	{
		if (!_initialized)
		{
			_initialized = true;

			_harmony = new Harmony(ModId);
       		_harmony.PatchAll();
			
			Log.Info("Initializing BaiMod");
		}

		
	}
}
