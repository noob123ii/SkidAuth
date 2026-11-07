using System;
using HarmonyLib;

namespace SkidAuth.Patchs
{
	[HarmonyPatch(typeof(MothershipAuthenticator), "Awake")]
	public class AwakePatch
	{
		public static bool Prefix(MothershipAuthenticator __instance)
		{
			return false;
		}
	}
}
