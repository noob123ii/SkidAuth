using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SkidAuth.Notifications
{
	[BepInPlugin("com.skidauth.notifications", "SkidAuth Notifications", "2.2.1")]
	public class NotifiLib : BaseUnityPlugin
	{
		public static bool IsEnabled = true;
		public static bool Disablenotifcations = false;

		private static NotifiLib instance;
		private GameObject HUDParent;
		private GameObject HUDCanvas;
		private Camera mainCamera;
		private bool hasInit;

		private static Texture2D texBg;
		private static Texture2D texBar;
		private static Texture2D texSep;
		private static Texture2D texProgressBg;
		private static Texture2D texProgressFill;
		private static Texture2D texGlow;

		private static GUIStyle stTitle;
		private static GUIStyle stReason;
		private static GUIStyle stMessage;

		private static readonly Color colBg     = new Color(0.07f, 0.07f, 0.09f, 0.97f);
		private static readonly Color colWhite  = new Color(1f, 1f, 1f, 1f);
		private static readonly Color colSubtle = new Color(0.6f, 0.6f, 0.65f, 1f);

		private static readonly List<NotifVR> vrQueue     = new List<NotifVR>();
		private static readonly List<NotifScreen> screenQueue = new List<NotifScreen>();

		private const float W          = 360f;
		private const float H          = 82f;
		private const float BAR_W      = 4f;
		private const float PAD_L      = 14f;
		private const float PAD_R      = 12f;
		private const float TITLE_H    = 28f;
		private const float SEP_H      = 2f;
		private const float SEP_MARGIN = 4f;
		private const float VR_SPACING = 90f;
		private const int   MAX_VR     = 3;
		private const int   MAX_SCREEN = 5;
		private const float INNER_X    = 18f;
		private const float INNER_W    = 330f;
		private const float SEP_Y      = 32f;
		private const float MSG_Y      = 38f;
		private const float MSG_H      = 34f;

		private static readonly Dictionary<NotifiReason, ReasonTheme> Themes =
			new Dictionary<NotifiReason, ReasonTheme>
			{
				{ NotifiReason.Error,              new ReasonTheme(new Color(1f, 0.3f, 0.3f, 1f), "ERROR") },
				{ NotifiReason.Success,            new ReasonTheme(new Color(0.25f, 0.88f, 0.52f, 1f), "SUCCESS") },
				{ NotifiReason.Warning,            new ReasonTheme(new Color(1f, 0.72f, 0.2f, 1f), "WARNING") },
				{ NotifiReason.RoomJoined,         new ReasonTheme(new Color(0.25f, 0.62f, 1f, 1f), "JOINED") },
				{ NotifiReason.RoomLeft,           new ReasonTheme(new Color(1f, 0.52f, 0.25f, 1f), "LEFT") },
				{ NotifiReason.Info,               new ReasonTheme(new Color(0.75f, 0.75f, 0.8f, 1f), "INFO") },
				{ NotifiReason.Button,             new ReasonTheme(new Color(0.78f, 0.52f, 1f, 1f), "ACTION") },
				{ NotifiReason.MasterClientChange, new ReasonTheme(new Color(1f, 0.84f, 0.25f, 1f), "MASTER") },
			};

		private struct ReasonTheme
		{
			public Color Accent;
			public string Tag;
			public ReasonTheme(Color accent, string tag) { Accent = accent; Tag = tag; }
		}

		private static ReasonTheme GetTheme(NotifiReason r) =>
			Themes.TryGetValue(r, out ReasonTheme theme) ? theme : Themes[NotifiReason.Info];

		private void Awake() => instance = this;

		private void Update()
		{
			if (!hasInit && Camera.main != null && GorillaTagger.Instance != null)
			{
				Boot();
			}

			if (!hasInit) return;

			for (int i = vrQueue.Count - 1; i >= 0; i--)
			{
				if (vrQueue[i] != null)
					vrQueue[i].Tick();
				else
					vrQueue.RemoveAt(i);
			}

			for (int j = screenQueue.Count - 1; j >= 0; j--)
			{
				screenQueue[j].Tick();
				if (screenQueue[j].done)
				{
					screenQueue.RemoveAt(j);
					RebuildScreenPositions();
				}
			}
		}

		private void OnGUI()
		{
			if (screenQueue.Count == 0) return;

			EnsureStyles();
			for (int i = screenQueue.Count - 1; i >= 0; i--)
			{
				NotifScreen n = screenQueue[i];
				int index    = screenQueue.Count - 1 - i;
				float target = Screen.height - 14f - 82f - index * 90f;
				n.y = Mathf.Lerp(n.y, target, 1f - Mathf.Exp(-10f * Time.deltaTime));
				DrawScreen(n, 14f, n.y);
			}
		}

		private void OnDestroy()
		{
			ClearAllNotifications();
			Tex.Kill(ref texBg);
			Tex.Kill(ref texBar);
			Tex.Kill(ref texSep);
			Tex.Kill(ref texProgressBg);
			Tex.Kill(ref texProgressFill);
			Tex.Kill(ref texGlow);
		}

		private void Boot()
		{
			mainCamera = Camera.main;
			if (mainCamera == null) return;

			BuildTextures();
			BuildVRCanvas();
			hasInit = true;
		}

		private static void BuildTextures()
		{
			int w = Mathf.RoundToInt(330f);
			int h = Mathf.RoundToInt(82f);
			texBg            = Tex.RoundRect(Mathf.RoundToInt(360f), h, colBg, 6);
			texBar           = Tex.Solid(Mathf.RoundToInt(4f), h, colWhite);
			texSep           = Tex.Solid(Mathf.RoundToInt(360f), Mathf.RoundToInt(2f), colWhite);
			texProgressBg    = Tex.RoundRect(w, 3, new Color(0.2f, 0.2f, 0.24f, 1f), 1);
			texProgressFill  = Tex.RoundRect(w, 3, colWhite, 1);
			texGlow          = Tex.Glow(Mathf.RoundToInt(360f) + 12, h + 12, colWhite);
		}

		private void BuildVRCanvas()
		{
			HUDParent = new GameObject("NOTIFLIB_VR_PARENT");
			HUDParent.transform.SetParent(mainCamera.transform, false);
			HUDParent.transform.localPosition = new Vector3(-0.12f, -0.02f, 0.5f);
			HUDParent.transform.localRotation = Quaternion.Euler(0f, -18f, 0f);

			HUDCanvas = new GameObject("NOTIFLIB_CANVAS");
			HUDCanvas.transform.SetParent(HUDParent.transform, false);
			Canvas canvas = HUDCanvas.AddComponent<Canvas>();
			canvas.renderMode   = RenderMode.WorldSpace;
			canvas.worldCamera  = mainCamera;
			HUDCanvas.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
			HUDCanvas.AddComponent<GraphicRaycaster>();
			RectTransform rt = HUDCanvas.GetComponent<RectTransform>();
			rt.sizeDelta      = new Vector2(500f, 600f);
			rt.localScale     = Vector3.one * 0.001f;
			rt.localPosition  = Vector3.zero;
		}

		private static void DrawScreen(NotifScreen n, float x, float y)
		{
			if (n.scale < 0.01f) return;

			float scale = n.scale;
			float alpha = n.alpha;
			float scaledW = W * scale;
			float scaledH = H * scale;
			float offsetX = x + (W - scaledW) * 0.5f;
			float offsetY = y + (H - scaledH);
			Color savedColor = GUI.color;
			ReasonTheme theme = GetTheme(n.reason);

			if (!n.fadingOut)
			{
				float pulseAlpha = (0.1f + Mathf.Sin(Time.time * 2.2f) * 0.05f) * alpha;
				GUI.color = new Color(1f, 1f, 1f, pulseAlpha);
				GUI.DrawTexture(new Rect(offsetX - 6f * scale, offsetY - 6f * scale, 372f * scale, 94f * scale), texGlow);
			}

			GUI.color = new Color(1f, 1f, 1f, alpha);
			GUI.DrawTexture(new Rect(offsetX, offsetY, scaledW, scaledH), texBg);
			GUI.DrawTexture(new Rect(offsetX, offsetY, BAR_W * scale, scaledH), texBar);

			float innerX = offsetX + INNER_X * scale;
			float innerW = INNER_W * scale;
			float titleY = offsetY + SEP_MARGIN * scale;

			GUI.color = new Color(1f, 1f, 1f, alpha);
			GUI.Label(new Rect(innerX, titleY, innerW * 0.62f, TITLE_H), n.title, stTitle);

			GUI.color = new Color(theme.Accent.r, theme.Accent.g, theme.Accent.b, alpha);
			GUI.Label(new Rect(innerX + innerW * 0.38f, titleY, innerW * 0.62f, TITLE_H), theme.Tag, stReason);

			GUI.color = new Color(1f, 1f, 1f, alpha * 0.85f);
			GUI.DrawTexture(new Rect(offsetX, offsetY + SEP_Y * scale, scaledW, SEP_H * scale), texSep);

			GUI.color = new Color(colSubtle.r, colSubtle.g, colSubtle.b, alpha);
			GUI.Label(new Rect(innerX, offsetY + MSG_Y * scale, innerW, MSG_H * scale), n.message, stMessage);

			float progressY = offsetY + 75f * scale;
			GUI.color = new Color(1f, 1f, 1f, alpha * 0.1f);
			GUI.DrawTexture(new Rect(innerX, progressY, innerW, 3f * scale), texProgressBg);

			float remaining = 1f - Mathf.Clamp01((Time.time - n.startTime) / n.duration);
			if (remaining > 0f)
			{
				GUI.color = new Color(theme.Accent.r, theme.Accent.g, theme.Accent.b, alpha * 0.9f);
				GUI.DrawTexture(new Rect(innerX, progressY, innerW * remaining, 3f * scale), texProgressFill);
			}

			GUI.color = savedColor;
		}

		private static void EnsureStyles()
		{
			if (stTitle == null)
				stTitle = new GUIStyle(GUI.skin.label)
				{
					fontSize   = 14,
					fontStyle  = FontStyle.Bold,
					normal     = { textColor = colWhite },
					alignment  = TextAnchor.MiddleLeft,
					wordWrap   = false,
					richText   = false
				};

			if (stReason == null)
				stReason = new GUIStyle(GUI.skin.label)
				{
					fontSize   = 11,
					fontStyle  = FontStyle.Bold,
					normal     = { textColor = colWhite },
					alignment  = TextAnchor.MiddleRight,
					wordWrap   = false,
					richText   = false
				};

			if (stMessage == null)
				stMessage = new GUIStyle(GUI.skin.label)
				{
					fontSize   = 11,
					fontStyle  = FontStyle.Normal,
					normal     = { textColor = colSubtle },
					alignment  = TextAnchor.UpperLeft,
					wordWrap   = true,
					richText   = false
				};
		}

		public static void SendNotification(string message, NotifiReason reason = NotifiReason.Info) =>
			SendNotification(message, message, 5f, reason);

		public static void SendNotification(string title, string message, float duration = 5f, NotifiReason reason = NotifiReason.Info)
		{
			if (Disablenotifcations || !IsEnabled || instance == null || !instance.hasInit)
				return;

			try
			{
				vrQueue.RemoveAll(n => n == null || n.root == null);
				while (vrQueue.Count >= MAX_VR)
				{
					instance.StartCoroutine(vrQueue[0].FadeOut(true));
					vrQueue.RemoveAt(0);
				}

				NotifVR notif = new NotifVR(title, message, duration, reason);
				vrQueue.Add(notif);
				instance.StartCoroutine(notif.Show());
				instance.StartCoroutine(notif.LifeCycle());
				RebuildVRPositions();

				while (screenQueue.Count >= MAX_SCREEN)
					screenQueue.RemoveAt(0);
				screenQueue.Add(new NotifScreen(title, message, duration, reason));
				RebuildScreenPositions();
			}
			catch (Exception ex)
			{
				Debug.LogError($"[NotifiLib] {ex}");
			}
		}

		public static void ClearAllNotifications()
		{
			if (instance == null) return;

			foreach (NotifVR n in vrQueue)
				if (n != null) n.Kill();

			vrQueue.Clear();
			screenQueue.Clear();
		}

		public static void ClearPastNotifications(int amount)
		{
			if (instance == null) return;

			amount = Mathf.Min(amount, vrQueue.Count);
			for (int i = 0; i < amount; i++)
				if (vrQueue[i] != null) vrQueue[i].Kill();

			vrQueue.RemoveRange(0, amount);
			RebuildVRPositions();
			screenQueue.RemoveRange(0, Mathf.Min(amount, screenQueue.Count));
		}

		private static void RebuildVRPositions()
		{
			for (int i = 0; i < vrQueue.Count; i++)
				if (vrQueue[i] != null)
					vrQueue[i].targetY = (vrQueue.Count - 1 - i) * VR_SPACING;
		}

		private static void RebuildScreenPositions()
		{
			float top = Screen.height - 14f - 82f;
			for (int i = screenQueue.Count - 1; i >= 0; i--)
				screenQueue[i].targetY = top - (screenQueue.Count - 1 - i) * VR_SPACING;
		}


		private class NotifScreen
		{
			public string title;
			public string message;
			public float duration;
			public float startTime;
			public NotifiReason reason;
			public float alpha;
			public float scale;
			public float y;
			public float targetY;
			public bool done;
			public bool fadingOut;
			private Phase phase = Phase.In;

			private enum Phase { In, Hold, Out }

			public NotifScreen(string title, string message, float duration, NotifiReason reason)
			{
				this.title     = title;
				this.message   = message;
				this.duration  = duration;
				this.reason    = reason;
				this.startTime = Time.time;
				this.y         = Screen.height - 14f - 82f;
			}

			public void Tick()
			{
				float elapsed = Time.time - startTime;

				switch (phase)
				{
					case Phase.In:
						alpha = Mathf.MoveTowards(alpha, 1f, Time.deltaTime * 8f);
						scale = Mathf.MoveTowards(scale, 1f, Time.deltaTime * 10f);
						if (alpha >= 0.99f)
						{
							alpha = 1f;
							scale = 1f;
							phase = Phase.Hold;
						}
						break;

					case Phase.Hold:
						if (elapsed >= duration - 0.6f)
						{
							phase     = Phase.Out;
							fadingOut = true;
						}
						break;

					case Phase.Out:
						alpha = Mathf.MoveTowards(alpha, 0f, Time.deltaTime * 3f);
						scale = Mathf.MoveTowards(scale, 0f, Time.deltaTime * 3.5f);
						if (alpha <= 0.01f)
							done = true;
						break;
				}
			}
		}


		private class NotifVR
		{
			public GameObject root;
			public float targetY;

			private float startTime;
			private float duration;
			private NotifiReason reason;
			private RectTransform rt;
			private CanvasGroup canvasGroup;
			private Image imgGlow;
			private Image imgBg;
			private Image imgBar;
			private Image imgSep;
			private Image imgProgressBg;
			private Image imgProgressFill;
			private TextMeshProUGUI txtTitle;
			private TextMeshProUGUI txtReason;
			private TextMeshProUGUI txtMessage;
			private Phase phase = Phase.In;

			private enum Phase { In, Hold, Out, Dead }

			public NotifVR(string title, string message, float duration, NotifiReason reason)
			{
				this.duration = duration;
				this.reason   = reason;
				this.startTime = Time.time;
				Build(title, message, reason);
			}

			private void Build(string title, string message, NotifiReason reason)
			{
				ReasonTheme theme = GetTheme(reason);

				root = new GameObject("NotifVR");
				root.transform.SetParent(instance.HUDCanvas.transform, false);
				canvasGroup = root.AddComponent<CanvasGroup>();
				rt = root.AddComponent<RectTransform>();
				rt.sizeDelta        = new Vector2(W, H);
				rt.anchoredPosition = Vector2.zero;
				rt.localScale       = Vector3.zero;

				imgGlow = MakeImg("Glow", root, texGlow, new Vector2(372f, 94f), Vector2.zero);
				imgGlow.color = new Color(1f, 1f, 1f, 0f);

				imgBg = MakeImg("Bg", root, texBg, new Vector2(W, H), Vector2.zero);

				imgBar = MakeImg("Bar", root, texBar, new Vector2(BAR_W, H), new Vector2(-178f, 0f));
				imgBar.color = colWhite;

				imgSep = MakeImg("Sep", root, texSep, new Vector2(W, 2f), Vector2.zero);
				imgSep.color = new Color(1f, 1f, 1f, 0.85f);
				SetAnchors(imgSep, new Vector2(0f, 1f), new Vector2(0f, 1f),
					new Vector2(0f, 1f), new Vector2(0f, -32f), new Vector2(W, 2f));

				txtTitle = MakeTmp("Title", root);
				txtTitle.text           = title;
				txtTitle.fontSize       = 15f;
				txtTitle.fontStyle      = FontStyles.Bold;
				txtTitle.color          = colWhite;
				txtTitle.alignment      = TextAlignmentOptions.TopJustified;
				txtTitle.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
				SetAnchors(txtTitle, Vector2.zero, Vector2.zero,
					new Vector2(0f, 1f), new Vector2(18f, -4f), new Vector2(204.6f, TITLE_H));

				txtReason = MakeTmp("Reason", root);
				txtReason.text           = theme.Tag;
				txtReason.fontSize       = 11f;
				txtReason.fontStyle      = FontStyles.Bold;
				txtReason.color          = theme.Accent;
				txtReason.alignment      = TextAlignmentOptions.TopRight;
				txtReason.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
				SetAnchors(txtReason, Vector2.zero, Vector2.zero,
					new Vector2(0f, 1f), new Vector2(143.4f, -4f), new Vector2(204.6f, TITLE_H));

				txtMessage = MakeTmp("Msg", root);
				txtMessage.text           = message;
				txtMessage.fontSize       = 12f;
				txtMessage.fontStyle      = FontStyles.Normal;
				txtMessage.color          = colSubtle;
				txtMessage.alignment      = TextAlignmentOptions.TopLeft;
				txtMessage.textWrappingMode = TMPro.TextWrappingModes.Normal;
				SetAnchors(txtMessage, Vector2.zero, Vector2.zero,
					new Vector2(0f, 1f), new Vector2(18f, -38f), new Vector2(INNER_W, MSG_H));

				imgProgressBg = MakeImg("PBg", root, texProgressBg, new Vector2(INNER_W, 3f), Vector2.zero);
				imgProgressBg.color = new Color(1f, 1f, 1f, 0.12f);
				SetAnchors(imgProgressBg, Vector2.zero, Vector2.zero,
					new Vector2(0f, 0f), new Vector2(18f, 5f), new Vector2(INNER_W, 3f));

				imgProgressFill = MakeImg("PFill", root, texProgressFill, new Vector2(INNER_W, 3f), Vector2.zero);
				imgProgressFill.type       = Image.Type.Filled;
				imgProgressFill.fillMethod = 0;
				imgProgressFill.fillAmount = 1f;
				imgProgressFill.color      = theme.Accent;
				SetAnchors(imgProgressFill, Vector2.zero, Vector2.zero,
					new Vector2(0f, 0f), new Vector2(18f, 5f), new Vector2(INNER_W, 3f));
			}

			public IEnumerator Show()
			{
				if (root == null) yield break;
				root.SetActive(true);
				rt.anchoredPosition = new Vector2(0f, -100f);

				float elapsed = 0f;
				while (elapsed < 0.3f)
				{
					elapsed += Time.deltaTime;
					float t = elapsed / 0.3f;
					rt.anchoredPosition = Vector2.Lerp(new Vector2(0f, -100f), new Vector2(0f, targetY), t);
					yield return null;
				}
				rt.anchoredPosition = new Vector2(0f, targetY);
			}

			public IEnumerator LifeCycle()
			{
				yield return Show();
				yield return new WaitForSeconds(duration);
				yield return FadeOut(false);
				if (root != null)
					UnityEngine.Object.Destroy(root);
			}

			public IEnumerator FadeOut(bool fast)
			{
				if (canvasGroup == null) yield break;

				float fadeDuration = fast ? 0.1f : 0.3f;
				float elapsed = 0f;
				while (elapsed < fadeDuration)
				{
					elapsed += Time.deltaTime;
					canvasGroup.alpha = 1f - elapsed / fadeDuration;
					yield return null;
				}
				canvasGroup.alpha = 0f;
			}

			public void Tick()
			{
				if (root == null || rt == null) return;

				Vector2 pos = rt.anchoredPosition;
				Vector2 target = new Vector2(0f, targetY);
				if (Vector2.Distance(pos, target) > 0.5f)
					rt.anchoredPosition = Vector2.Lerp(pos, target, 1f - Mathf.Exp(-10f * Time.deltaTime));

				if (phase == Phase.Hold && imgProgressFill != null)
					imgProgressFill.fillAmount = Mathf.Clamp01(1f - (Time.time - startTime) / duration);

				if (phase == Phase.Hold && imgGlow != null)
				{
					float pulse = 0.08f + Mathf.Sin(Time.time * 2.2f) * 0.04f;
					imgGlow.color = new Color(1f, 1f, 1f, pulse);
				}
			}

			public void Kill()
			{
				if (root == null) return;
				UnityEngine.Object.Destroy(root);
				root = null;
			}

			private void SetAlpha(float a)
			{
				if (root == null) return;

				ReasonTheme theme = GetTheme(reason);
				SetImageColor(imgBg,            1f, 1f, 1f, a);
				SetImageColor(imgBar,           1f, 1f, 1f, a);
				SetImageColor(imgSep,           1f, 1f, 1f, a * 0.85f);
				SetImageColor(imgProgressBg,    1f, 1f, 1f, a * 0.12f);
				SetImageColor(imgProgressFill,  theme.Accent.r, theme.Accent.g, theme.Accent.b, a);
				SetTextColor(txtTitle,   1f, 1f, 1f, a);
				SetTextColor(txtReason,  theme.Accent.r, theme.Accent.g, theme.Accent.b, a);
				SetTextColor(txtMessage, colSubtle.r, colSubtle.g, colSubtle.b, a * 0.9f);
			}

			private static void SetImageColor(Image img, float r, float g, float b, float a)
			{
				if (img != null) img.color = new Color(r, g, b, a);
			}

			private static void SetTextColor(TextMeshProUGUI tmp, float r, float g, float b, float a)
			{
				if (tmp != null) tmp.color = new Color(r, g, b, a);
			}

			private static float EaseOutBack(float t) =>
				1f + 2.70158f * Mathf.Pow(t - 1f, 3f) + 1.70158f * Mathf.Pow(t - 1f, 2f);

			private static Image MakeImg(string name, GameObject parent, Texture2D tex, Vector2 size, Vector2 pos)
			{
				GameObject go = new GameObject(name);
				go.transform.SetParent(parent.transform, false);
				Image img = go.AddComponent<Image>();
				img.sprite        = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
				img.raycastTarget = false;
				RectTransform rt  = go.GetComponent<RectTransform>();
				rt.sizeDelta      = size;
				rt.anchoredPosition = pos;
				return img;
			}

			private static TextMeshProUGUI MakeTmp(string name, GameObject parent)
			{
				GameObject go = new GameObject(name);
				go.transform.SetParent(parent.transform, false);
				Canvas c   = go.AddComponent<Canvas>();
				c.overrideSorting = true;
				c.sortingOrder    = 10;
				TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
				tmp.raycastTarget = false;
				return tmp;
			}

			private static void SetAnchors(Component comp, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size)
			{
				RectTransform rt = comp.GetComponent<RectTransform>();
				rt.anchorMin          = aMin;
				rt.anchorMax          = aMax;
				rt.pivot              = pivot;
				rt.anchoredPosition   = pos;
				rt.sizeDelta          = size;
			}
		}


		private static class Tex
		{
			public static void Kill(ref Texture2D t)
			{
				if (t == null) return;
				UnityEngine.Object.Destroy(t);
				t = null;
			}

			public static Texture2D Solid(int w, int h, Color c)
			{
				Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
				tex.filterMode = FilterMode.Bilinear;
				tex.wrapMode   = TextureWrapMode.Repeat;
				Color[] pixels = new Color[w * h];
				for (int i = 0; i < pixels.Length; i++)
					pixels[i] = c;
				tex.SetPixels(pixels);
				tex.Apply(false, true);
				return tex;
			}

			public static Texture2D RoundRect(int w, int h, Color color, int radius)
			{
				Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
				tex.filterMode = FilterMode.Bilinear;
				tex.wrapMode   = TextureWrapMode.Repeat;
				Color[] pixels = new Color[w * h];
				float rSq = radius * radius;

				for (int y = 0; y < h; y++)
				{
					for (int x = 0; x < w; x++)
					{
						bool clipped = false;

						if (x < radius && y < radius)
						{
							float dx = x - radius;
							float dy = y - radius;
							clipped = dx * dx + dy * dy > rSq;
						}
						else if (x > w - radius - 1 && y < radius)
						{
							float dx = x - (w - radius - 1);
							float dy = y - radius;
							clipped = dx * dx + dy * dy > rSq;
						}
						else if (x < radius && y > h - radius - 1)
						{
							float dx = x - radius;
							float dy = y - (h - radius - 1);
							clipped = dx * dx + dy * dy > rSq;
						}
						else if (x > w - radius - 1 && y > h - radius - 1)
						{
							float dx = x - (w - radius - 1);
							float dy = y - (h - radius - 1);
							clipped = dx * dx + dy * dy > rSq;
						}

						pixels[x + y * w] = clipped ? Color.clear : color;
					}
				}

				tex.SetPixels(pixels);
				tex.Apply(false, true);
				return tex;
			}

			public static Texture2D Glow(int w, int h, Color color)
			{
				Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
				tex.filterMode = FilterMode.Bilinear;
				tex.wrapMode   = TextureWrapMode.Repeat;
				Color[] pixels = new Color[w * h];
				Vector2 center = new Vector2(w * 0.5f, h * 0.5f);
				float maxDist  = Mathf.Sqrt(center.x * center.x + center.y * center.y);

				for (int y = 0; y < h; y++)
				{
					for (int x = 0; x < w; x++)
					{
						float dist = Vector2.Distance(new Vector2(x, y), center);
						float t    = 1f - Mathf.Clamp01(dist / maxDist);
						t = t * t * 0.5f;
						pixels[x + y * w] = new Color(color.r, color.g, color.b, t);
					}
				}

				tex.SetPixels(pixels);
				tex.Apply(false, true);
				return tex;
			}
		}
	}
}
