using System;
using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace SkidAuth.Patchs
{
	[HarmonyPatch(typeof(PhotonAuthenticator), "SetCustomAuthenticationParameters")]
	public class SetCustomAuthenticationParameters
	{
		public static void Prefix(PhotonAuthenticator __instance, Dictionary<string, object> customAuthData)
		{
			customAuthData.Add("Platform", "PC");
			Debug.Log(JsonConvert.SerializeObject(customAuthData));
		}
	}
}
