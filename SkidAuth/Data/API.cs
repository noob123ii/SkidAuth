using System;
using Meta.WitAi.Json;
using SkidAuth.Notifications;
using UnityEngine;

namespace SkidAuth.Data
{
	internal class API
	{
		public static void GetNonce(string token, Action<string> callback)
		{
			Http.Instance.SendPost("https://graph.oculus.com/user_nonce_generate?access_token=" + token, "{}", response =>
			{
				if (response.status_code != 200L)
				{
					NotifiLib.SendNotification("Failed to get nonce", "Error " + response.data, 1f, NotifiReason.Warning);
					Debug.Log("Error " + response.data);
					return;
				}

				var nonce = JsonConvert.DeserializeObject<NonceResponse>(response.data, null, false);
				callback(nonce.nonce);
			});
		}

		public static void GetMe(string token, Action<GetIdsResponse> callback)
		{
			Http.Instance.SendGet("https://graph.oculus.com/me?access_token=" + token, response =>
			{
				if (response.status_code != 200L)
				{
					NotifiLib.SendNotification("Failed to get nonce", "Error " + response.data, 1f, NotifiReason.Error);
					Debug.Log("Error " + response.data);
					return;
				}

				callback(JsonConvert.DeserializeObject<GetIdsResponse>(response.data, null, false));
			});
		}

		public class NonceResponse
		{
			public string nonce { get; set; }
		}

		public class GetIdsResponse
		{
			public string id { get; set; }
			public string org_scoped_id { get; set; }
		}
	}
}