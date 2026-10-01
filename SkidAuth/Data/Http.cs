using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace SkidAuth.Data
{
	public class Http : MonoBehaviour
	{
		public static Http Instance
		{
			get
			{
				if (_cached == null)
				{
					GameObject go = new GameObject("http");
					_cached = go.AddComponent<Http>();
					UnityEngine.Object.DontDestroyOnLoad(go);
				}
				return _cached;
			}
		}

		public void SendPost(string url, string data, Action<HttpResponse> callback)
		{
			StartCoroutine(SendWeb("POST", url, data, callback));
		}

		public void SendGet(string url, Action<HttpResponse> callback)
		{
			StartCoroutine(SendWeb("GET", url, null, callback));
		}

		private IEnumerator SendWeb(string method, string url, string data, Action<HttpResponse> callback)
		{
			UnityWebRequest request = new UnityWebRequest(url, method);

			if (!string.IsNullOrEmpty(data))
			{
				request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(data));
			}
			request.downloadHandler = new DownloadHandlerBuffer();
			request.SetRequestHeader("Content-Type", "application/json");

			yield return request.SendWebRequest();

			callback?.Invoke(new HttpResponse
			{
				status_code = request.responseCode,
				data = request.downloadHandler.text
			});

			request.Dispose();
		}

		private static Http _cached;
	}
}