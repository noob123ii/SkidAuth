using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using BepInEx;
using ExitGames.Client.Photon;
using GorillaLocomotion;
using GorillaNetworking;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using Photon.Voice.PUN;
using Newtonsoft.Json;
using PlayFab;
using SkidAuth.Data;
using SkidAuth.Notifications;
using SkidAuth.Patchs;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SkidAuth
{
	[BepInPlugin("com.skid.gorillatag.skidauth", "skidauth", "1.0.0")]
	public sealed class Plugin : BaseUnityPlugin
	{
		public static Plugin instance { get; private set; }

		public static string FRLToken1
		{
			get
			{
				return FRLToken;
			}
		}

		private static FieldInfo RigFpsField
		{
			get
			{
				return _lazyRigFps.Value;
			}
		}

		internal static FieldInfo CosmeticsField
		{
			get
			{
				return _lazyCosmeticsField.Value;
			}
		}

		private static ThreatLevel EvaluateThreat(PlayerESPData d)
		{
			if (d == null)
			{
				return ThreatLevel.None;
			}

			if (d.hasConsoleMod && d.hasCosmetX)
			{
				return ThreatLevel.Critical;
			}

			if (d.hasConsoleMod || d.hasCosmetX)
			{
				return ThreatLevel.High;
			}

			if (d.isReporting)
			{
				return d.velocity > 10f ? ThreatLevel.High : ThreatLevel.Medium;
			}

			if (d.velocity > 20f)
			{
				return ThreatLevel.Medium;
			}

			return d.velocity > 10f ? ThreatLevel.Low : ThreatLevel.None;
		}

		private static string FpsColored(float fps)
		{
			return fps switch
			{
				<= 0.5f => "<color=#F0A8A8>--</color>",
				< 30f => FormatStat(fps, "F0", "F0A8A8"),
				< 45f => FormatStat(fps, "F0", "F0CCA8"),
				< 60f => FormatStat(fps, "F0", "F0ECA8"),
				< 90f => FormatStat(fps, "F0", "C0F0A8"),
				_ => FormatStat(fps, "F0", "A8F0C0")
			};
		}

		private static string FormatStat(float value, string format, string colour)
		{
			return string.Format("<color=#{0}>{1}</color>", colour, value.ToString(format));
		}

		private static string PingColored(float ping)
		{
			return ping switch
			{
				>= 200f => FormatStat(ping, "F0ms", "F0A8A8"),
				>= 120f => FormatStat(ping, "F0ms", "F0CCA8"),
				>= 80f => FormatStat(ping, "F0ms", "F0ECA8"),
				>= 40f => FormatStat(ping, "F0ms", "C0F0A8"),
				_ => FormatStat(ping, "F0ms", "A8F0C0")
			};
		}

		private static string VelocityColored(float v)
		{
			return v switch
			{
				>= 12f => FormatStat(v, "F1m/s", "F0A8A8"),
				>= 6f => FormatStat(v, "F1m/s", "F0ECA8"),
				>= 2f => FormatStat(v, "F1m/s", "A8F0C0"),
				_ => FormatStat(v, "F1m/s", "9988BB")
			};
		}

		private static string PlatformDisplay(string raw)
		{
			return PlatformDisplayMap.TryGetValue(raw ?? "?", out string display)
				? display
				: "<color=#888888>" + raw + "</color>";
		}

		private static (float r, float g, float b) DecomposeColor(Color c)
		{
			return (c.r, c.g, c.b);
		}

		private static float GetGameVelocityLimit()
		{
			try
			{
				if (GTPlayer.Instance != null && GTPlayer.Instance.velocityLimit > 0f)
				{
					return GTPlayer.Instance.velocityLimit;
				}
			}
			catch (Exception)
			{
			}

			return 25f;
		}

		private void Awake()
		{
			instance = this;
			this.whiteTex = Texture2D.whiteTexture;
			this.transparentTex = BuildSolidTexture(new Color(0f, 0f, 0f, 0f));
			HarmonyPatches.ApplyHarmonyPatches();
			this.TryInitFonts();
			base.StartCoroutine(this.LoadLogoTexture());
			this.timeOffset = UnityEngine.Random.Range(0f, 100f);
			NotifiLib.SendNotification("SkidAuth Loaded", "Version 1.0.0", 2f, NotifiReason.Info);
			this.InitializeBadgeDefinitions();
			base.StartCoroutine(this.DownloadBadgeImages());
			GorillaTagger.OnPlayerSpawned(new Action(OnPlayerSpawnedWebhook));
			this.LoadSettings();
		}

		private static bool IsRemotePlayerRig(VRRig r)
		{
			return r?.Creator != null && r != (GorillaTagger.Instance != null ? GorillaTagger.Instance.offlineVRRig : null);
		}

		private static Texture2D BuildSolidTexture(Color c)
		{
			Texture2D texture2D = new Texture2D(1, 1);
			texture2D.SetPixel(0, 0, c);
			texture2D.Apply();
			return texture2D;
		}

		private void TryInitFonts()
		{
			try
			{
				this.customFont = (Font.CreateDynamicFontFromOSFont("Segoe UI", 14) ?? Font.CreateDynamicFontFromOSFont("Arial", 14));
				this.fontLoaded = (this.customFont != null);
				if (this.fontLoaded)
				{
					this.customFont.material.mainTexture.filterMode = FilterMode.Point;
				}
				string[] source = new string[]
				{
					"Impact",
					"Haettenschweiler",
					"Arial Black",
					"Franklin Gothic Heavy",
					"Arial Narrow",
					"Arial"
				};
				this.streetFont = source
					.Select(name => Font.CreateDynamicFontFromOSFont(name, 17))
					.FirstOrDefault(font => font != null);
				this.streetFontSmall = source
					.Select(name => Font.CreateDynamicFontFromOSFont(name, 11))
					.FirstOrDefault(font => font != null);
				if (this.streetFont != null)
				{
					this.streetFont.material.mainTexture.filterMode = FilterMode.Point;
				}
				if (this.streetFontSmall != null)
				{
					this.streetFontSmall.material.mainTexture.filterMode = FilterMode.Point;
				}
			}
			catch (Exception)
			{
			}
		}

		private static void OnPlayerSpawnedWebhook()
		{
			NetworkSystem.Instance.OnJoinedRoomEvent += OnJoinRoomWebhook;
			NetworkSystem.Instance.OnReturnedToSinglePlayer += OnLeaveRoomWebhook;
		}

		private static void OnJoinRoomWebhook()
		{
			if (_inRoomStatus || PhotonNetwork.CurrentRoom == null || VRRigCache.ActiveRigs == null)
			{
				return;
			}

			_inRoomStatus = true;

			if (instance == null)
			{
				return;
			}

			if (instance.roomAnalyticsEnabled)
			{
				instance.currentRoomAnalytics = new RoomAnalytics
				{
					RoomName = PhotonNetwork.CurrentRoom.Name,
					StartTime = Time.time
				};
			}

			instance.StartCoroutine(ScanForTrackedPlayersDelayed());
		}

		private static void OnLeaveRoomWebhook()
		{
			if (!_inRoomStatus)
			{
				return;
			}

			_inRoomStatus = false;
			_sentThisRoom.Clear();

			if (instance == null || instance.currentRoomAnalytics == null)
			{
				return;
			}

			foreach (PlayerSessionRecord record in instance.currentRoomAnalytics.Sessions.Where(s => s.IsActive))
			{
				record.LeaveTime = Time.time;
			}
		}

		private static IEnumerator ScanForTrackedPlayersDelayed()
		{
			yield return new WaitForSeconds(5f);
			if (VRRigCache.Instance == null || VRRigCache.ActiveRigs == null)
			{
				yield break;
			}
			foreach (VRRig vrrig in VRRigCache.ActiveRigs)
			{
				if (vrrig == null || vrrig.Creator == null)
				{
					continue;
				}
				PlayerESPData esp = null;
				if (instance != null)
				{
					instance.espData.TryGetValue(vrrig.Creator.ActorNumber, out esp);
				}
				string uid = vrrig.Creator.UserId ?? string.Empty;
				string uname = vrrig.Creator.NickName ?? "Unknown";
				string room = PhotonNetwork.CurrentRoom?.Name ?? "—";
				string plat = GetPlatformStatic(vrrig);
				if (TrackedBadgeDefs != null)
				{
					string rawCosmetic = GetRawCosmeticStringStatic(vrrig);
					foreach (ValueTuple<string, string, string> def in TrackedBadgeDefs)
					{
						TryTrackBadge(rawCosmetic, uid, room, uname, plat, def.Item1, def.Item2, def.Item3);
					}
				}
				if (TrackedSpecialIds != null)
				{
					foreach (ValueTuple<string, string, string> def2 in TrackedSpecialIds)
					{
						TryTrackSpecialId(uid, def2.Item1, def2.Item2, room, uname, plat, def2.Item3);
					}
				}
			}
		}

		private static void TryTrackBadge(string raw, string uid, string room, string uname, string plat, string code, string label, string thumb)
		{
			bool flag = !raw.Contains(code) || !_sentThisRoom.Add(string.Concat(new string[]
			{
				room,
				"|",
				uid,
				"|",
				label
			}));
			if (!flag)
			{
				SendWebhookEmbed(label, uname, uid, room, plat, thumb);
				SendUnknownEmbedToSecondWebhook(label, thumb);
			}
		}

		private static void TryTrackSpecialId(string uid, string targetId, string label, string room, string uname, string plat, string thumb)
		{
			bool flag = uid != targetId || !_sentThisRoom.Add(string.Concat(new string[]
			{
				room,
				"|",
				uid,
				"|",
				label
			}));
			if (!flag)
			{
				SendWebhookEmbed(label, uname, uid, room, plat, thumb);
				SendUnknownEmbedToSecondWebhook(label, thumb);
			}
		}

		private static void SendWebhookEmbed(string tracked, string username, string userid, string room, string platform, string thumbnailUrl)
		{
			var embedObj = new
			{
				title = "「  FOUND " + tracked.ToUpper() + "  」",
				color = 0,
				author = new
				{
					name = "Tracking Hub Board",
					icon_url = AuthorIcon
				},
				fields = new object[]
				{
					new
					{
						name = "\ud83c\udf0d | Room Code",
						value = room,
						inline = false
					},
					new
					{
						name = "\ud83d\udc64 | Username",
						value = username,
						inline = true
					},
					new
					{
						name = "\ud83d\udd11 | User ID",
						value = userid,
						inline = true
					},
					new
					{
						name = "\ud83d\udda5️ | Platform",
						value = platform,
						inline = true
					}
				},
				thumbnail = new
				{
					url = thumbnailUrl
				},
				footer = new
				{
					text = "Quote: Best Gorilla Tag Player Tracker"
				}
			};
			SendEmbedToWebhook(embedObj, WebhookURL, "<@&1414723976445558895>");
		}

		private static void SendUnknownEmbedToSecondWebhook(string cosmeticName, string thumbnailUrl)
		{
			var embedObj = new
			{
				author = new
				{
					name = "Tracking Hub Board",
					icon_url = AuthorIcon
				},
				title = "「 FOUND " + cosmeticName.ToUpper() + " 」",
				color = 0,
				thumbnail = new
				{
					url = thumbnailUrl
				},
				fields = new object[]
				{
					new
					{
						name = "\ud83c\udf0d | Room Code",
						value = "**Unknown**",
						inline = false
					},
					new
					{
						name = "\ud83d\udc64 | Username",
						value = "**Unknown**",
						inline = true
					},
					new
					{
						name = "\ud83d\udd11 | PlayFab ID",
						value = "**Unknown**",
						inline = true
					},
					new
					{
						name = "\ud83d\udda5️ | Platform",
						value = "**Unknown**",
						inline = true
					},
					new
					{
						name = "\ud83d\udd12 | Tracker",
						value = "**Buy To Find Out The Unknown**",
						inline = false
					}
				},
				footer = new
				{
					text = "Quote: Best Gorilla Tag Player Tracker"
				}
			};
			SendEmbedToWebhook(embedObj, WebhookURL2, null);
		}

		private static void SendEmbedToWebhook(object embedObj, string webhookUrl, string content)
		{
			if (instance == null)
			{
				return;
			}

			instance.StartCoroutine(PostWebhookCoroutine(embedObj, webhookUrl, content));
		}

		private static IEnumerator PostWebhookCoroutine(object embedObj, string webhookUrl, string content)
		{
			string json = JsonConvert.SerializeObject(embedObj);
			if (!string.IsNullOrEmpty(content))
			{
				json = "{\"content\":\"" + content.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"," + json.TrimStart('{');
			}
			byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
			using (var www = new UnityEngine.Networking.UnityWebRequest(webhookUrl, "POST"))
			{
				www.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(bodyRaw);
				www.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
				www.SetRequestHeader("Content-Type", "application/json");
				yield return www.SendWebRequest();
				if (www.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
				{
					UnityEngine.Debug.LogWarning("[SkidAuth] Webhook failed: " + www.error);
				}
			}
		}

		private static string GetRawCosmeticStringStatic(VRRig rig)
		{
			if (rig == null || rig.Creator == null)
			{
				return string.Empty;
			}

			if (_playerOwnedCosmeticsFieldStatic == null)
			{
				_playerOwnedCosmeticsFieldStatic = typeof(VRRig).GetField("_playerOwnedCosmetics", BindingFlags.Instance | BindingFlags.NonPublic);
			}

			object raw = _playerOwnedCosmeticsFieldStatic?.GetValue(rig);
			if (raw == null)
			{
				return string.Empty;
			}

			if (raw is string singleValue)
			{
				return singleValue;
			}

			if (raw is IEnumerable<object> manyValues)
			{
				return string.Join(".", manyValues.Select(o => o?.ToString() ?? string.Empty));
			}

			return raw.ToString() ?? string.Empty;
		}

		private static string GetPlatformStatic(VRRig rig)
		{
			if (rig == null || rig.Creator == null)
			{
				return "UNKNOWN";
			}

			string cosmetics = GetRawCosmeticStringStatic(rig);
			if (cosmetics.Contains("S. FIRST LOGIN"))
			{
				return "STEAM";
			}

			if (cosmetics.Contains("FIRST LOGIN"))
			{
				return "PC";
			}

			try
			{
				PhotonView view = rig.GetComponent<PhotonView>();
				ExitGames.Client.Photon.Hashtable properties = view?.Owner?.CustomProperties;
				if (properties != null && properties.Count >= 2)
				{
					return "PC";
				}
			}
			catch (Exception)
			{
			}

			return "STANDALONE";
		}

		private static bool CheckCosmetX(VRRig rig)
		{
			try
			{
				if (rig == null)
				{
					return false;
				}

				if (_playerOwnedCosmeticsFieldStatic == null)
				{
					_playerOwnedCosmeticsFieldStatic = typeof(VRRig).GetField("_playerOwnedCosmetics", BindingFlags.Instance | BindingFlags.NonPublic);
				}

				if (!(_playerOwnedCosmeticsFieldStatic?.GetValue(rig) is HashSet<string> owned))
				{
					return false;
				}

				CosmeticsController.CosmeticSet cosmeticSet = rig.cosmeticSet;
				return cosmeticSet.items != null
					&& cosmeticSet.items.Any(c => !c.isNullItem && !owned.Contains(c.itemName));
			}
			catch (Exception)
			{
				return false;
			}
		}

		private void LoadSettings()
		{
			this.selectedThemeIndex = PlayerPrefs.GetInt("SA_ThemeIndex", 4);
			this.ApplyColourPreset(colourPresets[this.selectedThemeIndex]);
			this.showOverlay = (PlayerPrefs.GetInt("SA_ShowOverlay", 1) == 1);
			this.playerTracers = (PlayerPrefs.GetInt("SA_PlayerTracers", 0) == 1);
			this.lavaTrails = (PlayerPrefs.GetInt("SA_LavaTrails", 0) == 1);
			this.autoReauth = (PlayerPrefs.GetInt("SA_AutoReauth", 0) == 1);
			this.testBadgeMode = (PlayerPrefs.GetInt("SA_TestBadge", 0) == 1);
			this.disableNetworkTriggers = (PlayerPrefs.GetInt("SA_NetworkTriggers", 0) == 1);
			this.badgeTracers = (PlayerPrefs.GetInt("SA_BadgeTracers", 0) == 1);
			this.speedDetection = (PlayerPrefs.GetInt("SA_SpeedDetection", 0) == 1);
			this.showThreatScores = (PlayerPrefs.GetInt("SA_ThreatScores", 1) == 1);
			this.roomAnalyticsEnabled = (PlayerPrefs.GetInt("SA_RoomAnalytics", 1) == 1);
			this.textGradientStrength = PlayerPrefs.GetFloat("SA_GradStrength", 0.5f);
			this.speedThreshold = PlayerPrefs.GetFloat("SA_SpeedThreshold", 14f);
			try
			{
				this.toggleKey = (KeyCode)PlayerPrefs.GetInt("SA_ToggleKey", 277);
			}
			catch (Exception)
			{
				this.toggleKey = KeyCode.Insert;
			}
		}

		private void SaveSettings()
		{
			PlayerPrefs.SetInt("SA_ThemeIndex", this.selectedThemeIndex);
			PlayerPrefs.SetInt("SA_ShowOverlay", this.showOverlay ? 1 : 0);
			PlayerPrefs.SetInt("SA_PlayerTracers", this.playerTracers ? 1 : 0);
			PlayerPrefs.SetInt("SA_LavaTrails", this.lavaTrails ? 1 : 0);
			PlayerPrefs.SetInt("SA_AutoReauth", this.autoReauth ? 1 : 0);
			PlayerPrefs.SetInt("SA_TestBadge", this.testBadgeMode ? 1 : 0);
			PlayerPrefs.SetInt("SA_NetworkTriggers", this.disableNetworkTriggers ? 1 : 0);
			PlayerPrefs.SetInt("SA_BadgeTracers", this.badgeTracers ? 1 : 0);
			PlayerPrefs.SetInt("SA_SpeedDetection", this.speedDetection ? 1 : 0);
			PlayerPrefs.SetInt("SA_ThreatScores", this.showThreatScores ? 1 : 0);
			PlayerPrefs.SetInt("SA_RoomAnalytics", this.roomAnalyticsEnabled ? 1 : 0);
			PlayerPrefs.SetFloat("SA_GradStrength", this.textGradientStrength);
			PlayerPrefs.SetFloat("SA_SpeedThreshold", this.speedThreshold);
			PlayerPrefs.SetInt("SA_ToggleKey", (int)this.toggleKey);
			PlayerPrefs.Save();
		}

		private void InitializeBadgeDefinitions()
		{
			this.badgeDefinitions["LBANI."] = CreateBadgeInfo("LBANI.", "AABadge.png", "https://cdn.discordapp.com/attachments/1256418157417992334/1403060782572372059/Screenshot_2025-08-07_180114-removebg-preview.png?ex=69ae546e&is=69ad02ee&hm=f31a8208c8dde2e59ae9d012252211d22f59705afcd66d46d1f27d14b5d8d219&", "https://cdn.discordapp.com/attachments/1256418157417992334/1403060782572372059/Screenshot_2025-08-07_180114-removebg-preview.png", new Color(0.05f, 0.05f, 0.05f, 0.85f));
			this.badgeDefinitions["LBAGS."] = CreateBadgeInfo("LBAGS.", "illustrator.png", "https://cdn.discordapp.com/attachments/1245897958013145230/1259632896851837042/IllustratorbadgeTransparent.webp?ex=69ae9ccd&is=69ad4b4d&hm=b133082a3b39f09b944c14df620338dabf0273efa00c96b5dcd1d1d2eb8c251c&", "https://cdn.discordapp.com/attachments/1245897958013145230/1259632896851837042/IllustratorbadgeTransparent.webp", new Color(0.95f, 0.95f, 0.95f, 0.75f));
			this.badgeDefinitions["LBAAK."] = CreateBadgeInfo("LBAAK.", "mod_stick.png", "https://media.discordapp.net/attachments/1261535966259056764/1263290491953217587/Sticks.png?ex=69aebc34&is=69ad6ab4&hm=59e38f2c1f4969f0d0eb8e3ef3b6788c1a1c265e2e3bd0a304cd00aaa4338728&", "https://media.discordapp.net/attachments/1261535966259056764/1263290491953217587/Sticks.png", new Color(0.55f, 0.32f, 0.08f, 0.8f));
			this.badgeDefinitions["LBADE."] = CreateBadgeInfo("LBADE.", "FingerPainter.png", "https://media.discordapp.net/attachments/1261535966259056764/1263290492611858453/Finger.png?ex=69aebc34&is=69ad6ab4&hm=65ea419d0dfea2913af38002ba2d525a500352f8adc75ec0099df87e90780082&", "https://media.discordapp.net/attachments/1261535966259056764/1263290492611858453/Finger.png", new Color(0.85f, 0.08f, 0.08f, 0.8f));
			this.badgeDefinitions["LMAPY."] = CreateBadgeInfo("LMAPY.", "fire_stick.png", "https://cdn.discordapp.com/attachments/1332368975190818897/1380648529789325382/forest_guide_stick-removebg-preview.png?ex=69ae8864&is=69ad36e4&hm=89be98c05f6f10a9b2fd19be64229810c1a1c7943064437c05c00469a93bee51&", "https://cdn.discordapp.com/attachments/1332368975190818897/1380648529789325382/forest_guide_stick-removebg-preview.png", new Color(0.9f, 0.5f, 0.1f, 0.85f));
			this.badgeDefinitions["LBAAD."] = CreateBadgeInfo("LBAAD.", "admin_badge.png", "https://media.discordapp.net/attachments/1402721963033759844/1472309870811287654/noFilter.png?ex=69ae732b&is=69ad21ab&hm=4d77956b3fe279a2adf8b758ccf7798e16f988f3a21e7791928419cf4cf50806&", "https://media.discordapp.net/attachments/1402721963033759844/1472309870811287654/noFilter.png", new Color(0.2f, 0.6f, 1f, 0.8f));
			this.specialIdBadgeDefinitions["1D6E20BE9655C798"] = CreateBadgeInfo("ID_TTTPIG", "tttpig_badge.png", "https://media.discordapp.net/attachments/1402730733507969064/1464011408927621406/OIP.png?ex=69ae951f&is=69ad439f&hm=e2c92457d6d7737d464c356906888ea586564e0f87d67931b92bd744c27241da&=&format=webp&quality=lossless&width=130&height=130", "https://media.discordapp.net/attachments/1402730733507969064/1464011408927621406/OIP.png", new Color(0.8f, 0.6f, 0.1f, 0.8f));
			this.specialIdBadgeDefinitions["D6971CA01F82A975"] = CreateBadgeInfo("ID_ELLIOT", "elliot_badge.png", "https://cdn.discordapp.com/attachments/1402733766140498025/1417265746274422854/9k.png?ex=69ae9755&is=69ad45d5&hm=6b7bf7b0f1d1dd880137599f87e8aff0353b19e18779a534e55ce4709d77dea2&", "https://cdn.discordapp.com/attachments/1402733766140498025/1417265746274422854/9k.png", new Color(0.85f, 0.08f, 0.08f, 0.8f));
			this.specialIdBadgeDefinitions["B4E45E48C5CE0656"] = CreateBadgeInfo("ID_BODA", "boda_badge.png", "https://cdn.discordapp.com/attachments/1462142469729943725/1463263039397560494/OIP.png?ex=69ae7f26&is=69ad2da6&hm=b696d161bd7c89746b7c0b5c221df4be3371f7cce129f82f9357feafea53a824&", "https://cdn.discordapp.com/attachments/1462142469729943725/1463263039397560494/OIP.png", new Color(0.55f, 0.22f, 0.8f, 0.8f));
			this.specialIdBadgeDefinitions["28579AFACDE1FB19"] = CreateBadgeInfo("ID_PEPSI", "pepsi_badge.png", "https://media.discordapp.net/attachments/1402730733507969064/1463564348554481695/channels4_profile.jpg?ex=69aeef04&is=69ad9d84&hm=166ef4212f46888342942f89b688618b70d110aef5a89185139ee340b870d529&format=webp&", "https://media.discordapp.net/attachments/1402730733507969064/1463564348554481695/channels4_profile.jpg", new Color(0.1f, 0.3f, 0.9f, 0.8f));
		}

		private IEnumerator DownloadBadgeImages()
		{
			foreach (var kvp in this.badgeDefinitions)
			{
				BadgeInfo badge = kvp.Value;
				if (badge.Texture != null) continue;
				string folder = EnsureBadgeFolder();
				string path = Path.Combine(folder, badge.FileName);
				if (File.Exists(path))
				{
					byte[] data = File.ReadAllBytes(path);
					Texture2D tex = new Texture2D(2, 2);
					LoadImageData(tex, data);
					badge.Texture = tex;
					this.CreateMaterialForBadge(badge);
				}
				else
				{
					using (var www = new UnityEngine.Networking.UnityWebRequest(badge.Url))
					{
						www.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
						yield return www.SendWebRequest();
						if (www.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
						{
							File.WriteAllBytes(path, www.downloadHandler.data);
							Texture2D tex2 = new Texture2D(2, 2);
							LoadImageData(tex2, www.downloadHandler.data);
							badge.Texture = tex2;
							this.CreateMaterialForBadge(badge);
						}
					}
				}
			}
			foreach (var kvp2 in this.specialIdBadgeDefinitions)
			{
				BadgeInfo badge2 = kvp2.Value;
				if (badge2.Texture != null) continue;
				string folder2 = EnsureBadgeFolder();
				string path2 = Path.Combine(folder2, badge2.FileName);
				if (File.Exists(path2))
				{
					byte[] data2 = File.ReadAllBytes(path2);
					Texture2D tex3 = new Texture2D(2, 2);
					LoadImageData(tex3, data2);
					badge2.Texture = tex3;
					this.CreateMaterialForBadge(badge2);
				}
				else
				{
					using (var www2 = new UnityEngine.Networking.UnityWebRequest(badge2.Url))
					{
						www2.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
						yield return www2.SendWebRequest();
						if (www2.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
						{
							File.WriteAllBytes(path2, www2.downloadHandler.data);
							Texture2D tex4 = new Texture2D(2, 2);
							LoadImageData(tex4, www2.downloadHandler.data);
							badge2.Texture = tex4;
							this.CreateMaterialForBadge(badge2);
						}
					}
				}
			}
			yield break;
		}

		private static string EnsureBadgeFolder()
		{
			string text = Path.Combine(Application.dataPath, "../SkidAuth");
			if (!Directory.Exists(text))
			{
				Directory.CreateDirectory(text);
			}
			return text;
		}

		private void CreateMaterialForBadge(BadgeInfo badge)
		{
			Shader shader;
			if ((shader = Shader.Find("Sprites/Default")) == null)
			{
				shader = (Shader.Find("Unlit/Transparent") ?? Shader.Find("Unlit/Texture"));
			}
			Shader shader2 = shader;
			badge.Material = new Material(shader2)
			{
				mainTexture = badge.Texture
			};
			badge.Material.color = Color.white;
			Material material = new Material(shader2)
			{
				mainTexture = this.CreateCircularGlowTex(128, badge.GlowColor)
			};
			material.color = Color.white;
			badge.GlowMaterial = material;
		}

		private Texture2D CreateCircularGlowTex(int size, Color col)
		{
			Texture2D texture2D = new Texture2D(size, size, TextureFormat.RGBA32, false)
			{
				hideFlags = HideFlags.HideAndDontSave,
				filterMode = FilterMode.Bilinear
			};
			float centre = size * 0.5f;
			Color[] pixels = new Color[size * size];
			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					float dx = x + 0.5f - centre;
					float dy = y + 0.5f - centre;
					float falloff = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) / centre);
					pixels[y * size + x] = new Color(col.r, col.g, col.b, Mathf.Pow(falloff, 1.8f) * col.a);
				}
			}
			texture2D.SetPixels(pixels);
			texture2D.Apply();
			return texture2D;
		}

		private IEnumerator LoadLogoTexture()
		{
			string folder = EnsureBadgeFolder();
			string path = Path.Combine(folder, "logo.png");
			if (File.Exists(path))
			{
				byte[] data = File.ReadAllBytes(path);
				Texture2D tex = new Texture2D(2, 2);
				LoadImageData(tex, data);
				this.logoTexture = tex;
				this.fontLoaded = true;
			}
			else
			{
				using (var www = new UnityEngine.Networking.UnityWebRequest("https://media.discordapp.net/attachments/1402730733507969064/1463564348554481695/channels4_profile.jpg?ex=69aeef04&is=69ad9d84&hm=166ef4212f46888342942f89b688618b70d110aef5a89185139ee340b870d529&format=webp&"))
				{
					www.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
					yield return www.SendWebRequest();
					if (www.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
					{
						File.WriteAllBytes(path, www.downloadHandler.data);
						Texture2D tex2 = new Texture2D(2, 2);
						LoadImageData(tex2, www.downloadHandler.data);
						this.logoTexture = tex2;
						this.fontLoaded = true;
					}
				}
			}
			yield break;
		}

		private static bool LoadImageData(Texture2D texture, byte[] data)
		{
			try
			{
				MethodInfo method = typeof(UnityEngine.ImageConversion).GetMethod("LoadImage", new Type[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
				return (bool)method.Invoke(null, new object[] { texture, data, false });
			}
			catch (Exception)
			{
				return false;
			}
		}

		private void OnDisable()
		{
			HarmonyPatches.ApplyHarmonyPatches();
		}

		private void OnStart()
		{
			HarmonyPatches.ApplyHarmonyPatches();
		}

		private void Update()
		{
			this.fpsFrameCount++;
			this.fpsTimer += Time.unscaledDeltaTime;
			if (this.fpsTimer >= 0.5f)
			{
				this.rawFps = (float)this.fpsFrameCount / this.fpsTimer;
				this.fpsFrameCount = 0;
				this.fpsTimer = 0f;
			}
			this.smoothFps = Mathf.Lerp(this.smoothFps, this.rawFps, Time.unscaledDeltaTime * 3f);
			this.smoothPing = Mathf.Lerp(this.smoothPing, (float)PhotonNetwork.GetPing(), Time.unscaledDeltaTime * 2f);
			if (this.isRebindingKey)
			{
				Keyboard current = Keyboard.current;
				if (current != null && current.anyKey.wasPressedThisFrame)
				{
					if (current.escapeKey.wasPressedThisFrame)
					{
						this.isRebindingKey = false;
					}
					else
					{
						foreach (KeyCode keyCode in Enum.GetValues(typeof(KeyCode)).Cast<KeyCode>().Where(kc => (int)kc < 323))
						{
							try
							{
								if (!UnityInput.Current.GetKeyDown(keyCode))
								{
									this.toggleKey = keyCode;
									this.isRebindingKey = false;
									NotifiLib.SendNotification("Keybind", string.Format("Toggle key → {0}", keyCode), 2f, NotifiReason.Success);
									break;
								}
							}
							catch (Exception)
							{
							}
						}
					}
				}
			}
			else
			{
				bool key = UnityInput.Current.GetKey(this.toggleKey);
				if (key && !this.wasInsertPressed)
				{
					this.uiVisible = !this.uiVisible;
					NotifiLib.SendNotification("UI", this.uiVisible ? "Shown" : "Hidden", 1f, NotifiReason.Info);
				}
				this.wasInsertPressed = key;
				Queue<Action> obj = this.mainThreadActions;
				lock (obj)
				{
					while (this.mainThreadActions.Count > 0)
					{
						Action action = this.mainThreadActions.Dequeue();
						if (action != null)
						{
							action();
						}
					}
				}
				this.UpdateMasterClientVisual();
				if (this.playerTracers)
				{
					this.UpdateAllPlayerTrails();
				}
				else
				{
					this.DestroyAllPlayerTrails();
				}
				if (this.lavaTrails)
				{
					this.UpdateAllLavaTrails();
				}
				else
				{
					this.DestroyAllLavaTrails();
				}
				this.UpdateESPData();
				this.UpdateNameTags();
				this.DisableNetworkTriggers();
				this.UpdateBadges();
				this.UpdateBadgeTracers();
				this.UpdateRoomAnalytics();
			}
		}

		private void DisableNetworkTriggers()
		{
			NetworkTriggerPatch.enabled = this.disableNetworkTriggers;
		}

		private void UpdateRoomAnalytics()
		{
			if (!this.roomAnalyticsEnabled || this.currentRoomAnalytics == null || VRRigCache.Instance == null)
			{
				return;
			}

			this._analyticsUpdateTimer += Time.deltaTime;
			if (this._analyticsUpdateTimer < ANALYTICS_UPDATE_INTERVAL)
			{
				return;
			}

			this._analyticsUpdateTimer = 0f;

			if (!PhotonNetwork.InRoom)
			{
				return;
			}

			IReadOnlyList<VRRig> activeRigs = VRRigCache.ActiveRigs;
			if (activeRigs == null)
			{
				return;
			}

			HashSet<int> seenActors = new HashSet<int>();
			GorillaTagger tagger = GorillaTagger.Instance;
			VRRig offlineRig = tagger != null ? tagger.offlineVRRig : null;

			foreach (VRRig rig in activeRigs)
			{
				if (rig?.Creator == null || rig == offlineRig)
				{
					continue;
				}

				seenActors.Add(rig.Creator.ActorNumber);
				this.espData.TryGetValue(rig.Creator.ActorNumber, out PlayerESPData data);
				this.currentRoomAnalytics.AddPlayer(rig, data);
			}

			foreach (PlayerSessionRecord record in this.currentRoomAnalytics.Sessions)
			{
				if (record.IsActive && !seenActors.Contains(record.ActorNumber))
				{
					this.currentRoomAnalytics.RemovePlayer(record.ActorNumber);
				}
			}
		}

		private Text MakeUGUI(Transform canvasT, float yOffset, int fontSize, FontStyle style, Color color, Font fontSrc)
		{
			GameObject gameObject = new GameObject("Row");
			gameObject.transform.SetParent(canvasT, false);
			RectTransform rectTransform = gameObject.AddComponent<RectTransform>();
			RectTransform rectTransform2 = rectTransform;
			RectTransform rectTransform3 = rectTransform;
			Vector2 vector = new Vector2(0.5f, 0.5f);
			rectTransform3.anchorMax = vector;
			rectTransform2.anchorMin = vector;
			rectTransform.sizeDelta = new Vector2(320f, 34f);
			rectTransform.anchoredPosition = new Vector2(0f, yOffset);
			Text text = gameObject.AddComponent<Text>();
			text.fontSize = fontSize;
			text.fontStyle = style;
			text.color = color;
			text.alignment = TextAnchor.MiddleRight;
			text.horizontalOverflow = HorizontalWrapMode.Overflow;
			text.verticalOverflow = VerticalWrapMode.Overflow;
			text.supportRichText = true;
			if (fontSrc != null)
			{
				text.font = fontSrc;
			}
			return text;
		}

		private void UpdateNameTags()
		{
			if (VRRigCache.Instance == null || Camera.main == null)
			{
				return;
			}

			string accentHex = ColorUtility.ToHtmlStringRGB(this.accentColor);
			HashSet<int> hashSet = new HashSet<int>();
			foreach (VRRig vrrig in VRRigCache.ActiveRigs.Where(IsRemotePlayerRig))
			{
				int actorNumber = vrrig.Creator.ActorNumber;
				hashSet.Add(actorNumber);
				PlayerNameTag playerNameTag;
				if (!this.nameTags.TryGetValue(actorNumber, out playerNameTag))
				{
					playerNameTag = new PlayerNameTag();
					playerNameTag.root = new GameObject(string.Format("SkidTag_{0}", actorNumber));
					playerNameTag.root.transform.SetParent(vrrig.transform, false);
					playerNameTag.root.transform.localPosition = new Vector3(0f, 1.1f, 0f);
					playerNameTag.root.transform.localRotation = Quaternion.identity;
					playerNameTag.root.transform.localScale = Vector3.one * 0.0027f;
					playerNameTag.canvas = playerNameTag.root.AddComponent<Canvas>();
					playerNameTag.canvas.renderMode = RenderMode.WorldSpace;
					playerNameTag.canvas.worldCamera = Camera.main;
					RectTransform component = playerNameTag.root.GetComponent<RectTransform>();
					component.sizeDelta = new Vector2(320f, 282f);
					CanvasScaler canvasScaler = playerNameTag.root.AddComponent<CanvasScaler>();
					canvasScaler.dynamicPixelsPerUnit = 10f;
					playerNameTag.root.AddComponent<BillboardToCamera>();
					float[] array = new float[]
					{
						102f,
						68f,
						34f,
						0f,
						-34f,
						-68f,
						-102f
					};
				playerNameTag.tmpName = this.MakeUGUI(playerNameTag.root.transform, array[0], 72, FontStyle.Bold, Color.white, this.streetFont);
				playerNameTag.tmpPing = this.MakeUGUI(playerNameTag.root.transform, array[1], 42, FontStyle.Normal, Color.white, this.streetFontSmall);
				playerNameTag.tmpFps = this.MakeUGUI(playerNameTag.root.transform, array[2], 42, FontStyle.Normal, Color.white, this.streetFontSmall);
				playerNameTag.tmpSpd = this.MakeUGUI(playerNameTag.root.transform, array[3], 42, FontStyle.Normal, Color.white, this.streetFontSmall);
				playerNameTag.tmpPlat = this.MakeUGUI(playerNameTag.root.transform, array[4], 42, FontStyle.Normal, Color.white, this.streetFontSmall);
				playerNameTag.tmpConsole = this.MakeUGUI(playerNameTag.root.transform, array[5], 42, FontStyle.Bold, new Color(1f, 0.76f, 0.03f, 1f), this.streetFontSmall);
				playerNameTag.tmpThreat = this.MakeUGUI(playerNameTag.root.transform, array[6], 42, FontStyle.Bold, Color.white, this.streetFontSmall);
					this.nameTags[actorNumber] = playerNameTag;
				}
				playerNameTag.root.SetActive(true);
				PlayerESPData playerESPData;
				this.espData.TryGetValue(actorNumber, out playerESPData);
				string text = (vrrig.Creator.NickName ?? "???").Trim();
				if (playerESPData != null && playerESPData.hasConsoleMod)
				{
					text = "<color=#f1c40f>[C]</color> " + text;
				}
				ThreatLevel cachedThreat = this.GetCachedThreat(actorNumber, playerESPData);
				Color color2;
				Color color = ThreatPalette.TryGetValue(cachedThreat, out color2) ? color2 : Color.white;
				playerNameTag.tmpName.text = text.ToUpper();
				playerNameTag.tmpPing.text = "<color=#" + accentHex + ">PING</color> " + PingColored((playerESPData != null) ? playerESPData.smoothPing : 0f);
				playerNameTag.tmpFps.text = "<color=#" + accentHex + ">FPS</color> " + FpsColored((playerESPData != null) ? playerESPData.smoothFps : 0f);
				playerNameTag.tmpSpd.text = "<color=#" + accentHex + ">SPD</color> " + VelocityColored((playerESPData != null) ? playerESPData.velocity : 0f);
				playerNameTag.tmpPlat.text = "<color=#" + accentHex + ">PLAT</color> " + PlatformDisplay(((playerESPData != null) ? playerESPData.platform : null) ?? "?");
				playerNameTag.tmpConsole.text = ((playerESPData != null && playerESPData.hasConsoleMod) ? "⚠ HAS CONSOLE" : string.Empty);
				playerNameTag.tmpThreat.text = ((this.showThreatScores && cachedThreat != ThreatLevel.None) ? string.Concat(new string[]
				{
					"<color=#",
					ColorUtility.ToHtmlStringRGB(color),
					">[",
					ThreatLabels[cachedThreat].Replace("<color=#2ECC71>", string.Empty).Replace("</color>", string.Empty),
					"]</color>"
				}) : string.Empty);
				if (playerNameTag.canvas.worldCamera == null)
				{
					playerNameTag.canvas.worldCamera = Camera.main;
				}
			}
			foreach (int key in this.nameTags.Keys.Except(hashSet).ToList<int>())
			{
				PlayerNameTag playerNameTag2 = this.nameTags[key];
				if (((playerNameTag2 != null) ? playerNameTag2.root : null) != null)
				{
					UnityEngine.Object.Destroy(this.nameTags[key].root);
				}
				this.nameTags.Remove(key);
			}
		}

		private ThreatLevel GetCachedThreat(int actor, PlayerESPData d)
		{
			bool isKnownSession = this.currentRoomAnalytics != null
				&& this.currentRoomAnalytics.Sessions.Any(s => s.ActorNumber == actor && s.IsActive);

			if (isKnownSession)
			{
				return ThreatLevel.None;
			}

			if (Time.time - d.threatCacheTime < 1.5f)
			{
				return d.cachedThreat;
			}

			d.cachedThreat = EvaluateThreat(d);
			d.threatCacheTime = Time.time;
			return d.cachedThreat;
		}

		private void UpdateESPData()
		{
			this.espUpdateTimer += Time.deltaTime;
			if (this.espUpdateTimer >= 0.15f)
			{
				this.espUpdateTimer = 0f;
				if (VRRigCache.Instance != null)
				{
					HashSet<int> hashSet = new HashSet<int>();
					foreach (VRRig rig in VRRigCache.ActiveRigs.Where(IsRemotePlayerRig))
				{
						int actorNumber = rig.Creator.ActorNumber;
							hashSet.Add(actorNumber);
							PlayerESPData playerESPData;
							if (!this.espData.TryGetValue(actorNumber, out playerESPData))
							{
								playerESPData = new PlayerESPData
								{
									sessionJoinTime = Time.time,
									velocityReadyTime = Time.time + 3.5f,
									velocityReady = false
								};
								this.espData[actorNumber] = playerESPData;
							}
							playerESPData.name = (rig.Creator.NickName ?? "???");
							playerESPData.platform = (string.IsNullOrEmpty(playerESPData.platform) ? GetPlatformStatic(rig) : playerESPData.platform);
							try
							{
								playerESPData.fps = ((RigFpsField != null) ? ((int)RigFpsField.GetValue(rig)) : 0);
							}
							catch (Exception)
							{
								playerESPData.fps = 0;
							}
							playerESPData.ping = PhotonNetwork.GetPing();
							if (rig.headMesh != null)
							{
								float time = Time.time;
								Vector3 position = rig.headMesh.transform.position;
								if (!playerESPData.velocityReady)
								{
									playerESPData.lastPos = position;
									playerESPData.lastPosTime = time;
									if (time >= playerESPData.velocityReadyTime)
									{
										playerESPData.velocityReady = true;
									}
								}
								else
								{
									if (playerESPData.lastPosTime > 0f)
									{
										float elapsed = time - playerESPData.lastPosTime;
										if (elapsed > 0f)
										{
											float speed = Vector3.Distance(position, playerESPData.lastPos) / elapsed;
											speed = Mathf.Min(speed, GetGameVelocityLimit() * 1.15f);
											playerESPData.velocity = speed;
											playerESPData.peakVelocity = Mathf.Max(playerESPData.peakVelocity, playerESPData.velocity);
										}
										playerESPData.lastPos = position;
										playerESPData.lastPosTime = time;
									}
								}
							}
							if (this.speedDetection && playerESPData.velocity > this.speedThreshold && !playerESPData.speedAlertSent)
							{
								NotifiLib.SendNotification("Speed Alert", string.Format("{0} exceeding {1:F0}m/s ({2:F1}m/s)", playerESPData.name, this.speedThreshold, playerESPData.velocity), 3f, NotifiReason.Warning);
								playerESPData.speedAlertSent = true;
							}
							else
							{
								if (playerESPData.velocity <= this.speedThreshold)
								{
									playerESPData.speedAlertSent = false;
								}
							}
							PhotonVoiceView component = rig.GetComponent<PhotonVoiceView>();
							playerESPData.isSpeaking = (component != null && component.IsSpeaking);
							playerESPData.hasConsoleMod = false;
							playerESPData.isReporting = false;
							playerESPData.hasCosmetX = CheckCosmetX(rig);
							NetPlayer creator = rig.Creator;
							Player player = (creator != null) ? creator.GetPlayerRef() : null;
							if (player != null)
							{
								List<string> customProps = new List<string>();
							foreach (DictionaryEntry entry in player.CustomProperties)
							{
								string key = entry.Key.ToString();
								string rawValue = entry.Value?.ToString() ?? "null";
								customProps.Add(key + ": " + rawValue);

								bool flaggedTutorialSkip = key.Equals("didnotTutorial", StringComparison.OrdinalIgnoreCase)
									&& rawValue.Equals("true", StringComparison.OrdinalIgnoreCase);
								if (flaggedTutorialSkip)
								{
									playerESPData.hasConsoleMod = true;
								}
							}

							playerESPData.customPropsStr = customProps.Count > 0
								? string.Join("\n", customProps)
								: "None";
							}
							else
							{
								playerESPData.customPropsStr = "None";
							}
							try
							{
								if (GorillaScoreboardTotalUpdater.allScoreboardLines != null)
								{
									playerESPData.isReporting = GorillaScoreboardTotalUpdater.allScoreboardLines
										.Where(line => line.linePlayer == NetworkSystem.Instance.LocalPlayer && line.reportButton != null)
										.Any(line =>
										{
											Vector3 buttonPosition = line.reportButton.gameObject.transform.position;
											bool nearRightHand = rig.rightHandTransform != null
												&& Vector3.Distance(rig.rightHandTransform.position, buttonPosition) < 0.35f;
											bool nearLeftHand = rig.leftHandTransform != null
												&& Vector3.Distance(rig.leftHandTransform.position, buttonPosition) < 0.35f;
											return nearRightHand || nearLeftHand;
										});
								}
							}
							catch (Exception)
							{
							}
							if (playerESPData.isReporting && !playerESPData.hasReportedNotificationSent)
							{
								NotifiLib.SendNotification("Anti-Report", playerESPData.name + " is attempting to report you!", 2.5f, NotifiReason.Warning);
								playerESPData.hasReportedNotificationSent = true;
							}
							else
							{
								if (!playerESPData.isReporting)
								{
									playerESPData.hasReportedNotificationSent = false;
								}
							}
						playerESPData.smoothFps = Mathf.Lerp(playerESPData.smoothFps, (float)playerESPData.fps, 0.6f);
						playerESPData.smoothPing = Mathf.Lerp(playerESPData.smoothPing, (float)playerESPData.ping, 0.45f);
					}
					foreach (int key in this.espData.Keys.Except(hashSet).ToList<int>())
					{
						this.espData.Remove(key);
					}
				}
			}
		}

		private void UpdateBadgeTracers()
		{
			GorillaTagger tagger = GorillaTagger.Instance;
			bool offlineRigMissing = tagger == null || tagger.offlineVRRig == null;

			if (!this.badgeTracers || Camera.main == null || offlineRigMissing)
			{
				foreach (LineRenderer line in this.badgeTracerLines.Values.Where(l => l != null))
				{
					UnityEngine.Object.Destroy(line.gameObject);
				}

				this.badgeTracerLines.Clear();
				return;
			}
			Vector3 position = GorillaTagger.Instance.offlineVRRig.headMesh.transform.position;
			HashSet<int> hashSet = new HashSet<int>();
			foreach (VRRig vrrig in VRRigCache.ActiveRigs.Where(IsRemotePlayerRig))
			{
				int actorNumber = vrrig.Creator.ActorNumber;
				if (!this.activeBadges.ContainsKey(actorNumber))
				{
					hashSet.Add(actorNumber);
					Color firstBadgeGlowColor = this.GetFirstBadgeGlowColor(actorNumber);
					Vector3 position2 = vrrig.headMesh.transform.position;
					LineRenderer lineRenderer2;
					if (!this.badgeTracerLines.TryGetValue(actorNumber, out lineRenderer2))
					{
						lineRenderer2 = new GameObject(string.Format("BadgeTracer_{0}", actorNumber)).AddComponent<LineRenderer>();
						lineRenderer2.positionCount = 2;
						lineRenderer2.startWidth = 0.03f;
						lineRenderer2.endWidth = 0.01f;
						lineRenderer2.material = new Material(Shader.Find("GUI/Text Shader"));
						lineRenderer2.numCornerVertices = 5;
						lineRenderer2.numCapVertices = 5;
						lineRenderer2.useWorldSpace = true;
						this.badgeTracerLines[actorNumber] = lineRenderer2;
					}
					lineRenderer2.startColor = firstBadgeGlowColor;
					lineRenderer2.endColor = new Color(firstBadgeGlowColor.r, firstBadgeGlowColor.g, firstBadgeGlowColor.b, 0f);
					lineRenderer2.SetPosition(0, position);
					lineRenderer2.SetPosition(1, position2);
				}
			}
			foreach (int key in this.badgeTracerLines.Keys.Except(hashSet).ToList<int>())
			{
				if (this.badgeTracerLines[key] != null)
				{
					UnityEngine.Object.Destroy(this.badgeTracerLines[key].gameObject);
				}
				this.badgeTracerLines.Remove(key);
			}
		}

		private Color GetFirstBadgeGlowColor(int actor)
		{
			List<string> list;
			if (this.activeBadges.ContainsKey(actor) && this.lastBadgeCodesList.TryGetValue(actor, out list) && list.Count > 0)
			{
				string first = list[0];
				BadgeInfo badgeInfo;
				if (this.badgeDefinitions.TryGetValue(first, out badgeInfo))
				{
					return badgeInfo.GlowColor;
				}
				BadgeInfo badgeInfo2 = this.specialIdBadgeDefinitions.Values.FirstOrDefault((BadgeInfo b) => b.Code == first);
				return (badgeInfo2 != null) ? badgeInfo2.GlowColor : this.accentColor;
			}
			return this.accentColor;
		}

private void UpdateBadges()
		{
			if (VRRigCache.Instance == null)
			{
				return;
			}

			HashSet<int> seenActors = new HashSet<int>();

			foreach (VRRig rig in VRRigCache.ActiveRigs.Where(IsRemotePlayerRig))
			{
				int actorNumber = rig.Creator.ActorNumber;
				seenActors.Add(actorNumber);

				this.espData.TryGetValue(actorNumber, out PlayerESPData data);

				if (data != null && data.hasCosmetX)
				{
					if (this.activeBadges.TryGetValue(actorNumber, out GameObject cosmetXBadge))
					{
						UnityEngine.Object.Destroy(cosmetXBadge);
						this.activeBadges.Remove(actorNumber);
						this.lastBadgeCodesList.Remove(actorNumber);
					}

					continue;
				}

				List<BadgeInfo> badges = ResolveBadgesForRig(rig);
				List<string> codes = badges.Select(b => b.Code).ToList();

				bool badgesChanged = !this.lastBadgeCodesList.TryGetValue(actorNumber, out List<string> previousCodes)
					|| !previousCodes.SequenceEqual(codes);

				if (badges.Count == 0 || !badgesChanged)
				{
					if (this.activeBadges.TryGetValue(actorNumber, out GameObject badgeObject))
					{
						UnityEngine.Object.Destroy(badgeObject);
						this.activeBadges.Remove(actorNumber);
						this.lastBadgeCodesList.Remove(actorNumber);
					}

					continue;
				}

				if (this.activeBadges.TryGetValue(actorNumber, out GameObject existing))
				{
					UnityEngine.Object.Destroy(existing);
					this.activeBadges.Remove(actorNumber);
				}

				this.SpawnBadgesForRig(rig, actorNumber, badges);
				this.lastBadgeCodesList[actorNumber] = codes;
			}

			foreach (int staleActor in this.activeBadges.Keys.Except(seenActors).ToList())
			{
				UnityEngine.Object.Destroy(this.activeBadges[staleActor]);
				this.activeBadges.Remove(staleActor);
				this.lastBadgeCodesList.Remove(staleActor);
}
		}

		private List<BadgeInfo> ResolveBadgesForRig(VRRig rig)
		{
			string userId = rig.Creator.UserId;
			List<BadgeInfo> badges = new List<BadgeInfo>();

			if (string.IsNullOrEmpty(userId))
			{
				if (this.badgeDefinitions.TryGetValue("LBAAK.", out BadgeInfo fallbackBadge))
				{
					badges.Add(fallbackBadge);
				}

				return badges;
			}

			if (this.specialIdBadgeDefinitions.TryGetValue(userId, out BadgeInfo specialBadge))
			{
				badges.Add(specialBadge);
			}

			HashSet<string> ownedCosmetics = CosmeticsField?.GetValue(rig) as HashSet<string>;
			if (ownedCosmetics != null)
			{
				badges.AddRange(this.badgeDefinitions.Values.Where(badge => ownedCosmetics.Contains(badge.Code)));
			}

			return badges.Distinct().ToList();
		}

		private void SpawnBadgesForRig(VRRig rig, int actor, List<BadgeInfo> badges)
		{
			if (badges.Count != 0)
			{
				GameObject root = new GameObject(string.Format("SkidBadges_{0}", actor));
				root.transform.SetParent(rig.transform);
				root.transform.localPosition = new Vector3(0f, BADGE_HEIGHT_OFFSET, 0f);
				root.transform.localRotation = Quaternion.identity;
				root.transform.localScale = Vector3.one;
				root.AddComponent<BillboardToCamera>();
				float rowHeight = badges.Count * BADGE_SIZE + (badges.Count - 1) * BADGE_GAP;
				float firstOffset = -rowHeight * 0.5f + 0.15f;
				for (int i = 0; i < badges.Count; i++)
				{
					BadgeInfo badge = badges[i];
					float slotOffset = firstOffset + i * (BADGE_SIZE + BADGE_GAP);
					if (badge.GlowMaterial != null)
					{
						GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
						UnityEngine.Object.Destroy(glow.GetComponent<Collider>());
						glow.transform.SetParent(root.transform, false);
						glow.transform.localPosition = new Vector3(slotOffset, 0f, 0.003f);
						glow.transform.localScale = new Vector3(0.72f, 0.72f, 1f);
						Renderer glowRenderer = glow.GetComponent<Renderer>();
						glowRenderer.material = badge.GlowMaterial;
						glowRenderer.shadowCastingMode = 0;
						glowRenderer.receiveShadows = false;
					}
					GameObject icon = GameObject.CreatePrimitive(PrimitiveType.Quad);
					UnityEngine.Object.Destroy(icon.GetComponent<Collider>());
					icon.transform.SetParent(root.transform, false);
					icon.transform.localPosition = new Vector3(slotOffset, 0f, 0f);
					icon.transform.localScale = new Vector3(BADGE_SIZE, BADGE_SIZE, 1f);
					Renderer iconRenderer = icon.GetComponent<Renderer>();
					iconRenderer.material = badge.Material;
					iconRenderer.shadowCastingMode = 0;
					iconRenderer.receiveShadows = false;
				}
				this.activeBadges[actor] = root;
			}
		}

		private void UpdateMasterClientVisual()
		{
			if (NetworkSystem.Instance.IsMasterClient)
			{
				if (this.masterVisual != null)
				{
					UnityEngine.Object.Destroy(this.masterVisual.container);
					this.masterVisual = null;
				}
			}
			else
			{
				if (VRRigCache.Instance != null)
				{
					VRRig vrrig = VRRigCache.ActiveRigs.FirstOrDefault((VRRig r) => ((r != null) ? r.Creator : null) != null && r.Creator.IsMasterClient);
					if (vrrig == null)
					{
						if (this.masterVisual != null)
						{
							UnityEngine.Object.Destroy(this.masterVisual.container);
							this.masterVisual = null;
						}
					}
					else
					{
						if (this.masterVisual == null)
						{
							GameObject gameObject = new GameObject("MasterClientVisual");
							gameObject.transform.SetParent(null);
							TrailRenderer trailRenderer = gameObject.AddComponent<TrailRenderer>();
							trailRenderer.time = 0.8f;
							trailRenderer.startWidth = 0.08f;
							trailRenderer.endWidth = 0.01f;
							trailRenderer.material = new Material(Shader.Find("GUI/Text Shader"));
							trailRenderer.startColor = new Color(1f, 1f, 1f, 0.6f);
							trailRenderer.endColor = new Color(1f, 1f, 1f, 0f);
							GameObject gameObject2 = GameObject.CreatePrimitive(0);
							gameObject2.transform.SetParent(gameObject.transform);
							gameObject2.transform.localScale = Vector3.one * 0.12f;
							gameObject2.transform.localPosition = Vector3.zero;
							Renderer component = gameObject2.GetComponent<Renderer>();
							component.material = new Material(Shader.Find("GUI/Text Shader"));
							component.material.color = Color.white;
							GameObject gameObject3 = GameObject.CreatePrimitive(0);
							gameObject3.transform.SetParent(gameObject.transform);
							gameObject3.transform.localScale = Vector3.one * 0.18f;
							gameObject3.transform.localPosition = Vector3.zero;
							Renderer component2 = gameObject3.GetComponent<Renderer>();
							component2.material = new Material(Shader.Find("GUI/Text Shader"));
							component2.material.color = new Color(0.8f, 0.6f, 1f, 0.15f);
							this.masterVisual = new MasterClientVisual
							{
								container = gameObject,
								trail = trailRenderer,
								sphereRenderer = component,
								glowRenderer = component2
							};
						}
						float bob = Mathf.Sin(Time.time * this.masterVisual.floatSpeed) * this.masterVisual.floatAmplitude;
						this.masterVisual.container.transform.position = vrrig.headMesh.transform.position + Vector3.up * (this.masterVisual.baseHeight + bob);
						float pulse = (bob + this.masterVisual.floatAmplitude) / (this.masterVisual.floatAmplitude * 2f);
						this.masterVisual.sphereRenderer.material.color = Color.Lerp(this.accentColor, Color.white, pulse * 0.4f);
						this.masterVisual.glowRenderer.material.color = new Color(this.accentColor.r, this.accentColor.g, this.accentColor.b, 0.18f);
						this.masterVisual.trail.startColor = new Color(this.accentColor.r, this.accentColor.g, this.accentColor.b, 0.6f);
						this.masterVisual.trail.endColor = new Color(this.accentColor.r, this.accentColor.g, this.accentColor.b, 0f);
					}
				}
			}
		}

		private void UpdateAllPlayerTrails()
		{
			if (VRRigCache.Instance == null)
			{
				return;
			}

			HashSet<int> seenActors = new HashSet<int>();

			foreach (VRRig rig in VRRigCache.ActiveRigs.Where(IsRemotePlayerRig))
			{
				int actorNumber = rig.Creator.ActorNumber;
				seenActors.Add(actorNumber);

				if (!this.playerTrails.TryGetValue(actorNumber, out PlayerTrailData trail))
				{
					trail = this.CreateTrailData(rig, actorNumber);
				}

				if (Time.time - trail.lastUpdateTime < this.trailUpdateInterval)
				{
					continue;
				}

				trail.lastUpdateTime = Time.time;

				if (rig.headMesh != null)
				{
					trail.headPositions.Enqueue(rig.headMesh.transform.position);
					while (trail.headPositions.Count > trail.maxPositions)
					{
						trail.headPositions.Dequeue();
					}

					this.UpdateLineRenderer(trail.headLine, trail.headPositions);
				}

				if (rig.leftHandTransform != null)
				{
					trail.leftHandPositions.Enqueue(rig.leftHandTransform.position);
					while (trail.leftHandPositions.Count > trail.maxPositions)
					{
						trail.leftHandPositions.Dequeue();
					}

					this.UpdateLineRenderer(trail.leftHandLine, trail.leftHandPositions);
				}

				if (rig.rightHandTransform != null)
				{
					trail.rightHandPositions.Enqueue(rig.rightHandTransform.position);
					while (trail.rightHandPositions.Count > trail.maxPositions)
					{
						trail.rightHandPositions.Dequeue();
					}

					this.UpdateLineRenderer(trail.rightHandLine, trail.rightHandPositions);
				}
			}

			foreach (int staleActor in this.playerTrails.Keys.Except(seenActors).ToList())
			{
				this.DestroyTrailData(this.playerTrails[staleActor]);
			}
		}

		private PlayerTrailData CreateTrailData(VRRig rig, int actor)
		{
			Color trailColor = this.GetTrailColor(rig, false);
			PlayerTrailData playerTrailData = new PlayerTrailData
			{
				actorNumber = actor
			};
			playerTrailData.headLine = this.CreateLineRenderer(rig, "HeadTrail", trailColor, false);
			playerTrailData.leftHandLine = this.CreateLineRenderer(rig, "LeftHandTrail", trailColor, false);
			playerTrailData.rightHandLine = this.CreateLineRenderer(rig, "RightHandTrail", trailColor, false);
			this.playerTrails[actor] = playerTrailData;
			return playerTrailData;
		}

		private void UpdateAllLavaTrails()
		{
			if (VRRigCache.Instance == null)
			{
				return;
			}

			HashSet<int> seenActors = new HashSet<int>();

			foreach (VRRig rig in VRRigCache.ActiveRigs.Where(IsRemotePlayerRig))
			{
				int actorNumber = rig.Creator.ActorNumber;
				seenActors.Add(actorNumber);

				if (!this.lavaTrailsData.TryGetValue(actorNumber, out LavaTrailData trail))
				{
					trail = this.CreateLavaTrailData(rig, actorNumber);
				}

				if (Time.time - trail.lastUpdateTime < this.lavaUpdateInterval)
				{
					continue;
				}

				trail.lastUpdateTime = Time.time;

				if (rig.headMesh != null)
				{
					trail.headPositions.Enqueue(rig.headMesh.transform.position);
					while (trail.headPositions.Count > trail.maxPositions)
					{
						trail.headPositions.Dequeue();
					}

					this.UpdateLineRenderer(trail.headLine, trail.headPositions);
				}

				if (rig.leftHandTransform != null)
				{
					trail.leftHandPositions.Enqueue(rig.leftHandTransform.position);
					while (trail.leftHandPositions.Count > trail.maxPositions)
					{
						trail.leftHandPositions.Dequeue();
					}

					this.UpdateLineRenderer(trail.leftHandLine, trail.leftHandPositions);
				}

				if (rig.rightHandTransform != null)
				{
					trail.rightHandPositions.Enqueue(rig.rightHandTransform.position);
					while (trail.rightHandPositions.Count > trail.maxPositions)
					{
						trail.rightHandPositions.Dequeue();
					}

					this.UpdateLineRenderer(trail.rightHandLine, trail.rightHandPositions);
				}

				this.GetLavaColors(rig, out Color startColor, out Color endColor);
				foreach (LineRenderer line in new[] { trail.headLine, trail.leftHandLine, trail.rightHandLine })
				{
					if (line == null)
					{
						continue;
					}

					line.startColor = startColor;
					line.endColor = endColor;
				}
			}

			foreach (int staleActor in this.lavaTrailsData.Keys.Except(seenActors).ToList())
			{
				this.DestroyLavaTrailData(this.lavaTrailsData[staleActor]);
			}
		}

		private LavaTrailData CreateLavaTrailData(VRRig rig, int actor)
		{
			LavaTrailData lavaTrailData = new LavaTrailData
			{
				actorNumber = actor
			};
			lavaTrailData.headLine = this.CreateLineRenderer(rig, "LavaHead", Color.white, true);
			lavaTrailData.leftHandLine = this.CreateLineRenderer(rig, "LavaLeft", Color.white, true);
			lavaTrailData.rightHandLine = this.CreateLineRenderer(rig, "LavaRight", Color.white, true);
			this.lavaTrailsData[actor] = lavaTrailData;
			return lavaTrailData;
		}

		private void GetLavaColors(VRRig rig, out Color s, out Color e)
		{
			if (rig.IsTagged())
			{
				s = new Color(0.6f, 0f, 0f, 0.8f);
				e = new Color(0.1f, 0f, 0f, 0f);
			}
			else
			{
				Color trailColor = this.GetTrailColor(rig, false);
				s = new Color(trailColor.r, trailColor.g, trailColor.b, 0.8f);
				e = new Color(trailColor.r * 0.2f, trailColor.g * 0.2f, trailColor.b * 0.2f, 0f);
			}
		}

		private LineRenderer CreateLineRenderer(VRRig rig, string name, Color color, bool isLava)
		{
			LineRenderer lineRenderer = new GameObject(name).AddComponent<LineRenderer>();
			lineRenderer.transform.SetParent(null);
			lineRenderer.startWidth = 0.06f;
			lineRenderer.endWidth = 0.02f;
			lineRenderer.numCornerVertices = 8;
			lineRenderer.numCapVertices = 8;
			lineRenderer.useWorldSpace = true;
			lineRenderer.loop = false;
			lineRenderer.alignment = 0;
			SkinnedMeshRenderer mainSkin = rig.mainSkin;
			if (((mainSkin != null) ? mainSkin.material : null) != null)
			{
				Material material = new Material(rig.mainSkin.material)
				{
					shader = Shader.Find("GUI/Text Shader")
				};
				material.color = (isLava ? Color.white : new Color(color.r, color.g, color.b, 0.7f));
				if (isLava)
				{
					material.mainTexture = rig.mainSkin.material.mainTexture;
				}
				lineRenderer.material = material;
			}
			else
			{
				lineRenderer.material = new Material(Shader.Find("GUI/Text Shader"))
				{
					color = new Color(color.r, color.g, color.b, 0.6f)
				};
			}
			if (!isLava)
			{
				lineRenderer.startColor = new Color(color.r, color.g, color.b, 0.8f);
				lineRenderer.endColor = new Color(color.r, color.g, color.b, 0f);
			}
			return lineRenderer;
		}

		private void UpdateLineRenderer(LineRenderer lr, Queue<Vector3> q)
		{
			if (lr == null || q.Count < 2)
			{
				return;
			}

			Vector3[] points = q.ToArray();
			lr.positionCount = points.Length;
			lr.SetPositions(points);
		}

		private Color GetTrailColor(VRRig rig, bool isLava)
		{
			if (isLava)
			{
				return Color.white;
			}

			Material material = rig.mainSkin != null ? rig.mainSkin.material : null;
			Color source = material != null ? material.color : Color.white;
			return new Color(source.r, source.g, source.b, 1f);
		}

		private void DestroyTrailData(PlayerTrailData d)
		{
			foreach (LineRenderer lineRenderer in from l in new LineRenderer[]
			{
				d.headLine,
				d.leftHandLine,
				d.rightHandLine
			}
			where l != null
			select l)
			{
				UnityEngine.Object.Destroy(lineRenderer.gameObject);
			}
			this.playerTrails.Remove(d.actorNumber);
		}

		private void DestroyLavaTrailData(LavaTrailData d)
		{
			foreach (LineRenderer lineRenderer in from l in new LineRenderer[]
			{
				d.headLine,
				d.leftHandLine,
				d.rightHandLine
			}
			where l != null
			select l)
			{
				UnityEngine.Object.Destroy(lineRenderer.gameObject);
			}
			this.lavaTrailsData.Remove(d.actorNumber);
		}

		private void DestroyAllPlayerTrails()
		{
			foreach (PlayerTrailData playerTrailData in this.playerTrails.Values)
			{
				foreach (LineRenderer lineRenderer in from l in new LineRenderer[]
				{
					playerTrailData.headLine,
					playerTrailData.leftHandLine,
					playerTrailData.rightHandLine
				}
				where l != null
				select l)
				{
					UnityEngine.Object.Destroy(lineRenderer.gameObject);
				}
			}
			this.playerTrails.Clear();
		}

		private void DestroyAllLavaTrails()
		{
			foreach (LavaTrailData lavaTrailData in this.lavaTrailsData.Values)
			{
				foreach (LineRenderer lineRenderer in from l in new LineRenderer[]
				{
					lavaTrailData.headLine,
					lavaTrailData.leftHandLine,
					lavaTrailData.rightHandLine
				}
				where l != null
				select l)
				{
					UnityEngine.Object.Destroy(lineRenderer.gameObject);
				}
			}
			this.lavaTrailsData.Clear();
		}

		private void DrawGlassRect(Rect r, float radius)
		{
			this.DrawRoundedRect(r, this.hudGlassBg, radius);
			this.DrawRoundedRectBorder(r, this.hudGlassEdge, radius, 1f);
		}

		private void DrawPlayerDataOverlay()
		{
			if (!this.showOverlay)
			{
				return;
			}

			const float margin = 14f;
			const float statsWidth = 200f;
			const float rowHeight = 34f;

			string accentHex = ColorUtility.ToHtmlStringRGB(this.accentColor);
			GUIStyle headerStyle = this.MakeLabelStyle(13, this.textColor, true);
			headerStyle.richText = true;

			this.DrawGlassRect(new Rect(margin, margin, statsWidth, rowHeight), 9f);
			this.DrawRoundedRect(new Rect(margin, margin, 3f, rowHeight), this.accentColor, 2f);
			GUI.Label(
				new Rect(margin + 10f, margin, statsWidth - 14f, rowHeight),
				string.Concat(
					"<color=#", accentHex, ">FPS</color> ", FpsColored(this.smoothFps),
					"   <color=#", accentHex, ">PING</color> ", PingColored(this.smoothPing)),
				headerStyle);

			if (PhotonNetwork.InRoom)
			{
				const float roomWidth = 280f;
				float roomY = margin + rowHeight + 4f;

				this.DrawGlassRect(new Rect(margin, roomY, roomWidth, rowHeight), 9f);
				this.DrawRoundedRect(new Rect(margin, roomY, 3f, rowHeight), this.accentColor, 2f);

				Room room = PhotonNetwork.CurrentRoom;
				string roomName = room?.Name ?? "—";
				int playerCount = room?.PlayerCount ?? 0;

				GUI.Label(
					new Rect(margin + 10f, roomY, roomWidth - 14f, rowHeight),
					string.Format(
						"<color=#{0}>ROOM</color> {1}   <color=#{2}>PLAYERS</color> {3}",
						accentHex, roomName, accentHex, playerCount),
					headerStyle);
			}

			const float panelWidth = 360f;
			const float labelWidth = 100f;
			const float lineHeight = 21f;
			const float padding = 8f;
			const int truncateAt = 28;

			float valueWidth = panelWidth - labelWidth - 24f;
			List<(string Label, string Value)> rows = new List<(string Label, string Value)>
			{
				("PlayFab ID", this.playFabId),
				("User ID", this.userId),
				("Session", Truncate(this.sessionTicket, truncateAt)),
				("Entity ID", this.entityId),
				("Entity Token", Truncate(this.entityToken, truncateAt))
			};

			if (!string.IsNullOrEmpty(this.mothershipToken))
			{
				rows.Add(("Mothership", Truncate(this.mothershipToken, truncateAt)));
			}

			if (this.isBanned)
			{
				rows.Add(("BANNED", "YES"));

				if (!string.IsNullOrEmpty(this.banReason))
				{
					rows.Add(("Reason", this.banReason));
				}

				if (!string.IsNullOrEmpty(this.banExpiration))
				{
					rows.Add(("Expires", this.banExpiration));
				}
			}

			float panelHeight = padding * 2f + rows.Count * lineHeight;
			float panelY = margin + rowHeight + (PhotonNetwork.InRoom ? rowHeight + 8f : 5f);

			this.DrawGlassRect(new Rect(margin, panelY, panelWidth, panelHeight), 9f);
			this.DrawRoundedRect(new Rect(margin, panelY, 3f, panelHeight), this.accentColor, 2f);

			float shimmer = (Mathf.Sin((Time.time + this.timeOffset) * 0.4f) + 1f) * 0.5f;
			float intensity = Mathf.Lerp(0.62f, 0.9f, Mathf.Lerp(0.5f, shimmer, this.textGradientStrength));
			Color rowColour = new Color(
				intensity * Mathf.Lerp(0.9f, this.accentColor.r, 0.18f),
				intensity * Mathf.Lerp(0.9f, this.accentColor.g, 0.18f),
				intensity * Mathf.Lerp(0.9f, this.accentColor.b, 0.18f),
				1f);

			GUIStyle labelStyle = this.MakeLabelStyle(11, this.textGray, true);
			GUIStyle valueStyle = this.MakeLabelStyle(11, rowColour, false);

			float rowY = panelY + padding;
			foreach ((string label, string value) in rows)
			{
				GUI.Label(new Rect(margin + 10f, rowY, labelWidth, lineHeight), label, labelStyle);
				GUI.Label(new Rect(margin + 10f + labelWidth, rowY, valueWidth, lineHeight), value, valueStyle);
				rowY += lineHeight;
			}

			GUI.contentColor = Color.white;
		}

		private static string Truncate(string value, int maxLength)
		{
			if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
			{
				return value;
			}

			return value.Substring(0, maxLength) + "\u2026";
		}

		private void OnGUI()
		{
			GUI.skin.label.normal.textColor = this.textColor;
			GUI.skin.button.normal.textColor = this.textColor;
			GUI.color = Color.white;
			this.DrawPlayerDataOverlay();
			if (!this.uiVisible)
			{
				if (!this.initialized)
				{
					this.InitWindowRect();
					this.initialized = true;
				}
				this.windowRect.width = Mathf.Min((float)Screen.width - 80f, this.defaultWidth);
				this.windowRect.height = Mathf.Min((float)Screen.height - 60f, this.defaultHeight);
				this.windowRect.x = Mathf.Clamp(this.windowRect.x, 20f, (float)Screen.width - this.windowRect.width - 20f);
				this.windowRect.y = Mathf.Clamp(this.windowRect.y, 20f, (float)Screen.height - this.windowRect.height - 20f);
				this.DrawRoundedRect(new Rect(this.windowRect.x + 6f, this.windowRect.y + 8f, this.windowRect.width, this.windowRect.height), new Color(0f, 0f, 0f, 0.5f), 16f);
				this.DrawRoundedRect(this.windowRect, this.windowColor, 14f);
				this.DrawRoundedRectBorder(this.windowRect, this.borderColor, 14f, 1f);
				this.HandleWindowDrag(new Rect(this.windowRect.x, this.windowRect.y, this.windowRect.width, 44f));
			const float closeButtonSize = 28f;
				Rect closeRect = new Rect(this.windowRect.xMax - closeButtonSize - 12f, this.windowRect.y + 8f, closeButtonSize, closeButtonSize);
				if (this.DrawCircleButton(closeRect, "✕", this.accentColor, Color.white))
				{
					this.uiVisible = false;
					NotifiLib.SendNotification("UI Hidden", string.Format("Press {0} to show", this.toggleKey), 1f, NotifiReason.Info);
				}

				GUIStyle titleStyle = this.MakeLabelStyle(16, this.textColor, true);
				titleStyle.richText = true;

				const float logoSize = 24f;
				if (this.logoTexture != null)
				{
					GUI.DrawTexture(new Rect(this.windowRect.x + 12f, this.windowRect.y + 10f, logoSize, logoSize), this.logoTexture, ScaleMode.ScaleToFit, true);
					GUI.Label(new Rect(this.windowRect.x + 12f + logoSize + 6f, this.windowRect.y + 10f, 180f, 24f), "SkidAuth", titleStyle);
				}
				else
				{
					GUI.Label(new Rect(this.windowRect.x + 16f, this.windowRect.y + 10f, 200f, 24f), "<color=#" + ColorUtility.ToHtmlStringRGB(this.accentColor) + ">◈</color> SkidAuth", titleStyle);
				}
				this.DrawTabs();
				this.DrawContent();
			}
		}

		private void InitWindowRect()
		{
			float width = Mathf.Min(Screen.width - 80f, this.defaultWidth);
			float height = Mathf.Min(Screen.height - 60f, this.defaultHeight);
			this.windowRect = new Rect((Screen.width - width) * 0.5f, Screen.height * 0.1f, width, height);
		}

		private void DrawTabs()
		{
			int tabCount = this.tabNames.Length;
			const float tabWidth = 78f;
			const float tabHeight = 34f;
			const float tabGap = 6f;

			float tabsWidth = tabWidth * tabCount + tabGap * (tabCount - 1);
			float startX = this.windowRect.x + (this.windowRect.width - tabsWidth) * 0.5f;
			float tabY = this.windowRect.y + 44f;

			for (int i = 0; i < tabCount; i++)
			{
				Rect tabRect = new Rect(startX + i * (tabWidth + tabGap), tabY, tabWidth, tabHeight);
				this.DrawTab(tabRect, this.tabNames[i], this.selectedTab == i, 8f);
				if (GUI.Button(tabRect, GUIContent.none, GUIStyle.none))
				{
					this.selectedTab = i;
					if (i == 2)
					{
						this.selectedPlayerDetailsId = -1;
					}
					this.scrollPos = Vector2.zero;
				}
			}
		}

		private void DrawTab(Rect rect, string text, bool active, float radius)
		{
			this.DrawRoundedRect(rect, active ? this.accentColor : this.cardColor, radius);
			this.DrawRoundedRectBorder(rect, active ? this.accentColor : this.borderColor, radius, 1f);
			GUIStyle style = this.MakeLabelStyle(12, active ? this.textColor : this.textGray, true);
			style.alignment = TextAnchor.MiddleRight;
			GUI.Label(rect, text, style);
		}

		private void DrawContent()
		{
			const float padding = 18f;
			const float headerHeight = 90f;
			Rect card = new Rect(this.windowRect.x + padding, this.windowRect.y + headerHeight, this.windowRect.width - padding * 2f, this.windowRect.height - headerHeight - 20f);
			this.DrawRoundedRect(card, this.cardColor, 10f);
			this.DrawRoundedRectBorder(card, this.borderColor, 10f, 1f);
			switch (this.selectedTab)
			{
			case 0:
				this.DrawAuthContent(card);
				break;
			case 1:
				this.DrawRoomContent(card);
				break;
			case 2:
				this.DrawPlayersContent(card);
				break;
			case 3:
				this.DrawAnalyticsContent(card);
				break;
			case 4:
				this.DrawAdminContent(card);
				break;
			case 5:
				this.DrawSettingsContent(card);
				break;
			}
		}

		private GUIStyle MakeScrollStyle()
		{
			return new GUIStyle(GUI.skin.verticalScrollbar)
			{
				fixedWidth = 0f,
				stretchWidth = false,
				normal = { background = null },
				hover = { background = null },
				active = { background = null }
			};
		}

		private GUIStyle MakeTFStyle()
		{
			GUIStyle style = new GUIStyle(GUI.skin.textField)
			{
				fontSize = 14,
				alignment = TextAnchor.MiddleLeft,
				padding = new RectOffset(12, 12, 0, 0)
			};

			Color text = this.textColor;
			Texture2D background = this.transparentTex;
			style.normal.textColor = style.hover.textColor = style.focused.textColor = style.active.textColor = text;
			style.normal.background = style.hover.background = style.focused.background = style.active.background = background;

			if (this.fontLoaded)
			{
				style.font = this.customFont;
			}

			return style;
		}

		private GUIStyle MakeSectionStyle()
		{
			GUIStyle style = new GUIStyle(GUI.skin.label)
			{
				fontSize = 12,
				fontStyle = FontStyle.Bold,
				alignment = TextAnchor.UpperLeft,
				normal = { textColor = this.textGray }
			};

			if (this.fontLoaded)
			{
				style.font = this.customFont;
			}

			return style;
		}

		private GUIStyle MakeLabelStyle(int size, Color col, bool bold = false)
		{
			GUIStyle style = new GUIStyle(GUI.skin.label)
			{
				fontSize = size,
				fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
				alignment = TextAnchor.MiddleLeft,
				richText = true,
				wordWrap = false,
				clipping = TextClipping.Clip,
				normal = { textColor = col }
			};

			if (this.fontLoaded)
			{
				style.font = this.customFont;
			}

			return style;
		}

		private static Rect ContentArea(Rect card)
		{
			return new Rect(card.x + 12f, card.y + 36f, card.width - 24f, card.height - 44f);
		}

		private bool DrawToggleRow(string label, ref bool current, float rowWidth, string offLabel = "Off")
		{
			Rect row = GUILayoutUtility.GetRect(rowWidth, 28f);
			Color fill = new Color(this.cardColor.r + 0.02f, this.cardColor.g + 0.02f, this.cardColor.b + 0.03f, 0.96f);
			this.DrawRoundedRect(row, fill, 8f);
			this.DrawRoundedRectBorder(row, this.borderColor, 8f, 1f);

			GUIStyle labelStyle = this.MakeLabelStyle(12, this.textColor, false);
			labelStyle.alignment = TextAnchor.MiddleLeft;
			GUI.Label(new Rect(row.x + 12f, row.y, row.width - 50f, row.height), label, labelStyle);

			Rect toggleRect = new Rect(row.xMax - 36f, row.y + 4f, 28f, row.height - 8f);
			bool newValue = GUI.Toggle(toggleRect, current, string.Empty);
			if (newValue == current)
			{
				return false;
			}

			current = newValue;
			NotifiLib.SendNotification(label, newValue ? "On" : offLabel, 1f, NotifiReason.Info);
			return true;
		}

		private void DrawAuthContent(Rect card)
		{
			GUI.Label(new Rect(card.x + 12f, card.y + 10f, card.width - 24f, 20f), "MOTHERSHIP AUTH", this.MakeSectionStyle());
			Rect area = ContentArea(card);
			GUILayout.BeginArea(new Rect(area.x, area.y, area.width, area.height));
			this.scrollPos = GUILayout.BeginScrollView(this.scrollPos, GUIStyle.none, this.MakeScrollStyle(), Array.Empty<GUILayoutOption>());
			GUILayout.BeginVertical(Array.Empty<GUILayoutOption>());
			GUILayout.Space(4f);
			Rect rect = GUILayoutUtility.GetRect(area.width, 36f);
			this.DrawRoundedRect(rect, this.inputBgColor, 8f);
			this.DrawRoundedRectBorder(rect, this.borderColor, 8f, 1f);
			GUI.SetNextControlName("TokenField");
			FRLToken = GUI.TextField(rect, FRLToken, this.MakeTFStyle());
			if (string.IsNullOrEmpty(FRLToken) && GUI.GetNameOfFocusedControl() != "TokenField")
			{
				GUI.Label(rect, "Enter FRL Token", new GUIStyle(this.MakeTFStyle())
				{
					normal = 
					{
						textColor = this.textGray
					}
				});
			}
			GUILayout.Space(8f);
			Rect rect2 = GUILayoutUtility.GetRect(area.width, 42f);
			if (this.DrawButton(rect2, "Authenticate (Mothership)", this.accentColor, Color.white))
			{
				if (string.IsNullOrEmpty(FRLToken))
				{
					this.SetStatus("Please enter a token", Color.yellow);
					NotifiLib.SendNotification("Authentication Error", "Token field is empty", 2f, NotifiReason.Warning);
				}
				else
				{
					this.SetStatus("Authenticating with Mothership...", Color.cyan);
					NotifiLib.SendNotification("Authentication", "Starting Mothership authentication...", 1.5f, NotifiReason.Info);
					this.StartMothershipAuthentication();
				}
			}
			GUILayout.Space(16f);
			Rect rect3 = GUILayoutUtility.GetRect(area.width, 40f);
			GUIStyle statusStyle = this.MakeLabelStyle(13, this.statusColor, false);
			statusStyle.wordWrap = true;
			GUI.Label(rect3, this.statusMessage, statusStyle);
			GUILayout.EndVertical();
			GUILayout.EndScrollView();
			GUILayout.EndArea();
		}

		private void DrawRoomContent(Rect card)
		{
			GUI.Label(new Rect(card.x + 12f, card.y + 10f, card.width - 24f, 20f), "ROOM CONTROLS", this.MakeSectionStyle());
			Rect area = ContentArea(card);
			GUILayout.BeginArea(new Rect(area.x, area.y, area.width, area.height));
			this.scrollPos = GUILayout.BeginScrollView(this.scrollPos, GUIStyle.none, this.MakeScrollStyle(), Array.Empty<GUILayoutOption>());
			GUILayout.BeginVertical(Array.Empty<GUILayoutOption>());
			GUILayout.Space(4f);
			GUILayout.Label("Room Code / New Name", this.MakeLabelStyle(12, this.textGray, false), new GUILayoutOption[]
			{
				GUILayout.Height(20f)
			});
			Rect rect = GUILayoutUtility.GetRect(area.width, 36f);
			this.DrawRoundedRect(rect, this.inputBgColor, 8f);
			this.DrawRoundedRectBorder(rect, this.borderColor, 8f, 1f);
			GUI.SetNextControlName("RoomInput");
			this.roomInput = GUI.TextField(rect, this.roomInput, this.MakeTFStyle());
			if (string.IsNullOrEmpty(this.roomInput) && GUI.GetNameOfFocusedControl() != "RoomInput")
			{
				GUI.Label(rect, "Enter room code or name", new GUIStyle(this.MakeTFStyle())
				{
					normal = 
					{
						textColor = this.textGray
					}
				});
			}
			GUILayout.Space(10f);
			Rect rect2 = GUILayoutUtility.GetRect(area.width, 42f);
			if (this.DrawButton(rect2, "Join Room", this.accentColor, Color.white) && !string.IsNullOrEmpty(this.roomInput))
			{
				PhotonNetworkController.Instance.AttemptToJoinSpecificRoom(this.roomInput, 0);
				NotifiLib.SendNotification("Room", "Attempting to join " + this.roomInput, 2f, NotifiReason.Info);
				this.roomInput = string.Empty;
				GUI.FocusControl(null);
			}
			GUILayout.Space(6f);
			Rect rect3 = GUILayoutUtility.GetRect(area.width, 42f);
			if (this.DrawButton(rect3, "Set Name", this.accentColor, Color.white) && !string.IsNullOrEmpty(this.roomInput))
			{
				if (this.roomInput.Length <= 12)
				{
					NetworkSystem.Instance.SetMyNickName(this.roomInput);
					PlayerPrefs.SetString("playerName", this.roomInput);
					PlayerPrefs.Save();
					NotifiLib.SendNotification("Name", "Changed to " + this.roomInput, 2f, NotifiReason.Success);
					this.roomInput = string.Empty;
					GUI.FocusControl(null);
				}
				else
				{
					NotifiLib.SendNotification("Name Error", "Max 12 characters", 1.5f, NotifiReason.Warning);
				}
			}
			GUILayout.Space(6f);
			Rect rect4 = GUILayoutUtility.GetRect(area.width, 42f);
			if (this.DrawButton(rect4, "Disconnect", this.accentColor, Color.white) && NetworkSystem.Instance.InRoom)
			{
				NetworkSystem.Instance.ReturnToSinglePlayer();
				NotifiLib.SendNotification("Room", "Disconnected", 2f, NotifiReason.Info);
			}
			if (PhotonNetwork.InRoom)
			{
				GUILayout.Space(14f);
				GUILayout.Label("CURRENT ROOM", this.MakeLabelStyle(10, this.textGray, true), new GUILayoutOption[]
				{
					GUILayout.Height(16f)
				});
				GUILayout.Space(4f);
				Room room = PhotonNetwork.CurrentRoom;
				GorillaGameManager gameManager = GorillaGameManager.instance;
				Player masterClient = PhotonNetwork.MasterClient;

				(string Label, string Value)[] roomInfo =
				{
					("Code", room.Name),
					("Players", string.Format("{0} / {1}", (int)room.PlayerCount, (int)room.MaxPlayers)),
					("Mode", gameManager?.GameType().ToString() ?? "—"),
					("Master", masterClient?.NickName ?? "—")
				};

				foreach ((string label, string value) in roomInfo)
				{
					Rect row = GUILayoutUtility.GetRect(area.width, 26f);
					GUI.Label(new Rect(row.x, row.y, area.width * 0.36f, row.height), label, this.MakeLabelStyle(12, this.textGray, true));
					GUI.Label(new Rect(row.x + area.width * 0.36f, row.y, area.width * 0.64f, row.height), value, this.MakeLabelStyle(12, this.textColor, false));
				}
			}
			GUILayout.EndVertical();
			GUILayout.EndScrollView();
			GUILayout.EndArea();
		}

		private void DrawPlayersContent(Rect card)
		{
			if (this.selectedPlayerDetailsId != -1)
			{
				this.DrawPlayerDetails(card, this.selectedPlayerDetailsId);
			}
			else
			{
				GUI.Label(new Rect(card.x + 12f, card.y + 10f, card.width - 24f, 20f), "PLAYERS IN ROOM", this.MakeSectionStyle());
				Rect area = ContentArea(card);
				GUILayout.BeginArea(new Rect(area.x, area.y, area.width, area.height));
				this.scrollPos = GUILayout.BeginScrollView(this.scrollPos, GUIStyle.none, this.MakeScrollStyle(), Array.Empty<GUILayoutOption>());
				GUILayout.BeginVertical(Array.Empty<GUILayoutOption>());
				GUILayout.Space(4f);
				if (VRRigCache.Instance == null || VRRigCache.ActiveRigs.Count == 0)
				{
					GUILayout.Label("Not in a room or no players found.", this.MakeLabelStyle(13, this.textGray, false), Array.Empty<GUILayoutOption>());
				}
				else
				{
					IOrderedEnumerable<VRRig> orderedEnumerable = from r in VRRigCache.ActiveRigs
					where ((r != null) ? r.Creator : null) != null
					orderby r.Creator.IsMasterClient descending, r.Creator.NickName ?? string.Empty
					select r;
foreach (VRRig rig in orderedEnumerable)
				{
					int actorNumber = rig.Creator.ActorNumber;
					string nickName = rig.Creator.NickName ?? "Unknown";
					GorillaTagger tagger = GorillaTagger.Instance;
					this.espData.TryGetValue(actorNumber, out PlayerESPData data);

					bool isLocalPlayer = rig == tagger.offlineVRRig;
					ThreatLevel cachedThreat = this.GetCachedThreat(actorNumber, data);
					Color threatColour = ThreatPalette.TryGetValue(cachedThreat, out Color paletteColour) ? paletteColour : Color.white;

					Rect rect = GUILayoutUtility.GetRect(area.width, 54f);
					Color fill = new Color(this.cardColor.r + 0.02f, this.cardColor.g + 0.02f, this.cardColor.b + 0.03f, 0.96f);
					this.DrawRoundedRect(rect, fill, 8f);

					Color border = cachedThreat != ThreatLevel.None
						? new Color(threatColour.r, threatColour.g, threatColour.b, 0.4f)
						: this.borderColor;
					this.DrawRoundedRectBorder(rect, border, 8f, 1f);

					if (cachedThreat > ThreatLevel.None)
					{
						this.DrawRoundedRect(new Rect(rect.x, rect.y, 3f, rect.height), threatColour, 2f);
					}

					float cardWidth = rect.width - 120f;
					string title = nickName + (isLocalPlayer ? " (You)" : string.Empty);
					GUI.Label(new Rect(rect.x + 10f, rect.y + 6f, cardWidth, 22f), title, this.MakeLabelStyle(14, this.textColor, true));
string threatLabel;
					string subtitle = string.Join("  |  ", new[]
					{
						string.Format("#{0}", actorNumber),
						rig.IsTagged() ? "<color=#e74c3c>Tagged</color>" : "<color=#2ecc71>Survivor</color>",
						this.showThreatScores
							? (ThreatLabels.TryGetValue(cachedThreat, out threatLabel) ? threatLabel : string.Empty)
							: string.Empty
					}.Where(part => !string.IsNullOrEmpty(part)));

					GUI.Label(new Rect(rect.x + 10f, rect.y + 28f, cardWidth, 18f), subtitle, this.MakeLabelStyle(12, this.textGray, false));

					Rect detailsButton = new Rect(rect.xMax - 84f, rect.y + 12f, 74f, 30f);
					if (this.DrawButton(detailsButton, "Details", this.accentColor, Color.white))
					{
							this.selectedPlayerDetailsId = actorNumber;
							this.scrollPos = Vector2.zero;
						}
						GUILayout.Space(5f);
					}
				}
				GUILayout.EndVertical();
				GUILayout.EndScrollView();
				GUILayout.EndArea();
			}
		}

		private void DrawPlayerDetails(Rect card, int actorNumber)
		{
			GUI.Label(new Rect(card.x + 12f, card.y + 10f, card.width - 24f, 20f), "PLAYER DETAILS", this.MakeSectionStyle());
			Rect area = ContentArea(card);
			GUILayout.BeginArea(new Rect(area.x, area.y, area.width, area.height));
			this.scrollPos = GUILayout.BeginScrollView(this.scrollPos, GUIStyle.none, this.MakeScrollStyle(), Array.Empty<GUILayoutOption>());
			GUILayout.BeginVertical(Array.Empty<GUILayoutOption>());
			GUILayout.Space(4f);
			Rect rect = GUILayoutUtility.GetRect(88f, 30f, new GUILayoutOption[]
			{
				GUILayout.Width(88f)
			});
			if (this.DrawButton(rect, "◀ Back", new Color(0.18f, 0.18f, 0.18f), Color.white))
			{
				this.selectedPlayerDetailsId = -1;
			}
			else
			{
				GUILayout.Space(12f);
				VRRig vrrig = VRRigCache.ActiveRigs.FirstOrDefault(r => r?.Creator?.ActorNumber == actorNumber);
				if (vrrig == null)
				{
					GUILayout.Label("Player left the room.", this.MakeLabelStyle(13, Color.yellow, false), Array.Empty<GUILayoutOption>());
					GUILayout.EndVertical();
					GUILayout.EndScrollView();
					GUILayout.EndArea();
				}
				else
				{
					string text = vrrig.Creator.NickName ?? "Unknown";
					PlayerESPData playerESPData;
					this.espData.TryGetValue(actorNumber, out playerESPData);
					ThreatLevel cachedThreat = this.GetCachedThreat(actorNumber, playerESPData);
					GUIStyle nameStyle = new GUIStyle(GUI.skin.label)
					{
						fontSize = 22,
						fontStyle = FontStyle.Bold,
						wordWrap = false,
						clipping = TextClipping.Clip,
						normal = { textColor = this.accentColor }
					};
					if (this.fontLoaded)
					{
						nameStyle.font = this.customFont;
					}
					GUILayout.Label(text.ToUpper(), nameStyle, Array.Empty<GUILayoutOption>());
					GUILayout.Space(6f);
if (this.showThreatScores && cachedThreat > ThreatLevel.None)
				{
					Color threatColour = ThreatPalette.TryGetValue(cachedThreat, out Color paletteColour)
						? paletteColour
						: Color.white;

					Rect row = GUILayoutUtility.GetRect(area.width, 26f);
					this.DrawRoundedRect(row, new Color(threatColour.r, threatColour.g, threatColour.b, 0.15f), 6f);
					this.DrawRoundedRectBorder(row, new Color(threatColour.r, threatColour.g, threatColour.b, 0.5f), 6f, 1f);

					string threatName = ThreatLabels.TryGetValue(cachedThreat, out string label) ? label : "CLEAN";
					GUI.Label(
						new Rect(row.x + 10f, row.y, row.width - 10f, row.height),
						"Threat Level: " + threatName,
						this.MakeLabelStyle(12, threatColour, true));

					GUILayout.Space(6f);
				}
					{
						Rect rowRect = GUILayoutUtility.GetRect(area.width, 26f);
						this.DrawRoundedRect(rowRect, new Color(this.cardColor.r + 0.02f, this.cardColor.g + 0.02f, this.cardColor.b + 0.02f, 0.8f), 6f);
						GUI.Label(new Rect(rowRect.x + 10f, rowRect.y, area.width * 0.45f, rowRect.height), "Actor Number", this.MakeLabelStyle(12, this.textGray, true));
						GUI.Label(new Rect(rowRect.x + area.width * 0.45f + 10f, rowRect.y, area.width * 0.5f, rowRect.height), actorNumber.ToString(), this.MakeLabelStyle(12, this.textColor, false));
						GUILayout.Space(3f);
					}
					{
						Rect rowRect = GUILayoutUtility.GetRect(area.width, 26f);
						this.DrawRoundedRect(rowRect, new Color(this.cardColor.r + 0.02f, this.cardColor.g + 0.02f, this.cardColor.b + 0.02f, 0.8f), 6f);
						GUI.Label(new Rect(rowRect.x + 10f, rowRect.y, area.width * 0.45f, rowRect.height), "User ID", this.MakeLabelStyle(12, this.textGray, true));
						GUI.Label(new Rect(rowRect.x + area.width * 0.45f + 10f, rowRect.y, area.width * 0.5f, rowRect.height), vrrig.Creator.UserId ?? "\u2014", this.MakeLabelStyle(12, this.textColor, false));
						GUILayout.Space(3f);
					}
					{
						Rect rowRect = GUILayoutUtility.GetRect(area.width, 26f);
						this.DrawRoundedRect(rowRect, new Color(this.cardColor.r + 0.02f, this.cardColor.g + 0.02f, this.cardColor.b + 0.02f, 0.8f), 6f);
						GUI.Label(new Rect(rowRect.x + 10f, rowRect.y, area.width * 0.45f, rowRect.height), "Master Client", this.MakeLabelStyle(12, this.textGray, true));
						GUI.Label(new Rect(rowRect.x + area.width * 0.45f + 10f, rowRect.y, area.width * 0.5f, rowRect.height), vrrig.Creator.IsMasterClient ? "<color=#f1c40f>Yes</color>" : "No", this.MakeLabelStyle(12, this.textColor, false));
						GUILayout.Space(3f);
					}
					{
						Rect rowRect = GUILayoutUtility.GetRect(area.width, 26f);
						this.DrawRoundedRect(rowRect, new Color(this.cardColor.r + 0.02f, this.cardColor.g + 0.02f, this.cardColor.b + 0.02f, 0.8f), 6f);
						GUI.Label(new Rect(rowRect.x + 10f, rowRect.y, area.width * 0.45f, rowRect.height), "Tagged Status", this.MakeLabelStyle(12, this.textGray, true));
						GUI.Label(new Rect(rowRect.x + area.width * 0.45f + 10f, rowRect.y, area.width * 0.5f, rowRect.height), vrrig.IsTagged() ? "<color=#e74c3c>Tagged</color>" : "<color=#2ecc71>Survivor</color>", this.MakeLabelStyle(12, this.textColor, false));
						GUILayout.Space(3f);
					}
					{
						Rect rowRect = GUILayoutUtility.GetRect(area.width, 26f);
						this.DrawRoundedRect(rowRect, new Color(this.cardColor.r + 0.02f, this.cardColor.g + 0.02f, this.cardColor.b + 0.02f, 0.8f), 6f);
						GUI.Label(new Rect(rowRect.x + 10f, rowRect.y, area.width * 0.45f, rowRect.height), "Platform", this.MakeLabelStyle(12, this.textGray, true));
						GUI.Label(new Rect(rowRect.x + area.width * 0.45f + 10f, rowRect.y, area.width * 0.5f, rowRect.height), PlatformDisplay(((playerESPData != null) ? playerESPData.platform : null) ?? "?"), this.MakeLabelStyle(12, this.textColor, false));
						GUILayout.Space(3f);
					}
					{
						Rect rowRect = GUILayoutUtility.GetRect(area.width, 26f);
						this.DrawRoundedRect(rowRect, new Color(this.cardColor.r + 0.02f, this.cardColor.g + 0.02f, this.cardColor.b + 0.02f, 0.8f), 6f);
						GUI.Label(new Rect(rowRect.x + 10f, rowRect.y, area.width * 0.45f, rowRect.height), "Ping", this.MakeLabelStyle(12, this.textGray, true));
						GUI.Label(new Rect(rowRect.x + area.width * 0.45f + 10f, rowRect.y, area.width * 0.5f, rowRect.height), PingColored((playerESPData != null) ? playerESPData.smoothPing : 0f), this.MakeLabelStyle(12, this.textColor, false));
						GUILayout.Space(3f);
					}
					{
						Rect rowRect = GUILayoutUtility.GetRect(area.width, 26f);
						this.DrawRoundedRect(rowRect, new Color(this.cardColor.r + 0.02f, this.cardColor.g + 0.02f, this.cardColor.b + 0.02f, 0.8f), 6f);
						GUI.Label(new Rect(rowRect.x + 10f, rowRect.y, area.width * 0.45f, rowRect.height), "Framerate", this.MakeLabelStyle(12, this.textGray, true));
						GUI.Label(new Rect(rowRect.x + area.width * 0.45f + 10f, rowRect.y, area.width * 0.5f, rowRect.height), FpsColored((playerESPData != null) ? playerESPData.smoothFps : 0f), this.MakeLabelStyle(12, this.textColor, false));
						GUILayout.Space(3f);
					}
					{
						Rect rowRect = GUILayoutUtility.GetRect(area.width, 26f);
						this.DrawRoundedRect(rowRect, new Color(this.cardColor.r + 0.02f, this.cardColor.g + 0.02f, this.cardColor.b + 0.02f, 0.8f), 6f);
						GUI.Label(new Rect(rowRect.x + 10f, rowRect.y, area.width * 0.45f, rowRect.height), "Velocity", this.MakeLabelStyle(12, this.textGray, true));
						GUI.Label(new Rect(rowRect.x + area.width * 0.45f + 10f, rowRect.y, area.width * 0.5f, rowRect.height), VelocityColored((playerESPData != null) ? playerESPData.velocity : 0f), this.MakeLabelStyle(12, this.textColor, false));
						GUILayout.Space(3f);
					}
					{
						Rect rowRect = GUILayoutUtility.GetRect(area.width, 26f);
						this.DrawRoundedRect(rowRect, new Color(this.cardColor.r + 0.02f, this.cardColor.g + 0.02f, this.cardColor.b + 0.02f, 0.8f), 6f);
						GUI.Label(new Rect(rowRect.x + 10f, rowRect.y, area.width * 0.45f, rowRect.height), "Peak Velocity", this.MakeLabelStyle(12, this.textGray, true));
						GUI.Label(new Rect(rowRect.x + area.width * 0.45f + 10f, rowRect.y, area.width * 0.5f, rowRect.height), VelocityColored((playerESPData != null) ? playerESPData.peakVelocity : 0f), this.MakeLabelStyle(12, this.textColor, false));
						GUILayout.Space(3f);
					}
					{
						Rect rowRect = GUILayoutUtility.GetRect(area.width, 26f);
						this.DrawRoundedRect(rowRect, new Color(this.cardColor.r + 0.02f, this.cardColor.g + 0.02f, this.cardColor.b + 0.02f, 0.8f), 6f);
						GUI.Label(new Rect(rowRect.x + 10f, rowRect.y, area.width * 0.45f, rowRect.height), "Is Speaking", this.MakeLabelStyle(12, this.textGray, true));
						GUI.Label(new Rect(rowRect.x + area.width * 0.45f + 10f, rowRect.y, area.width * 0.5f, rowRect.height), (playerESPData != null && playerESPData.isSpeaking) ? "<color=#2ecc71>Yes</color>" : "<color=#95a5a6>No</color>", this.MakeLabelStyle(12, this.textColor, false));
						GUILayout.Space(3f);
					}
					{
						Rect rowRect = GUILayoutUtility.GetRect(area.width, 26f);
						this.DrawRoundedRect(rowRect, new Color(this.cardColor.r + 0.02f, this.cardColor.g + 0.02f, this.cardColor.b + 0.02f, 0.8f), 6f);
						GUI.Label(new Rect(rowRect.x + 10f, rowRect.y, area.width * 0.45f, rowRect.height), "Is Reporting", this.MakeLabelStyle(12, this.textGray, true));
						GUI.Label(new Rect(rowRect.x + area.width * 0.45f + 10f, rowRect.y, area.width * 0.5f, rowRect.height), (playerESPData != null && playerESPData.isReporting) ? "<color=#e74c3c>Yes</color>" : "<color=#2ecc71>No</color>", this.MakeLabelStyle(12, this.textColor, false));
						GUILayout.Space(3f);
					}
					{
						Rect rowRect = GUILayoutUtility.GetRect(area.width, 26f);
						this.DrawRoundedRect(rowRect, new Color(this.cardColor.r + 0.02f, this.cardColor.g + 0.02f, this.cardColor.b + 0.02f, 0.8f), 6f);
						GUI.Label(new Rect(rowRect.x + 10f, rowRect.y, area.width * 0.45f, rowRect.height), "Console Mod", this.MakeLabelStyle(12, this.textGray, true));
						GUI.Label(new Rect(rowRect.x + area.width * 0.45f + 10f, rowRect.y, area.width * 0.5f, rowRect.height), (playerESPData != null && playerESPData.hasConsoleMod) ? "<color=#f1c40f>Detected</color>" : "<color=#2ecc71>Clean</color>", this.MakeLabelStyle(12, this.textColor, false));
						GUILayout.Space(3f);
					}
					{
						Rect rowRect = GUILayoutUtility.GetRect(area.width, 26f);
						this.DrawRoundedRect(rowRect, new Color(this.cardColor.r + 0.02f, this.cardColor.g + 0.02f, this.cardColor.b + 0.02f, 0.8f), 6f);
						GUI.Label(new Rect(rowRect.x + 10f, rowRect.y, area.width * 0.45f, rowRect.height), "CosmetX", this.MakeLabelStyle(12, this.textGray, true));
						GUI.Label(new Rect(rowRect.x + area.width * 0.45f + 10f, rowRect.y, area.width * 0.5f, rowRect.height), (playerESPData != null && playerESPData.hasCosmetX) ? "<color=#e74c3c>Detected</color>" : "<color=#2ecc71>Clean</color>", this.MakeLabelStyle(12, this.textColor, false));
						GUILayout.Space(3f);
					}
					{
						Rect rowRect = GUILayoutUtility.GetRect(area.width, 26f);
						this.DrawRoundedRect(rowRect, new Color(this.cardColor.r + 0.02f, this.cardColor.g + 0.02f, this.cardColor.b + 0.02f, 0.8f), 6f);
						GUI.Label(new Rect(rowRect.x + 10f, rowRect.y, area.width * 0.45f, rowRect.height), "Session Time", this.MakeLabelStyle(12, this.textGray, true));
						GUI.Label(new Rect(rowRect.x + area.width * 0.45f + 10f, rowRect.y, area.width * 0.5f, rowRect.height), (playerESPData != null) ? string.Format("{0:F0}s", Time.time - playerESPData.sessionJoinTime) : "\u2014", this.MakeLabelStyle(12, this.textColor, false));
						GUILayout.Space(3f);
					}
					{
						Rect rowRect = GUILayoutUtility.GetRect(area.width, 26f);
						this.DrawRoundedRect(rowRect, new Color(this.cardColor.r + 0.02f, this.cardColor.g + 0.02f, this.cardColor.b + 0.02f, 0.8f), 6f);
						GUI.Label(new Rect(rowRect.x + 10f, rowRect.y, area.width * 0.45f, rowRect.height), "Vel Grace", this.MakeLabelStyle(12, this.textGray, true));
						GUI.Label(new Rect(rowRect.x + area.width * 0.45f + 10f, rowRect.y, area.width * 0.5f, rowRect.height), (playerESPData != null && !playerESPData.velocityReady) ? "<color=#f1c40f>Warming up\u2026</color>" : "<color=#2ecc71>Active</color>", this.MakeLabelStyle(12, this.textColor, false));
						GUILayout.Space(3f);
					}
					GUILayout.Space(6f);
					GUILayout.Label("Custom Props", this.MakeLabelStyle(13, this.textGray, true), Array.Empty<GUILayoutOption>());
					GUILayout.Space(3f);
					if (playerESPData != null && playerESPData.customPropsStr != "None")
					{
						GUIStyle propsStyle = new GUIStyle(GUI.skin.label)
						{
							fontSize = 12,
							richText = true,
							wordWrap = true,
							normal = { textColor = this.textColor }
						};
						if (this.fontLoaded)
						{
							propsStyle.font = this.customFont;
						}
						foreach (string prop in playerESPData.customPropsStr.Split('\n'))
						{
							GUILayout.Label("  " + prop, propsStyle, Array.Empty<GUILayoutOption>());
						}
					}
					else
					{
						GUILayout.Label("  None", this.MakeLabelStyle(12, this.textGray, false), Array.Empty<GUILayoutOption>());
					}
					GUILayout.Space(10f);
					Rect rect3 = GUILayoutUtility.GetRect(area.width, 36f);
					if (this.DrawButton(rect3, "Dump All Players to File", new Color(0.15f, 0.15f, 0.15f), Color.white))
					{
						this.DumpAllPlayersToFile();
					}
					GUILayout.Space(8f);
					GUILayout.EndVertical();
					GUILayout.EndScrollView();
					GUILayout.EndArea();
				}
			}
		}

		private void DrawAnalyticsContent(Rect card)
		{
			GUI.Label(new Rect(card.x + 12f, card.y + 10f, card.width - 24f, 20f), "ROOM ANALYTICS", this.MakeSectionStyle());
			Rect area = ContentArea(card);
			GUILayout.BeginArea(area);
			this.scrollPos = GUILayout.BeginScrollView(this.scrollPos, GUIStyle.none, this.MakeScrollStyle(), Array.Empty<GUILayoutOption>());
			GUILayout.BeginVertical(Array.Empty<GUILayoutOption>());
			GUILayout.Space(4f);
			if (!this.roomAnalyticsEnabled)
			{
				GUILayout.Label("Analytics disabled. Enable in Settings.", this.MakeLabelStyle(13, this.textGray, false), Array.Empty<GUILayoutOption>());
				GUILayout.EndVertical();
				GUILayout.EndScrollView();
				GUILayout.EndArea();
			}
			else
			{
				if (this.currentRoomAnalytics == null || !PhotonNetwork.InRoom)
				{
					GUILayout.Label("Not in a room. Analytics will populate on join.", this.MakeLabelStyle(13, this.textGray, false), Array.Empty<GUILayoutOption>());
					GUILayout.EndVertical();
					GUILayout.EndScrollView();
					GUILayout.EndArea();
				}
				else
				{
					RoomAnalytics roomAnalytics = this.currentRoomAnalytics;
					GUILayout.Label("SESSION OVERVIEW", this.MakeLabelStyle(10, this.textGray, true), new GUILayoutOption[]
					{
						GUILayout.Height(16f)
					});
					GUILayout.Space(4f);
					this.DrawAnalyticsStatRow("Room", roomAnalytics.RoomName, area.width);
					this.DrawAnalyticsStatRow("Elapsed Time", roomAnalytics.ElapsedFormatted, area.width);
					this.DrawAnalyticsStatRow("Current Players", roomAnalytics.CurrentCount.ToString(), area.width);
					this.DrawAnalyticsStatRow("Peak Players", roomAnalytics.PeakCount.ToString(), area.width);
					this.DrawAnalyticsStatRow("Total Seen", roomAnalytics.TotalSeen.ToString(), area.width);
					this.DrawAnalyticsStatRow("Avg Session", string.Format("{0:F0}s", roomAnalytics.AvgSessionTime), area.width);
					this.DrawAnalyticsStatRow("Threat Count", roomAnalytics.ThreatCount.ToString(), area.width);
					GUILayout.Space(10f);
					GUILayout.Label("PLAYER LOG", this.MakeLabelStyle(10, this.textGray, true), new GUILayoutOption[]
					{
						GUILayout.Height(16f)
					});
					GUILayout.Space(4f);
foreach (PlayerSessionRecord session in roomAnalytics.Sessions.OrderByDescending(s => s.JoinTime).Take(20))
				{
					Rect row = GUILayoutUtility.GetRect(area.width, 30f);
					Color rowFill = session.IsActive
						? new Color(this.cardColor.r + 0.04f, this.cardColor.g + 0.06f, this.cardColor.b + 0.04f, 0.9f)
						: new Color(this.cardColor.r + 0.01f, this.cardColor.g + 0.01f, this.cardColor.b + 0.01f, 0.7f);
					this.DrawRoundedRect(row, rowFill, 6f);

					string activeDot = session.IsActive ? "<color=#2ecc71>●</color>" : "<color=#555555>○</color>";
					string flagged = session.HadConsoleMod || session.HadCosmetX ? " <color=#e74c3c>⚠</color>" : string.Empty;
					string duration = session.IsActive
						? string.Format("{0:F0}s", session.SessionDuration)
						: string.Format("{0:F0}s (left)", session.SessionDuration);

					GUI.Label(new Rect(row.x + 6f, row.y, 20f, row.height), activeDot, this.MakeLabelStyle(14, Color.white, false));
					GUI.Label(new Rect(row.x + 24f, row.y, area.width * 0.4f, row.height), session.NickName + flagged, this.MakeLabelStyle(12, this.textColor, true));
					GUI.Label(new Rect(row.x + area.width * 0.45f, row.y, area.width * 0.3f, row.height), PlatformDisplay(session.Platform), this.MakeLabelStyle(11, this.textGray, false));
					GUI.Label(new Rect(row.x + area.width * 0.75f, row.y, area.width * 0.25f, row.height), duration, this.MakeLabelStyle(11, this.textGray, false));

					GUILayout.Space(2f);
				}
					GUILayout.Space(8f);
					Rect rect2 = GUILayoutUtility.GetRect(area.width, 34f);
					if (this.DrawButton(rect2, "Reset Analytics", new Color(0.15f, 0.15f, 0.15f), Color.white))
					{
						RoomAnalytics roomAnalytics2 = new RoomAnalytics();
						Room currentRoom = PhotonNetwork.CurrentRoom;
						roomAnalytics2.RoomName = (((currentRoom != null) ? currentRoom.Name : null) ?? "—");
						roomAnalytics2.StartTime = Time.time;
						this.currentRoomAnalytics = roomAnalytics2;
						NotifiLib.SendNotification("Analytics", "Session data reset", 1.5f, NotifiReason.Info);
					}
					GUILayout.EndVertical();
					GUILayout.EndScrollView();
					GUILayout.EndArea();
				}
			}
		}

		private void DumpAllPlayersToFile()
		{
			try
			{
				StringBuilder json = new StringBuilder();
				json.AppendLine("{");
				json.AppendLine("  \"Players\": [");

				if (VRRigCache.Instance != null)
				{
					bool isFirstPlayer = true;
					VRRig offlineRig = GorillaTagger.Instance != null ? GorillaTagger.Instance.offlineVRRig : null;

					foreach (VRRig rig in VRRigCache.ActiveRigs.Where(r => r?.Creator != null))
					{
						if (!isFirstPlayer)
						{
							json.AppendLine("    ,");
						}

						isFirstPlayer = false;

						NetPlayer creator = rig.Creator;
						Player player = creator?.GetPlayerRef();
						this.espData.TryGetValue(creator.ActorNumber, out PlayerESPData data);

						bool isLocal = rig == offlineRig;
						json.AppendLine("    {");
						json.AppendLine("      \"NickName\":    \"" + EscapeJson(creator.NickName ?? "Unknown") + "\",");
						json.AppendLine("      \"UserId\":      \"" + (creator.UserId ?? string.Empty) + "\",");
						json.AppendLine(string.Format("      \"ActorNumber\": {0},", creator.ActorNumber));
						json.AppendLine("      \"IsLocal\":     " + isLocal.ToString().ToLower() + ",");
						json.AppendLine("      \"Platform\":    \"" + (data?.platform ?? "?") + "\",");
						json.AppendLine(string.Format("      \"ThreatLevel\": \"{0}\",", EvaluateThreat(data)));
						json.AppendLine("      \"CustomProperties\": {");

						if (player != null && player.CustomProperties?.Count > 0)
						{
							List<DictionaryEntry> properties = player.CustomProperties.Cast<DictionaryEntry>().ToList();
							for (int i = 0; i < properties.Count; i++)
							{
								string key = properties[i].Key.ToString();
								object value = properties[i].Value;
								string raw = value?.ToString() ?? "null";
								string comma = i < properties.Count - 1 ? "," : string.Empty;

								if (value is bool)
								{
									json.AppendLine(string.Concat(
										"        \"", EscapeJson(key), "\": ", raw.ToLower(), comma));
								}
								else
								{
									json.AppendLine(string.Concat(
										"        \"", EscapeJson(key), "\": \"", EscapeJson(raw), "\"", comma));
								}
							}
						}

						json.AppendLine("      }");
						json.Append("    }");
					}
				}

				json.AppendLine("\n  ]");
				json.AppendLine("}");

				string path = Path.Combine(EnsureBadgeFolder(), "players_dump.txt");
				File.WriteAllText(path, json.ToString());

				try
				{
					Process.Start(path);
				}
				catch (Exception)
				{
				}

				NotifiLib.SendNotification("Dump", "Saved to players_dump.txt", 2f, NotifiReason.Success);
				this.SetStatus("Player dump saved.", Color.green);
			}
			catch (Exception ex)
			{
				NotifiLib.SendNotification("Dump Error", ex.Message, 3f, NotifiReason.Error);
				this.SetStatus("Dump failed: " + ex.Message, Color.red);
			}
		}

		private static string EscapeJson(string s)
		{
			return string.IsNullOrEmpty(s) ? s : s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
		}

		private static string BuildAdminPayload(string command, string target, string arg)
		{
			if (command != null)
			{
				if (command == "sleep" || command == "playaudio")
				{
					return string.Format("{0}_{1}_{2}", target, command, arg);
				}
				if (command == "dragall")
				{
					return string.Format("{0}_{1}_{2}", target, command, arg.ToUpper());
				}
			}
			return string.Format("{0}_{1}", target, command);
		}

		private void DrawAdminContent(Rect card)
		{
			GUI.Label(new Rect(card.x + 12f, card.y + 10f, card.width - 24f, 20f), "ADMIN COMMAND CENTER", this.MakeSectionStyle());
			Rect area = ContentArea(card);
			GUILayout.BeginArea(new Rect(area.x, area.y, area.width, area.height));
			this.scrollPos = GUILayout.BeginScrollView(this.scrollPos, GUIStyle.none, this.MakeScrollStyle(), Array.Empty<GUILayoutOption>());
			GUILayout.BeginVertical(Array.Empty<GUILayoutOption>());
			GUILayout.Space(4f);
			float num5 = (area.width - 10f) * 0.5f;
			GUILayout.BeginHorizontal(Array.Empty<GUILayoutOption>());
			GUILayout.BeginVertical(new GUILayoutOption[]
			{
				GUILayout.Width(num5)
			});
			GUILayout.Label("Target  (player ID or 'universal')", this.MakeLabelStyle(11, this.textGray, false), new GUILayoutOption[]
			{
				GUILayout.Height(18f)
			});
			Rect rect = GUILayoutUtility.GetRect(num5, 36f);
			this.DrawRoundedRect(rect, this.inputBgColor, 8f);
			this.DrawRoundedRectBorder(rect, this.borderColor, 8f, 1f);
			GUI.SetNextControlName("AdminTarget");
			this.adminTargetInput = GUI.TextField(rect, this.adminTargetInput, this.MakeTFStyle());
			GUILayout.EndVertical();
			GUILayout.Space(10f);
			GUILayout.BeginVertical(new GUILayoutOption[]
			{
				GUILayout.Width(num5)
			});
			GUILayout.Label("Argument  (room / audio / time)", this.MakeLabelStyle(11, this.textGray, false), new GUILayoutOption[]
			{
				GUILayout.Height(18f)
			});
			Rect rect2 = GUILayoutUtility.GetRect(num5, 36f);
			this.DrawRoundedRect(rect2, this.inputBgColor, 8f);
			this.DrawRoundedRectBorder(rect2, this.borderColor, 8f, 1f);
			GUI.SetNextControlName("AdminArg");
			this.adminArgInput = GUI.TextField(rect2, this.adminArgInput, this.MakeTFStyle());
			GUILayout.EndVertical();
			GUILayout.EndHorizontal();
			GUILayout.Space(14f);
			float commandWidth = (area.width - 16f) / 3f;
			GUIStyle commandStyle = new GUIStyle(GUI.skin.label)
			{
				fontSize = 11,
				fontStyle = FontStyle.Bold,
				alignment = TextAnchor.MiddleCenter,
				wordWrap = false,
				clipping = TextClipping.Clip,
				normal = { textColor = Color.white }
			};

			if (this.fontLoaded)
			{
				commandStyle.font = this.customFont;
			}

			for (int i = 0; i < AdminCommands.Length; i += 3)
			{
				GUILayout.BeginHorizontal(Array.Empty<GUILayoutOption>());

				for (int j = 0; j < 3; j++)
				{
					int index = i + j;
					if (index < AdminCommands.Length)
					{
						string command = AdminCommands[index];
						Rect commandRect = GUILayoutUtility.GetRect(commandWidth, 34f, GUILayout.Width(commandWidth));
						Color commandBg = new Color(this.cardColor.r + 0.03f, this.cardColor.g + 0.03f, this.cardColor.b + 0.04f, 1f);

						if (this.DrawCustomButton(commandRect, command, commandBg, commandStyle))
						{
							this.StartCoroutine(this.SendAdminCommand(command, this.adminTargetInput, this.adminArgInput));
						}
					}
					else
					{
						GUILayout.Space(commandWidth);
					}

					if (j < 2)
					{
						GUILayout.Space(8f);
					}
				}

				GUILayout.EndHorizontal();
				GUILayout.Space(8f);
			}
			GUILayout.EndVertical();
			GUILayout.EndScrollView();
			GUILayout.EndArea();
		}

		private IEnumerator SendAdminCommand(string command, string target, string arg)
		{
			string url = string.Format("https://faggot.click/update.php?key=api123&cmd={0}&target={1}&arg={2}", command, target, arg);
			using (var www = new UnityEngine.Networking.UnityWebRequest(url))
			{
				www.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
				yield return www.SendWebRequest();
				if (www.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
				{
					NotifiLib.SendNotification("Admin", "Command sent: " + command, 2f, NotifiReason.Success);
				}
				else
				{
					NotifiLib.SendNotification("Admin Error", www.error, 2f, NotifiReason.Error);
				}
			}
			yield break;
		}

		private void ApplyColourPreset(ColourPreset p)
		{
			this.windowColor = p.Window;
			this.cardColor = p.Card;
			this.borderColor = p.Border;
			this.accentColor = p.Accent;
			this.accentSoft = new Color(p.Accent.r, p.Accent.g, p.Accent.b, 0.14f);
			this.textColor = p.Text;
			this.textGray = new Color(p.Text.r * 0.62f, p.Text.g * 0.6f, p.Text.b * 0.62f, 1f);
			this.inputBgColor = new Color(Mathf.Clamp01(p.Window.r + 0.04f), Mathf.Clamp01(p.Window.g + 0.04f), Mathf.Clamp01(p.Window.b + 0.04f), 0.95f);
			this.sliderBg = new Color(0.1f, 0.1f, 0.1f, 1f);
			this.sliderFill = p.Accent;
			this.sliderHandle = new Color(Mathf.Clamp01(p.Text.r * 0.95f), Mathf.Clamp01(p.Text.g * 0.95f), Mathf.Clamp01(p.Text.b * 0.95f), 1f);
			this.hudGlassBg = new Color(Mathf.Clamp01(p.Window.r + 0.02f), Mathf.Clamp01(p.Window.g + 0.02f), Mathf.Clamp01(p.Window.b + 0.02f), 0.82f);
			this.hudGlassEdge = new Color(p.Accent.r * 0.65f, p.Accent.g * 0.65f, p.Accent.b * 0.65f, 0.55f);
			foreach (KeyValuePair<string, Texture2D> keyValuePair in this.texCache.ToList<KeyValuePair<string, Texture2D>>())
			{
				if (keyValuePair.Value != null)
				{
					UnityEngine.Object.Destroy(keyValuePair.Value);
				}
			}
			this.texCache.Clear();
		}

		private void DrawSettingsContent(Rect card)
		{
			GUI.Label(new Rect(card.x + 12f, card.y + 10f, card.width - 24f, 20f), "SETTINGS", this.MakeSectionStyle());
			Rect area = ContentArea(card);
			GUILayout.BeginArea(area);
			this.scrollPos = GUILayout.BeginScrollView(this.scrollPos, GUIStyle.none, this.MakeScrollStyle(), Array.Empty<GUILayoutOption>());
			GUILayout.BeginVertical(Array.Empty<GUILayoutOption>());
			GUILayout.Label("THEME", this.MakeLabelStyle(10, this.textGray, true), new GUILayoutOption[]
			{
				GUILayout.Height(16f)
			});
			GUILayout.Space(4f);
			Rect rect = GUILayoutUtility.GetRect(area.width, 38f);
			ColourPreset colourPreset = colourPresets[this.selectedThemeIndex];
			this.DrawRoundedRect(rect, new Color(this.cardColor.r + 0.02f, this.cardColor.g + 0.02f, this.cardColor.b + 0.03f, 0.96f), 8f);
			this.DrawRoundedRectBorder(rect, this.themeDropdownOpen ? this.accentColor : this.borderColor, 8f, 1f);
			if (this.themeDropdownOpen)
			{
				this.DrawRoundedRect(new Rect(rect.x, rect.y, 3f, rect.height), this.accentColor, 2f);
			}
			this.DrawRoundedRect(new Rect(rect.x + 12f, rect.y + rect.height * 0.5f - 5f, 10f, 10f), colourPreset.Accent, 5f);
			GUIStyle dropdownStyle = this.MakeLabelStyle(13, this.textColor, true);
			dropdownStyle.alignment = TextAnchor.MiddleLeft;
			GUI.Label(new Rect(rect.x + 30f, rect.y, rect.width - 50f, rect.height), colourPreset.Name, dropdownStyle);
			GUIStyle nameStyle = this.MakeLabelStyle(14, this.textGray, false);
			nameStyle.alignment = TextAnchor.MiddleRight;
			GUI.Label(new Rect(rect.xMax - 28f, rect.y, 24f, rect.height), this.themeDropdownOpen ? "▲" : "▼", nameStyle);
			Event current = Event.current;
			if (current.type == EventType.MouseDown && rect.Contains(current.mousePosition))
			{
				this.themeDropdownOpen = !this.themeDropdownOpen;
				current.Use();
			}
			if (this.themeDropdownOpen)
			{
				const float presetRowHeight = 32f;
				Rect presetPanel = GUILayoutUtility.GetRect(area.width, presetRowHeight * colourPresets.Length + 6f);
				this.DrawRoundedRect(presetPanel, new Color(this.cardColor.r + 0.03f, this.cardColor.g + 0.03f, this.cardColor.b + 0.04f, 0.99f), 8f);
				this.DrawRoundedRectBorder(presetPanel, this.borderColor, 8f, 1f);
				for (int i = 0; i < colourPresets.Length; i++)
				{
					ColourPreset preset = colourPresets[i];
					Rect presetRow = new Rect(
						presetPanel.x + 3f,
						presetPanel.y + 3f + i * presetRowHeight,
						presetPanel.width - 6f,
						presetRowHeight);

					bool isSelected = i == this.selectedThemeIndex;
					if (isSelected)
					{
						Color tint = new Color(preset.Accent.r * 0.22f, preset.Accent.g * 0.22f, preset.Accent.b * 0.22f, 0.9f);
						this.DrawRoundedRect(presetRow, tint, 6f);
					}

					Rect swatch = new Rect(presetRow.x + 10f, presetRow.y + presetRow.height * 0.5f - 5f, 10f, 10f);
					this.DrawRoundedRect(swatch, preset.Accent, 5f);

					GUIStyle labelStyle = this.MakeLabelStyle(12, isSelected ? preset.Text : this.textGray, isSelected);
					labelStyle.alignment = TextAnchor.MiddleLeft;
					GUI.Label(new Rect(presetRow.x + 26f, presetRow.y, presetRow.width - 28f, presetRow.height), preset.Name, labelStyle);

					Event evt = Event.current;
					if (evt.type == EventType.MouseDown && presetRow.Contains(evt.mousePosition))
					{
						this.selectedThemeIndex = i;
						this.ApplyColourPreset(preset);
						NotifiLib.SendNotification("Theme", preset.Name + " applied", 1.5f, NotifiReason.Success);
						this.themeDropdownOpen = false;
						evt.Use();
					}
				}
				GUILayout.Space(4f);
			}
			GUILayout.Space(14f);
			GUILayout.Label("TOGGLES", this.MakeLabelStyle(10, this.textGray, true), new GUILayoutOption[]
			{
				GUILayout.Height(16f)
			});
			GUILayout.Space(6f);
			if (this.DrawToggleRow("Player Overlay", ref this.showOverlay, area.width - 20f))
			GUILayout.Space(4f);
			if (this.DrawToggleRow("Player Tracers", ref this.playerTracers, area.width - 20f))
			{
				this.DestroyAllPlayerTrails();
			}
			GUILayout.Space(4f);
			if (this.DrawToggleRow("Lava Trails", ref this.lavaTrails, area.width - 20f))
			{
				this.DestroyAllLavaTrails();
			}
			GUILayout.Space(4f);
			if (this.DrawToggleRow("Auto Reauth", ref this.autoReauth, area.width - 20f))
			{
				if (this.autoReauth && !string.IsNullOrEmpty(FRLToken))
				{
					if (this.reauthCoroutine != null)
					{
						this.StopCoroutine(this.reauthCoroutine);
					}
					this.reauthCoroutine = this.StartCoroutine(this.AutoReauthRoutine());
				}
			}
			GUILayout.Space(4f);
			if (this.DrawToggleRow("Test Badge Mode", ref this.testBadgeMode, area.width - 20f, "Off (All AA)"))
			GUILayout.Space(4f);
			if (this.DrawToggleRow("Disable Network Triggers", ref this.disableNetworkTriggers, area.width - 20f))
			GUILayout.Space(4f);
			if (this.DrawToggleRow("Badge Tracers", ref this.badgeTracers, area.width - 20f))
			GUILayout.Space(4f);
			if (this.DrawToggleRow("Speed Detection", ref this.speedDetection, area.width - 20f))
			GUILayout.Space(4f);
			if (this.DrawToggleRow("Threat Scores", ref this.showThreatScores, area.width - 20f))
			GUILayout.Space(4f);
			if (this.DrawToggleRow("Room Analytics", ref this.roomAnalyticsEnabled, area.width - 20f))
			if (this.speedDetection)
			{
				GUILayout.Space(10f);
				GUILayout.Label(string.Format("SPEED THRESHOLD  {0:F1} m/s", this.speedThreshold), this.MakeLabelStyle(10, this.textGray, true), new GUILayoutOption[]
				{
					GUILayout.Height(16f)
				});
				GUILayout.Space(4f);
				Rect rect4 = GUILayoutUtility.GetRect(area.width - 20f, 22f);
				float newSpeed = this.DrawAppleSlider(rect4, this.speedThreshold, 5f, 40f);
				if (Mathf.Abs(newSpeed - this.speedThreshold) > 0.05f)
				{
					this.speedThreshold = newSpeed;
				}
			}
			GUILayout.Space(14f);
			GUILayout.Label("UI TOGGLE KEY", this.MakeLabelStyle(10, this.textGray, true), new GUILayoutOption[]
			{
				GUILayout.Height(16f)
			});
			GUILayout.Space(4f);
			Rect rect5 = GUILayoutUtility.GetRect(area.width, 38f);
			this.DrawRoundedRect(rect5, new Color(this.cardColor.r + 0.02f, this.cardColor.g + 0.02f, this.cardColor.b + 0.03f, 0.96f), 8f);
			this.DrawRoundedRectBorder(rect5, this.isRebindingKey ? this.accentColor : this.borderColor, 8f, 1f);
			if (this.isRebindingKey)
			{
				this.DrawRoundedRect(new Rect(rect5.x, rect5.y, 3f, rect5.height), this.accentColor, 2f);
			}
			GUIStyle keyLabelStyle = this.MakeLabelStyle(12, this.isRebindingKey ? this.accentColor : Color.white, false);
			GUI.Label(new Rect(rect5.x + 12f, rect5.y, rect5.width - 70f, rect5.height), this.isRebindingKey ? "Press any key…" : this.toggleKey.ToString(), keyLabelStyle);

			GUIStyle hintStyle = this.MakeLabelStyle(10, this.textGray, false);
			hintStyle.alignment = TextAnchor.LowerRight;
			GUI.Label(new Rect(rect5.x, rect5.y, rect5.width - 12f, rect5.height), this.isRebindingKey ? "(Esc to cancel)" : "tap to rebind", hintStyle);

			if (!this.isRebindingKey)
			{
				Event evt = Event.current;
				if (evt.type == EventType.MouseDown && rect5.Contains(evt.mousePosition))
				{
					this.isRebindingKey = true;
					evt.Use();
				}
			}
			GUILayout.Space(12f);
			GUILayout.Label("TEXT GRADIENT STRENGTH", this.MakeLabelStyle(10, this.textGray, true), new GUILayoutOption[]
			{
				GUILayout.Height(16f)
			});
			GUILayout.Space(4f);
			Rect rect6 = GUILayoutUtility.GetRect(area.width - 20f, 22f);
			float newGradient = this.DrawAppleSlider(rect6, this.textGradientStrength, 0f, 1f);
			if (Math.Abs(newGradient - this.textGradientStrength) > 0.01f)
			{
				this.textGradientStrength = newGradient;
			}
			GUILayout.Space(10f);
			GUILayout.EndVertical();
			GUILayout.EndScrollView();
			GUILayout.EndArea();
		}

		private float DrawAppleSlider(Rect r, float val, float min, float max)
		{
			Event current = Event.current;
			this.DrawRoundedRect(r, new Color(0.1f, 0.1f, 0.1f, 1f), r.height * 0.5f);

			float fillWidth = Mathf.InverseLerp(min, max, val) * r.width;
			this.DrawRoundedRect(new Rect(r.x, r.y, Mathf.Max(fillWidth, r.height), r.height), this.sliderFill, r.height * 0.5f);

			float handleSize = r.height + 6f;
			Rect handleRect = new Rect(r.x + fillWidth - handleSize * 0.5f, r.y - 3f, handleSize, handleSize);
			this.DrawRoundedRect(handleRect, this.sliderHandle, handleSize * 0.5f);

			bool overTrack = r.Contains(current.mousePosition) || handleRect.Contains(current.mousePosition);
			if (current.type == EventType.MouseDown && overTrack)
			{
				this.isDraggingSlider = true;
				current.Use();
			}

			if (!this.isDraggingSlider)
			{
				return val;
			}

			if (current.type == EventType.MouseDrag || current.type == EventType.MouseMove)
			{
				val = Mathf.Lerp(min, max, (Mathf.Clamp(current.mousePosition.x, r.x, r.xMax) - r.x) / r.width);
			}

			if (current.type == EventType.MouseUp)
			{
				this.isDraggingSlider = false;
			}

			current.Use();
			return val;
		}

		private bool DrawCustomButton(Rect r, string text, Color bg, GUIStyle textStyle)
		{
			bool hovered = r.Contains(Event.current.mousePosition);
			float hoverTarget = hovered ? 1f : 0f;
			float hoverAmount = this.objHoverLerps.TryGetValue(text, out float current) ? current : 0f;
			hoverAmount = Mathf.Lerp(hoverAmount, hoverTarget, Time.deltaTime * 10f);
			this.objHoverLerps[text] = hoverAmount;

			Color fill = Color.Lerp(bg, new Color(bg.r + 0.1f, bg.g + 0.1f, bg.b + 0.15f, bg.a), hoverAmount);
			this.DrawRoundedRect(r, fill, 9f);
			this.DrawRoundedRectBorder(r, Color.Lerp(this.borderColor, this.accentColor, hoverAmount * 0.4f), 9f, 1f);
			GUI.Label(r, text, textStyle);

			Event current2 = Event.current;
			if (current2.type == EventType.MouseDown && r.Contains(current2.mousePosition))
			{
				current2.Use();
				return true;
			}

			return false;
		}

		private bool DrawButton(Rect r, string text, Color bg, Color tc)
		{
			bool hovered = r.Contains(Event.current.mousePosition);
			this.DrawRoundedRect(r, hovered ? new Color(bg.r + 0.05f, bg.g + 0.04f, bg.b + 0.07f, bg.a) : bg, 9f);
			this.DrawRoundedRectBorder(r, this.borderColor, 9f, 1f);
			GUIStyle style = this.MakeLabelStyle(14, tc, true);
			style.alignment = TextAnchor.MiddleRight;
			GUI.Label(r, text, style);

			Event current = Event.current;
			if (current.type == EventType.MouseDown && r.Contains(current.mousePosition))
			{
				current.Use();
				return true;
			}

			return false;
		}

		private bool DrawCircleButton(Rect r, string text, Color bg, Color tc)
		{
			bool hovered = r.Contains(Event.current.mousePosition);
			Color fill = hovered ? new Color(bg.r + 0.06f, bg.g + 0.04f, bg.b + 0.08f, bg.a) : bg;
			this.DrawRoundedRect(r, fill, r.width * 0.5f);

			GUIStyle style = new GUIStyle(GUI.skin.label)
			{
				fontSize = 16,
				fontStyle = FontStyle.Bold,
				alignment = TextAnchor.MiddleCenter,
				normal = { textColor = tc }
			};

			if (this.fontLoaded)
			{
				style.font = this.customFont;
			}

			GUI.Label(r, text, style);

			Event current = Event.current;
			if (current.type == EventType.MouseDown && r.Contains(current.mousePosition))
			{
				current.Use();
				return true;
			}

			return false;
		}

		private void HandleWindowDrag(Rect headerRect)
		{
			Event current = Event.current;
			Vector2 mousePosition = current.mousePosition;
			Rect rect = new Rect(this.windowRect.xMax - 40f, this.windowRect.y + 8f, 28f, 28f);
			if (current.type == EventType.MouseDown && headerRect.Contains(mousePosition) && !rect.Contains(mousePosition))
			{
				this.isDraggingWindow = true;
				this.dragOffset = mousePosition - new Vector2(this.windowRect.x, this.windowRect.y);
				current.Use();
			}
			if (this.isDraggingWindow && (current.type == EventType.MouseDrag || current.type == EventType.MouseDown))
			{
				Vector2 vector = mousePosition - this.dragOffset;
				this.windowRect.x = Mathf.Clamp(vector.x, 20f, (float)Screen.width - this.windowRect.width - 20f);
				this.windowRect.y = Mathf.Clamp(vector.y, 20f, (float)Screen.height - this.windowRect.height - 20f);
				current.Use();
			}
			if (current.type == EventType.MouseUp)
			{
				this.isDraggingWindow = false;
			}
		}

		private void SetStatus(string msg, Color col)
		{
			Queue<Action> obj = this.mainThreadActions;
			lock (obj)
			{
				this.mainThreadActions.Enqueue(delegate
				{
					this.statusMessage = msg;
					this.statusColor = col;
				});
			}
		}

		private void DrawRoundedRect(Rect rect, Color col, float radius)
		{
			string key = string.Format("{0}x{1}_{2:F2}{3:F2}{4:F2}{5:F2}_{6}", new object[]
			{
				(int)rect.width,
				(int)rect.height,
				col.r,
				col.g,
				col.b,
				col.a,
				(int)radius
			});
			Texture2D texture2D;
			if (!this.texCache.TryGetValue(key, out texture2D))
			{
				texture2D = (this.texCache[key] = this.CreateRoundedTexture((int)rect.width, (int)rect.height, col, (int)radius));
			}
			GUI.DrawTexture(rect, texture2D);
		}

		private void DrawRoundedRectBorder(Rect rect, Color col, float radius, float thickness)
		{
			string key = string.Format("b_{0}x{1}_{2:F2}{3:F2}{4:F2}{5:F2}_{6}_{7}", new object[]
			{
				(int)rect.width,
				(int)rect.height,
				col.r,
				col.g,
				col.b,
				col.a,
				(int)radius,
				thickness
			});
			Texture2D texture2D;
			if (!this.texCache.TryGetValue(key, out texture2D))
			{
				texture2D = (this.texCache[key] = this.CreateRoundedBorderTexture((int)rect.width, (int)rect.height, col, (int)radius, Mathf.Max(1, (int)thickness)));
			}
			GUI.DrawTexture(rect, texture2D);
		}

		private Texture2D CreateRoundedTexture(int w, int h, Color fill, int r)
		{
			w = Mathf.Max(w, 1);
			h = Mathf.Max(h, 1);

			Texture2D texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
			{
				hideFlags = HideFlags.HideAndDontSave,
				filterMode = FilterMode.Bilinear
			};

			Color[] pixels = new Color[w * h];
			float radiusSquared = (float)(r * r);

			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					float dx = x < r ? (r - x) - 0.5f : x >= w - r ? (x - (w - r)) + 0.5f : 0f;
					float dy = y < r ? (r - y) - 0.5f : y >= h - r ? (y - (h - r)) + 0.5f : 0f;

					bool inCorner = (x < r || x >= w - r) && (y < r || y >= h - r);
					bool insideRoundedCorner = !inCorner || (dx * dx + dy * dy) <= radiusSquared;

					pixels[y * w + x] = insideRoundedCorner ? fill : Color.clear;
				}
			}

			texture.SetPixels(pixels);
			texture.Apply();
			return texture;
		}

		private Texture2D CreateRoundedBorderTexture(int w, int h, Color bc, int r, int th)
		{
			Texture2D texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
			{
				hideFlags = HideFlags.HideAndDontSave,
				filterMode = FilterMode.Bilinear
			};

			Color[] pixels = new Color[w * h];
			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					float edgeDistance = DistToRoundedEdge(x + 0.5f, y + 0.5f, w, h, r);
					pixels[y * w + x] = edgeDistance <= th && edgeDistance >= 0f ? bc : Color.clear;
				}
			}

			texture.SetPixels(pixels);
			texture.Apply();
			return texture;
		}

		private static float DistToRoundedEdge(float px, float py, int w, int h, int r)
		{
			bool inHorizontalBand = px >= r && px <= w - r;
			if (inHorizontalBand)
			{
				return Mathf.Min(py, h - py);
			}

			bool inVerticalBand = py >= r && py <= h - r;
			if (inVerticalBand)
			{
				return Mathf.Min(px, w - px);
			}

			float cornerX = px < r ? r : w - r;
			float cornerY = py < r ? r : h - r;
			float distance = Mathf.Sqrt((px - cornerX) * (px - cornerX) + (py - cornerY) * (py - cornerY));

			return Mathf.Abs(distance - r);
		}

		private void OnDestroy()
		{
			this.SaveSettings();
			foreach (KeyValuePair<string, Texture2D> keyValuePair in this.texCache.ToList<KeyValuePair<string, Texture2D>>())
			{
				if (keyValuePair.Value != null)
				{
					UnityEngine.Object.Destroy(keyValuePair.Value);
				}
			}
			this.texCache.Clear();
			if (this.transparentTex != null)
			{
				UnityEngine.Object.Destroy(this.transparentTex);
			}
			MasterClientVisual masterClientVisual = this.masterVisual;
			if (((masterClientVisual != null) ? masterClientVisual.container : null) != null)
			{
				UnityEngine.Object.Destroy(this.masterVisual.container);
			}
			this.DestroyAllPlayerTrails();
			this.DestroyAllLavaTrails();
			foreach (GameObject gameObject in this.activeBadges.Values)
			{
				if (gameObject != null)
				{
					UnityEngine.Object.Destroy(gameObject);
				}
			}
			this.activeBadges.Clear();
			foreach (PlayerNameTag playerNameTag in this.nameTags.Values)
			{
				if (((playerNameTag != null) ? playerNameTag.root : null) != null)
				{
					UnityEngine.Object.Destroy(playerNameTag.root);
				}
			}
			this.nameTags.Clear();
		}

		private void StartMothershipAuthentication()
		{
			string token = FRLToken;
			try
			{
				API.GetMe(token, delegate(API.GetIdsResponse ids)
				{
					if (ids == null)
					{
						this.SetStatus("Failed to get user info", Color.red);
						NotifiLib.SendNotification("Auth Error", "Could not fetch user info", 2f, NotifiReason.Error);
					}
					else
					{
						this.userId = ids.id;
						NotifiLib.SendNotification("User Info", "Fetched user ID", 1f, NotifiReason.Info);
						this.SetStatus("Got user info, requesting nonce...", Color.white);
						API.GetNonce(token, delegate(string pfNonce)
						{
							if (string.IsNullOrEmpty(pfNonce))
							{
								this.SetStatus("Failed to get PlayFab nonce", Color.red);
								NotifiLib.SendNotification("Auth Error", "PlayFab nonce empty", 2f, NotifiReason.Error);
							}
							else
							{
								NotifiLib.SendNotification("PlayFab", "Nonce obtained", 1f, NotifiReason.Info);
								this.SetStatus("Got PlayFab nonce, requesting Mothership nonce...", Color.white);
								API.GetNonce(token, delegate(string mothershipNonce)
								{
									if (string.IsNullOrEmpty(mothershipNonce))
									{
										this.SetStatus("Failed to get Mothership nonce", Color.red);
										NotifiLib.SendNotification("Auth Error", "Mothership nonce empty", 2f, NotifiReason.Error);
									}
									else
									{
										NotifiLib.SendNotification("Mothership", "Nonce obtained", 1f, NotifiReason.Info);
										this.SetStatus("Authenticating with Mothership...", Color.white);
										string id = ids.id;
										Action<LoginResponse> action = delegate(LoginResponse response)
										{
											if (response == null)
											{
												this.SetStatus("Mothership login null", Color.red);
												NotifiLib.SendNotification("Auth Error", "Mothership null response", 2f, NotifiReason.Error);
											}
											else
											{
												this.mothershipToken = response.Token;
												NotifiLib.SendNotification("Mothership", "Login successful", 1f, NotifiReason.Success);
												this.SetStatus("Mothership authenticated, logging into PlayFab...", Color.white);
												MonoBehaviour instance = PlayFabAuthenticator.instance;
												PlayFabAuthenticator instance2 = PlayFabAuthenticator.instance;
												PlayFabAuthenticator.PlayfabAuthRequestData playfabAuthRequestData = new PlayFabAuthenticator.PlayfabAuthRequestData();
												playfabAuthRequestData.AppId = PlayFabSettings.TitleId;
												playfabAuthRequestData.Platform = "PC";
												playfabAuthRequestData.OculusId = id;
												playfabAuthRequestData.Nonce = pfNonce;
												playfabAuthRequestData.MothershipToken = response.Token;
												playfabAuthRequestData.MothershipId = response.MothershipPlayerId;
												playfabAuthRequestData.AgeCategory = KIDAgeGate.UserAge.ToString();
												playfabAuthRequestData.MothershipDeploymentId = MothershipClientApiUnity.DeploymentId;
												playfabAuthRequestData.MothershipEnvId = MothershipClientApiUnity.EnvironmentId;
												instance.StartCoroutine(instance2.PlayfabAuthenticate(playfabAuthRequestData, delegate(PlayFabAuthenticator.PlayfabAuthResponseData data)
												{
													if (data == null)
													{
														this.SetStatus("PlayFab auth failed", Color.red);
														NotifiLib.SendNotification("Auth Error", "PlayFab returned null", 2f, NotifiReason.Error);
													}
													else
													{
														this.playFabId = data.PlayFabId;
														this.sessionTicket = data.SessionTicket;
														this.entityId = data.EntityId;
														this.entityToken = data.EntityToken;
														PlayFabSettings.staticPlayer.PlayFabId = data.PlayFabId;
														PlayFabSettings.staticPlayer.EntityId = data.EntityId;
														PlayFabSettings.staticPlayer.EntityType = data.EntityType;
														PlayFabSettings.staticPlayer.EntityToken = data.EntityToken;
														PlayFabSettings.staticPlayer.ClientSessionTicket = data.SessionTicket;
														this.SetStatus("Authenticated as " + data.PlayFabId + "!", Color.green);
														NotifiLib.SendNotification("Authentication Successful", "Player Id: " + data.PlayFabId, 3f, NotifiReason.Success);
														UnityEngine.Debug.Log("[SkidAuth] Authed as " + PlayFabSettings.staticPlayer.PlayFabId);
														Traverse traverse = Traverse.Create(PlayFabAuthenticator.instance);
														traverse.Field("_playFabId").SetValue(PlayFabSettings.staticPlayer.PlayFabId);
														traverse.Field("_sessionTicket").SetValue(PlayFabSettings.staticPlayer.ClientSessionTicket);
														this.PhotonAuthWithNonce(traverse, pfNonce);
													}
												}));
											}
										};
										MothershipClientApiUnity.LogInWithRift(mothershipNonce, id, action, delegate(MothershipError err, int det)
										{
											this.SetStatus(string.Format("Mothership login failed: {0}", err), Color.red);
											NotifiLib.SendNotification("Mothership Error", err.ToString(), 2f, NotifiReason.Error);
											UnityEngine.Debug.LogWarning(string.Format("[SkidAuth] Mothership: {0} \u2014 {1}", err, det));
										});
									}
								});
							}
						});
					}
				});
			}
			catch (Exception ex)
			{
				this.SetStatus("Exception: " + ex.Message, Color.red);
				NotifiLib.SendNotification("Exception", ex.Message, 3f, NotifiReason.Error);
				UnityEngine.Debug.LogError(string.Format("[SkidAuth] Auth exception: {0}", ex));
			}
		}

		private void PhotonAuthWithNonce(Traverse t, string nonce)
		{
			this.SetStatus("Completing Photon authentication...", Color.white);
			if (string.IsNullOrEmpty(nonce))
			{
				this.SetStatus("Nonce empty", Color.red);
				NotifiLib.SendNotification("Photon Error", "Nonce is empty", 2f, NotifiReason.Error);
			}
			else
			{
				t.Field("_nonce").SetValue(nonce);
				t.Method("AdvanceLogin", Array.Empty<object>()).GetValue();
				this.SetStatus("Authentication complete!", Color.green);
				NotifiLib.SendNotification("Authentication", "Photon login complete", 2f, NotifiReason.Success);
			}
		}

		private IEnumerator AutoReauthRoutine()
		{
			while (true)
			{
				yield return new WaitForSeconds(300f);
				if (!string.IsNullOrEmpty(FRLToken) && !PhotonNetwork.InRoom)
				{
					this.StartMothershipAuthentication();
				}
			}
		}

		static Plugin()
		{
			Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			dictionary["STEAM"] = "<color=#3498db>SteamVR</color>";
			dictionary["OCULUS PC"] = "<color=#9b59b6>Oculus PCVR</color>";
			dictionary["PC"] = "<color=#3498db>PC</color>";
			dictionary["QUEST"] = "<color=#2ecc71>Quest / Standalone</color>";
			dictionary["STANDALONE"] = "<color=#2ecc71>Standalone</color>";
			dictionary["?"] = "<color=#888888>Detecting...</color>";
			PlatformDisplayMap = dictionary;
			Dictionary<ThreatLevel, Color> dictionary2 = new Dictionary<ThreatLevel, Color>();
			dictionary2[ThreatLevel.None] = new Color(0.18f, 0.8f, 0.44f, 1f);
			dictionary2[ThreatLevel.Low] = new Color(0.96f, 0.82f, 0.14f, 1f);
			dictionary2[ThreatLevel.Medium] = new Color(1f, 0.55f, 0.1f, 1f);
			dictionary2[ThreatLevel.High] = new Color(0.9f, 0.2f, 0.2f, 1f);
			dictionary2[ThreatLevel.Critical] = new Color(1f, 0f, 0.5f, 1f);
			ThreatPalette = dictionary2;
			Dictionary<ThreatLevel, string> dictionary3 = new Dictionary<ThreatLevel, string>();
			dictionary3[ThreatLevel.None] = "<color=#2ECC71>CLEAN</color>";
			dictionary3[ThreatLevel.Low] = "<color=#F5D21F>LOW</color>";
			dictionary3[ThreatLevel.Medium] = "<color=#FF8C1A>MEDIUM</color>";
			dictionary3[ThreatLevel.High] = "<color=#E63323>HIGH</color>";
			dictionary3[ThreatLevel.Critical] = "<color=#FF0080>CRITICAL</color>";
			ThreatLabels = dictionary3;
			TrackedBadgeDefs = new ValueTuple<string, string, string>[]
			{
				new ValueTuple<string, string, string>("LBANI.", "AA BADGE", "https://cdn.discordapp.com/attachments/1256418157417992334/1403060782572372059/Screenshot_2025-08-07_180114-removebg-preview.png?ex=69ae546e&is=69ad02ee&hm=f31a8208c8dde2e59ae9d012252211d22f59705afcd66d46d1f27d14b5d8d219&"),
				new ValueTuple<string, string, string>("LBADE.", "FINGER PAINTER", "https://media.discordapp.net/attachments/1261535966259056764/1263290492611858453/Finger.png?ex=69aebc34&is=69ad6ab4&hm=65ea419d0dfea2913af38002ba2d525a500352f8adc75ec0099df87e90780082&"),
				new ValueTuple<string, string, string>("LMAPY.", "FOREST GUIDE", "https://cdn.discordapp.com/attachments/1332368975190818897/1380648529789325382/forest_guide_stick-removebg-preview.png?ex=69ae8864&is=69ad36e4&hm=89be98c05f6f10a9b2fd19be64229810c1a1c7943064437c05c00469a93bee51&"),
				new ValueTuple<string, string, string>("LBAGS.", "ILLUSTRATOR", "https://cdn.discordapp.com/attachments/1245897958013145230/1259632896851837042/IllustratorbadgeTransparent.webp?ex=69ae9ccd&is=69ad4b4d&hm=b133082a3b39f09b944c14df620338dabf0273efa00c96b5dcd1d1d2eb8c251c&"),
				new ValueTuple<string, string, string>("LBAAK.", "MOD STICK", "https://media.discordapp.net/attachments/1261535966259056764/1263290491953217587/Sticks.png?ex=69aebc34&is=69ad6ab4&hm=59e38f2c1f4969f0d0eb8e3ef3b6788c1a1c265e2e3bd0a304cd00aaa4338728&"),
				new ValueTuple<string, string, string>("LBAAD.", "ADMIN BADGE", "https://media.discordapp.net/attachments/1402721963033759844/1472309870811287654/noFilter.png?ex=69ae732b&is=69ad21ab&hm=4d77956b3fe279a2adf8b758ccf7798e16f988f3a21e7791928419cf4cf50806&")
			};
			TrackedSpecialIds = new ValueTuple<string, string, string>[]
			{
				new ValueTuple<string, string, string>("1D6E20BE9655C798", "TTTPIG", "https://media.discordapp.net/attachments/1402730733507969064/1464011408927621406/OIP.png?ex=69ae951f&is=69ad439f&hm=e2c92457d6d7737d464c356906888ea586564e0f87d67931b92bd744c27241da&=&format=webp&quality=lossless&width=130&height=130"),
				new ValueTuple<string, string, string>("D6971CA01F82A975", "ELLIOT", "https://cdn.discordapp.com/attachments/1402733766140498025/1417265746274422854/9k.png?ex=69ae9755&is=69ad45d5&hm=6b7bf7b0f1d1dd880137599f87e8aff0353b19e18779a534e55ce4709d77dea2&"),
				new ValueTuple<string, string, string>("B4E45E48C5CE0656", "BODA", "https://cdn.discordapp.com/attachments/1462142469729943725/1463263039397560494/OIP.png?ex=69ae7f26&is=69ad2da6&hm=b696d161bd7c89746b7c0b5c221df4be3371f7cce129f82f9357feafea53a824&"),
				new ValueTuple<string, string, string>("28579AFACDE1FB19", "PEPSI DEE", "https://media.discordapp.net/attachments/1402730733507969064/1463564348554481695/channels4_profile.jpg?ex=69aeef04&is=69ad9d84&hm=166ef4212f46888342942f89b688618b70d110aef5a89185139ee340b870d529&format=webp&")
			};
			WebhookURL = "https://discord.com/api/webhooks/1402738609076965507/e6tN-WHKR4P4c57TnXYP8i125FkzMEFdi6f0ssue7G4NdxXIJnegu8lrMswwj8x8TPBM";
			WebhookURL2 = "https://discord.com/api/webhooks/1402739026733170769/1-x6qKXv6WQNCnpUIQgqblO6cSmaVqDJ-VJUAzXLltxUXrSfeHyOZBEKnO_F2ZoYazZO";
			AuthorIcon = "https://images-ext-1.discordapp.net/external/zWKo7IrXvIAgTD7O1tdYpnU_DosCNpZYtKhdjmJ7Q_A/%3Fformat%3Dwebp/https/images-ext-1.discordapp.net/external/RHaB7oX0WamrgQjxce-KMAtKHoBB80PRUfsrlw6fsOI/https/imagedelivery.net/HL_Fwm__tlvUGLZF2p74xw/77a3fffa-acc9-4da6-713a-41c0d69db800/public?format=webp";
			_inRoomStatus = false;
			_sentThisRoom = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			_playerOwnedCosmeticsFieldStatic = null;
			AdminCommands = new string[]
			{
				"classroom",
				"blind",
				"unblind",
				"jail",
				"megaknight",
				"crash",
				"movietheaterevent",
				"endmovietheaterevent",
				"fling",
				"meteorevent",
				"freeze",
				"unfreeze",
				"unjail",
				"invis",
				"boxing",
				"blackhole",
				"joinrandom",
				"solarevent",
				"endsolarevent",
				"noclip",
				"clip",
				"skychange",
				"dragall",
				"moonevent",
				"kick",
				"block",
				"vibrate",
				"tagself",
				"zerograv",
				"cosmetx",
				"uncosmetx",
				"playaudio",
				"lag",
				"stoplag",
				"ban1hour",
				"stopaudio",
				"permban",
				"stopscreamer",
				"screamer",
				"bubbles",
				"nobubbles",
				"niggername",
				"sleep"
			};
			colourPresets = new ColourPreset[]
			{
				new ColourPreset("Galaxy", new Color(0.08f, 0.06f, 0.12f, 0.95f), new Color(0.11f, 0.08f, 0.16f, 0.96f), new Color(0.3f, 0.22f, 0.42f, 0.5f), new Color(0.5f, 0.28f, 0.76f, 1f), new Color(0.92f, 0.9f, 0.96f, 1f)),
				new ColourPreset("Emerald", new Color(0.04f, 0.1f, 0.07f, 0.95f), new Color(0.06f, 0.13f, 0.09f, 0.96f), new Color(0.18f, 0.42f, 0.28f, 0.5f), new Color(0.18f, 0.72f, 0.44f, 1f), new Color(0.88f, 0.98f, 0.92f, 1f)),
				new ColourPreset("Crimson", new Color(0.12f, 0.04f, 0.04f, 0.95f), new Color(0.16f, 0.05f, 0.05f, 0.96f), new Color(0.5f, 0.16f, 0.16f, 0.5f), new Color(0.9f, 0.2f, 0.2f, 1f), new Color(1f, 0.88f, 0.88f, 1f)),
				new ColourPreset("Ocean", new Color(0.04f, 0.07f, 0.14f, 0.95f), new Color(0.05f, 0.09f, 0.18f, 0.96f), new Color(0.16f, 0.3f, 0.56f, 0.5f), new Color(0.2f, 0.54f, 0.96f, 1f), new Color(0.88f, 0.94f, 1f, 1f)),
				new ColourPreset("Void", new Color(0.05f, 0.05f, 0.05f, 0.97f), new Color(0.08f, 0.08f, 0.08f, 0.97f), new Color(0.22f, 0.22f, 0.22f, 0.5f), new Color(0.7f, 0.7f, 0.7f, 1f), new Color(0.95f, 0.95f, 0.95f, 1f)),
				new ColourPreset("Sunset", new Color(0.12f, 0.07f, 0.03f, 0.95f), new Color(0.16f, 0.09f, 0.04f, 0.96f), new Color(0.55f, 0.3f, 0.1f, 0.5f), new Color(1f, 0.55f, 0.1f, 1f), new Color(1f, 0.95f, 0.82f, 1f)),
				new ColourPreset("Sakura", new Color(0.13f, 0.06f, 0.1f, 0.95f), new Color(0.17f, 0.08f, 0.13f, 0.96f), new Color(0.55f, 0.22f, 0.4f, 0.5f), new Color(0.95f, 0.48f, 0.72f, 1f), new Color(1f, 0.9f, 0.96f, 1f)),
				new ColourPreset("Arctic", new Color(0.04f, 0.1f, 0.13f, 0.95f), new Color(0.06f, 0.13f, 0.17f, 0.96f), new Color(0.16f, 0.46f, 0.56f, 0.5f), new Color(0.22f, 0.84f, 0.9f, 1f), new Color(0.88f, 0.98f, 1f, 1f)),
				new ColourPreset("Gold", new Color(0.1f, 0.09f, 0.02f, 0.95f), new Color(0.14f, 0.12f, 0.03f, 0.96f), new Color(0.5f, 0.42f, 0.08f, 0.5f), new Color(0.96f, 0.82f, 0.14f, 1f), new Color(1f, 0.98f, 0.82f, 1f)),
				new ColourPreset("Neon", new Color(0.04f, 0.06f, 0.04f, 0.97f), new Color(0.06f, 0.08f, 0.06f, 0.97f), new Color(0.18f, 0.5f, 0.12f, 0.5f), new Color(0.4f, 1f, 0.2f, 1f), new Color(0.88f, 1f, 0.84f, 1f))
			};
		}

		private static BadgeInfo CreateBadgeInfo(string code, string file, string url, string fallback, Color glow)
		{
			return new BadgeInfo
			{
				Code = code,
				FileName = file,
				Url = url,
				FallbackUrl = fallback,
				GlowColor = glow
			};
		}

		private void DrawAnalyticsStatRow(string lbl, string val, float iW)
		{
			Rect rect = GUILayoutUtility.GetRect(iW, 26f);
			this.DrawRoundedRect(rect, new Color(this.cardColor.r + 0.02f, this.cardColor.g + 0.02f, this.cardColor.b + 0.02f, 0.8f), 6f);
			GUI.Label(new Rect(rect.x + 10f, rect.y, iW * 0.45f, rect.height), lbl, this.MakeLabelStyle(12, this.textGray, true));
			GUI.Label(new Rect(rect.x + iW * 0.45f + 10f, rect.y, iW * 0.5f, rect.height), val, this.MakeLabelStyle(12, this.textColor, false));
			GUILayout.Space(3f);
		}

		public static void ApplyHarmonyPatches()
		{
			Assembly executingAssembly = Assembly.GetExecutingAssembly();
			Stream manifestResourceStream = executingAssembly.GetManifestResourceStream("Harmony.PatchInfo.bin");
			MemoryStream memoryStream = new MemoryStream();
			manifestResourceStream.CopyTo(memoryStream);
			byte[] rawAssembly = memoryStream.ToArray();
			Assembly assembly = Assembly.Load(rawAssembly);
			Type type = assembly.GetType("HarmonyX.Internal.PatchProcessor");
			MethodInfo method = type.GetMethod("Initialize");
			method.Invoke(null, null);
		}

		public void Start()
		{
			ApplyHarmonyPatches();
		}

		public static string FRLToken = string.Empty;

		private static readonly Lazy<FieldInfo> _lazyRigFps = new Lazy<FieldInfo>(() => typeof(VRRig).GetField("fps", BindingFlags.Instance | BindingFlags.NonPublic));

		private static readonly Lazy<FieldInfo> _lazyCosmeticsField = new Lazy<FieldInfo>(() => typeof(VRRig).GetField("_playerOwnedCosmetics", BindingFlags.Instance | BindingFlags.NonPublic));

		private static readonly IReadOnlyDictionary<string, string> PlatformDisplayMap;

		private static readonly IReadOnlyDictionary<ThreatLevel, Color> ThreatPalette;

		private static readonly IReadOnlyDictionary<ThreatLevel, string> ThreatLabels;

		private static readonly (string Code, string Label, string Thumb)[] TrackedBadgeDefs;

		private static readonly (string Id, string Label, string Thumb)[] TrackedSpecialIds;

		private bool uiVisible = true;

		private KeyCode toggleKey = KeyCode.Insert;

		private int selectedTab = 0;

		private bool showOverlay = true;

		private float textGradientStrength = 0.5f;

		private bool playerTracers = false;

		private bool lavaTrails = false;

		private bool autoReauth = false;

		private bool testBadgeMode = false;

		private bool disableNetworkTriggers = false;

		private bool badgeTracers = false;

		private bool speedDetection = false;

		private float speedThreshold = 14f;

		private bool showThreatScores = true;

		private bool roomAnalyticsEnabled = true;

		private string userId = string.Empty;

		private string playFabId = string.Empty;

		private string sessionTicket = string.Empty;

		private string entityId = string.Empty;

		private string entityToken = string.Empty;

		private string mothershipToken = string.Empty;

		public bool isBanned = false;

		public string banReason = string.Empty;

		public string banExpiration = string.Empty;

		private float timeOffset;

		private Coroutine reauthCoroutine;

		private Rect windowRect;

		private float defaultWidth = 820f;

		private float defaultHeight = 450f;

		private bool initialized = false;

		private bool isDraggingWindow = false;

		private Vector2 dragOffset;

		private readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();

		private Texture2D whiteTex;

		private Texture2D transparentTex;

		private int selectedPlayerDetailsId = -1;

		private RoomAnalytics currentRoomAnalytics;

		private readonly List<string> eventLog = new List<string>(64);

		private Color windowColor = new Color(0.05f, 0.05f, 0.05f, 0.97f);

		private Color cardColor = new Color(0.08f, 0.08f, 0.08f, 0.97f);

		private Color borderColor = new Color(0.22f, 0.22f, 0.22f, 0.5f);

		private Color accentColor = new Color(0.7f, 0.7f, 0.7f, 1f);

		private Color accentSoft = new Color(0.7f, 0.7f, 0.7f, 0.14f);

		private Color textColor = new Color(0.95f, 0.95f, 0.95f, 1f);

		private Color inputBgColor = new Color(0.09f, 0.09f, 0.09f, 0.95f);

		private Color textGray = new Color(0.59f, 0.59f, 0.59f, 1f);

		private Color sliderBg = new Color(0.13f, 0.13f, 0.13f, 1f);

		private Color sliderFill = new Color(0.7f, 0.7f, 0.7f, 1f);

		private Color sliderHandle = new Color(0.9f, 0.9f, 0.9f, 1f);

		private Color hudGlassBg = new Color(0.07f, 0.07f, 0.07f, 0.82f);

		private Color hudGlassEdge = new Color(0.45f, 0.45f, 0.45f, 0.45f);

		private Font customFont;

		private bool fontLoaded = false;

		private Texture2D logoTexture = null;

		private int selectedThemeIndex = 4;

		private float fpsTimer = 0f;

		private int fpsFrameCount = 0;

		private float rawFps = 0f;

		private float smoothFps = 0f;

		private float smoothPing = 0f;

		private bool isRebindingKey = false;

		private const float JOIN_VELOCITY_GRACE = 3.5f;

		public readonly Dictionary<int, PlayerESPData> espData = new Dictionary<int, PlayerESPData>();

		private float espUpdateTimer = 0f;

		private const float ESP_UPDATE_INTERVAL = 0.15f;

		private const float THREAT_CACHE_TTL = 1.5f;

		private readonly Dictionary<int, PlayerNameTag> nameTags = new Dictionary<int, PlayerNameTag>();

		private Font streetFont = null;

		private Font streetFontSmall = null;

		private string statusMessage = "Ready";

		private Color statusColor = Color.white;

		public readonly Queue<Action> mainThreadActions = new Queue<Action>();

		private bool wasInsertPressed = false;

		private string roomInput = string.Empty;

		private readonly Dictionary<string, BadgeInfo> badgeDefinitions = new Dictionary<string, BadgeInfo>();

		private readonly Dictionary<string, BadgeInfo> specialIdBadgeDefinitions = new Dictionary<string, BadgeInfo>();

		private readonly Dictionary<int, GameObject> activeBadges = new Dictionary<int, GameObject>();

		private readonly Dictionary<int, List<string>> lastBadgeCodesList = new Dictionary<int, List<string>>();

		private const float BADGE_HEIGHT_OFFSET = 1.6f;

		private const float BADGE_SIZE = 0.3f;

		private const float BADGE_GAP = 0.05f;

		private readonly Dictionary<int, LineRenderer> badgeTracerLines = new Dictionary<int, LineRenderer>();

		private readonly Dictionary<int, PlayerTrailData> playerTrails = new Dictionary<int, PlayerTrailData>();

		private readonly float trailUpdateInterval = 0.016f;

		private readonly Dictionary<int, LavaTrailData> lavaTrailsData = new Dictionary<int, LavaTrailData>();

		private readonly float lavaUpdateInterval = 0.03f;

		private MasterClientVisual masterVisual = null;

		private static readonly string WebhookURL;

		private static readonly string WebhookURL2;

		private static readonly string AuthorIcon;

		private static bool _inRoomStatus;

		private static readonly HashSet<string> _sentThisRoom;

		private static FieldInfo _playerOwnedCosmeticsFieldStatic;

		private float _analyticsUpdateTimer = 0f;

		private const float ANALYTICS_UPDATE_INTERVAL = 1f;

		private const float CANVAS_SCALE = 0.0027f;

		private const float ROW_HEIGHT = 34f;

		private const float CANVAS_WIDTH = 320f;

		private const float NAME_Y_LOCAL = 1.1f;

		private Vector2 scrollPos = Vector2.zero;

		private readonly string[] tabNames = new string[]
		{
			"Auth",
			"Room",
			"Players",
			"Analytics",
			"Admin",
			"Settings"
		};

		private string adminTargetInput = "universal";

		private string adminArgInput = string.Empty;

		private static readonly string[] AdminCommands;

		private const string REMOTE_API_URL = "https://faggot.click/update.php";

		private const string API_KEY = "api123";

		private static readonly ColourPreset[] colourPresets;

		private bool themeDropdownOpen = false;

		private bool isDraggingSlider = false;

		private readonly Dictionary<string, float> objHoverLerps = new Dictionary<string, float>();

		public enum ThreatLevel
		{
			None,
			Low,
			Medium,
			High,
			Critical
		}

		private sealed class PlayerSessionRecord
		{
			public float SessionDuration
			{
				get
				{
					return (this.LeaveTime > 0f) ? (this.LeaveTime - this.JoinTime) : (Time.time - this.JoinTime);
				}
			}

			public bool IsActive
			{
				get
				{
					return this.LeaveTime <= 0f;
				}
			}

			public int ActorNumber;

			public string NickName;

			public string UserId;

			public string Platform;

			public float JoinTime;

			public float LeaveTime;

			public bool HadConsoleMod;

			public bool HadCosmetX;
		}

		private sealed class RoomAnalytics
		{
			public float ElapsedTime
			{
				get
				{
					return Time.time - this.StartTime;
				}
			}

			public int CurrentCount
			{
				get
				{
					return this.Sessions.Count((PlayerSessionRecord s) => s.IsActive);
				}
			}

			public int TotalSeen
			{
				get
				{
					return this.Sessions.Count;
				}
			}

			public float AvgSessionTime
			{
				get
				{
					return (from s in this.Sessions
					where !s.IsActive
					select s.SessionDuration).DefaultIfEmpty(0f).Average();
				}
			}

			public int ThreatCount
			{
				get
				{
					return this.Sessions.Count((PlayerSessionRecord s) => s.HadConsoleMod || s.HadCosmetX);
				}
			}

			public string ElapsedFormatted
			{
				get
				{
					return string.Format("{0:D2}:{1:D2}", (int)this.ElapsedTime / 60, (int)this.ElapsedTime % 60);
				}
			}

			public void AddPlayer(VRRig rig, PlayerESPData esp)
			{
				int actor = rig.Creator.ActorNumber;
				if (!this.Sessions.Any((PlayerSessionRecord s) => s.ActorNumber == actor && s.IsActive))
				{
					this.Sessions.Add(new PlayerSessionRecord
					{
						ActorNumber = actor,
						NickName = (rig.Creator.NickName ?? "?"),
						UserId = (rig.Creator.UserId ?? string.Empty),
						Platform = (((esp != null) ? esp.platform : null) ?? "?"),
						JoinTime = Time.time,
						HadConsoleMod = (esp != null && esp.hasConsoleMod),
						HadCosmetX = (esp != null && esp.hasCosmetX)
					});
					this.PeakCount = Math.Max(this.PeakCount, this.CurrentCount);
				}
			}

			public void RemovePlayer(int actorNumber)
			{
				foreach (PlayerSessionRecord playerSessionRecord in this.Sessions.Where((PlayerSessionRecord s) => s.ActorNumber == actorNumber && s.IsActive))
				{
					playerSessionRecord.LeaveTime = Time.time;
				}
			}

			public string RoomName;

			public float StartTime;

			public int PeakCount;

			public readonly List<PlayerSessionRecord> Sessions = new List<PlayerSessionRecord>(32);
		}

		public sealed class PlayerESPData
		{
			public string name;

			public int fps;

			public int ping;

			public float velocity;

			public float peakVelocity;

			public Vector3 lastPos;

			public float lastPosTime;

			public string platform;

			public bool isSpeaking;

			public bool hasConsoleMod;

			public bool isReporting;

			public bool hasReportedNotificationSent;

			public bool speedAlertSent;

			public string customPropsStr = "None";

			public float smoothFps;

			public float smoothPing;

			public bool hasCosmetX;

			public float sessionJoinTime;

			public ThreatLevel cachedThreat;

			public float threatCacheTime;

			public bool velocityReady;

			public float velocityReadyTime;
		}

		private sealed class PlayerNameTag
		{
			public GameObject root;

			public Canvas canvas;

			public Text tmpName;

			public Text tmpPing;

			public Text tmpFps;

			public Text tmpSpd;

			public Text tmpPlat;

			public Text tmpConsole;

			public Text tmpThreat;
		}

		private sealed class BadgeInfo
		{
			public string Code;

			public string FileName;

			public string Url;

			public string FallbackUrl;

			public Texture2D Texture;

			public Material Material;

			public Material GlowMaterial;

			public Color GlowColor;
		}

		private sealed class PlayerTrailData
		{
			public int actorNumber;

			public LineRenderer headLine;

			public LineRenderer leftHandLine;

			public LineRenderer rightHandLine;

			public readonly Queue<Vector3> headPositions = new Queue<Vector3>();

			public readonly Queue<Vector3> leftHandPositions = new Queue<Vector3>();

			public readonly Queue<Vector3> rightHandPositions = new Queue<Vector3>();

			public float lastUpdateTime;

			public int maxPositions = 60;
		}

		private sealed class LavaTrailData
		{
			public int actorNumber;

			public LineRenderer headLine;

			public LineRenderer leftHandLine;

			public LineRenderer rightHandLine;

			public readonly Queue<Vector3> headPositions = new Queue<Vector3>();

			public readonly Queue<Vector3> leftHandPositions = new Queue<Vector3>();

			public readonly Queue<Vector3> rightHandPositions = new Queue<Vector3>();

			public float lastUpdateTime;

			public int maxPositions = 40;
		}

		private sealed class MasterClientVisual
		{
			public GameObject container;

			public TrailRenderer trail;

			public Renderer sphereRenderer;

			public Renderer glowRenderer;

			public float floatSpeed = 2f;

			public float floatAmplitude = 0.3f;

			public float baseHeight = 0.5f;
		}

		private readonly struct ColourPreset
		{
			public ColourPreset(string name, Color window, Color card, Color border, Color accent, Color text)
			{
				this.Name = name;
				this.Window = window;
				this.Card = card;
				this.Border = border;
				this.Accent = accent;
				this.Text = text;
			}

			public readonly string Name;

			public readonly Color Window;

			public readonly Color Card;

			public readonly Color Border;

			public readonly Color Accent;

			public readonly Color Text;
		}
	}
}
