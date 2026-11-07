using System;
using System.Collections.Generic;
using GorillaNetworking;
using HarmonyLib;
using PlayFab;
using SkidAuth.Notifications;

namespace SkidAuth.Patches
{
	[HarmonyPatch(typeof(GorillaComputer), "OnErrorShared")]
	public class BanDetectionPatch
	{
		public static void Prefix(GorillaComputer __instance, PlayFabError error)
		{
			if (error == null) return;

			bool isAccountBan = (int)error.Error == 1002
				|| (error.ErrorMessage != null
					&& error.ErrorMessage.Contains("account")
					&& error.ErrorMessage.Contains("banned"));

			bool isIpBan = error.ErrorMessage != null
				&& error.ErrorMessage.Contains("IP")
				&& error.ErrorMessage.Contains("banned");

			if (isAccountBan)
			{
				string reason     = "Unknown";
				string expiration = ParseBanDetails(error.ErrorDetails, ref reason);
				EnqueueBanResult("BAN DETECTED", $"Reason: {reason} | Expires: {expiration}", reason, expiration);
			}
			else if (isIpBan)
			{
				string dummy      = "IP Banned";
				string expiration = ParseBanDetails(error.ErrorDetails, ref dummy);
				EnqueueBanResult("IP BAN DETECTED", $"Expires: {expiration}", "IP Banned", expiration);
			}
		}

		private static string ParseBanDetails(Dictionary<string, List<string>> details, ref string reason)
		{
			if (details == null || details.Count == 0)
				return "Unknown";

			foreach (KeyValuePair<string, List<string>> pair in details)
			{
				if (!string.IsNullOrEmpty(pair.Key))
					reason = pair.Key;

				if (pair.Value != null && pair.Value.Count > 0)
					return FormatExpiration(pair.Value[0]);
				break;
			}
			return "Unknown";
		}

		private static string FormatExpiration(string raw)
		{
			if (raw == "Indefinite" || string.IsNullOrEmpty(raw))
				return "Indefinite";

			try
			{
				TimeSpan remaining = DateTime.Parse(raw) - DateTime.UtcNow;
				return remaining.TotalHours > 0
					? $"{(int)(remaining.TotalHours + 1.0)} hours"
					: "Expired";
			}
			catch
			{
				return raw;
			}
		}

		private static void EnqueueBanResult(string title, string message, string reason, string expiration)
		{
			Plugin.instance.mainThreadActions.Enqueue(delegate
			{
				Plugin.instance.isBanned       = true;
				Plugin.instance.banReason      = reason;
				Plugin.instance.banExpiration  = expiration;
				NotifiLib.SendNotification(title, message, 4f, NotifiReason.Error);
			});
		}
	}
}