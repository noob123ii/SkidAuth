using System;
using System.Collections.Generic;
using HarmonyLib;

namespace SkidAuth.Patchs
{
	[HarmonyPatch(typeof(VRRig))]
	[HarmonyPatch("IUserCosmeticsCallback.OnGetUserCosmetics", 0)]
	public static class CosmeticsPlatformPatch
	{
		private static void Postfix(VRRig __instance)
		{
			DetectAsync(__instance);
		}

		private static async void DetectAsync(VRRig rig)
		{
			if (rig == null || rig.Creator == null) return;

			string userId = rig.Creator.UserId;
			if (string.IsNullOrEmpty(userId)) return;

			if (creationDateCache.TryGetValue(userId, out DateTime created))
			{
				string platform = created > OculusPayDay ? "QUEST" : "OCULUS PC";
				SetPlatform(rig.Creator.ActorNumber, platform);
				return;
			}

			var cosmeticsField = Plugin.CosmeticsField;
			if (cosmeticsField != null)
			{
				var cosmetics = cosmeticsField.GetValue(rig) as HashSet<string>;
				if (cosmetics != null && cosmetics.Count > 0)
				{
					SetPlatform(rig.Creator.ActorNumber, "QUEST");
					return;
				}
			}

			SetPlatform(rig.Creator.ActorNumber, "?");
		}

		private static void SetPlatform(int actor, string platform)
		{
			if (Plugin.instance == null) return;

			Queue<Action> actions = Plugin.instance.mainThreadActions;
			lock (actions)
			{
				Plugin.instance.mainThreadActions.Enqueue(() =>
				{
					if (Plugin.instance.espData.TryGetValue(actor, out Plugin.PlayerESPData data))
					{
						data.platform = platform;
					}
				});
			}
		}

		private static readonly DateTime OculusPayDay = new DateTime(2023, 2, 6);
		private static readonly Dictionary<string, DateTime> creationDateCache = new Dictionary<string, DateTime>();
	}
}