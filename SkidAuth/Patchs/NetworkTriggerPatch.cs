using System;
using GorillaNetworking;
using HarmonyLib;

namespace SkidAuth.Patchs
{
	[HarmonyPatch(typeof(GorillaNetworkJoinTrigger), "OnBoxTriggered")]
	public class NetworkTriggerPatch
	{
		public static bool Prefix()
		{
			return !NetworkTriggerPatch.enabled;
		}

		public static bool enabled;
	}
}
