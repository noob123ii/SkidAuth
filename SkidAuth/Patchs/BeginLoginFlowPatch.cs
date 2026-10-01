using System;
using GorillaNetworking;
using HarmonyLib;

namespace SkidAuth.Patchs
{
	[HarmonyPatch(typeof(PlayFabAuthenticator), "BeginLoginFlow")]
	public class BeginLoginFlowPatch
	{
		public static bool Prefix(PlayFabAuthenticator __instance)
		{
			return false;
		}
	}
}
