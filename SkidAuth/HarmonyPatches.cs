using System.Reflection;
using HarmonyLib;

namespace SkidAuth
{
	public class HarmonyPatches
	{
		public static bool IsPatched { get; private set; }

		public const string InstanceId = "com.skid.gorillatag.skidauth";

		private static Harmony instance;

		internal static void ApplyHarmonyPatches()
		{
			if (IsPatched)
			{
				return;
			}

			instance ??= new Harmony(InstanceId);
			instance.PatchAll(Assembly.GetExecutingAssembly());
			IsPatched = true;
		}

		internal static void RemoveHarmonyPatches()
		{
			if (instance != null && IsPatched)
			{
				instance.UnpatchSelf();
				IsPatched = false;
			}
		}
	}
}