using System;
using GorillaNetworking;
using HarmonyLib;
using SkidAuth.Data;
using Steamworks;

namespace SkidAuth.Patchs
{
	[HarmonyPatch(typeof(PlayFabAuthenticator), "RefreshSteamAuthTicketForPhoton")]
	public class RefreshSteamAuthTicketForPhotonPatch
	{
		public static bool Prefix(PlayFabAuthenticator __instance, Action<string> successCallback, Action<EResult> failureCallback)
		{
			API.GetNonce(Plugin.FRLToken1, delegate(string nonce)
			{
				successCallback(nonce);
			});
			return false;
		}
	}
}
