using UnityEngine;

namespace SkidAuth.Data
{
	public static class AppleToggle
	{
		private static readonly Texture2D WhiteTex = new Texture2D(1, 1);
		private static readonly Texture2D GrayOnTex;
		private static readonly Texture2D GrayOffTex;

		static AppleToggle()
		{
			WhiteTex.SetPixel(0, 0, Color.white);
			WhiteTex.Apply();

			GrayOnTex = new Texture2D(1, 1);
			GrayOnTex.SetPixel(0, 0, new Color(0.5f, 0.5f, 0.5f, 1f));
			GrayOnTex.Apply();

			GrayOffTex = new Texture2D(1, 1);
			GrayOffTex.SetPixel(0, 0, new Color(0.2f, 0.2f, 0.2f, 1f));
			GrayOffTex.Apply();
		}

		public static bool Draw(Rect rect, bool value)
		{
			Event current = Event.current;
			if (current.type == EventType.MouseDown && rect.Contains(current.mousePosition))
			{
				value = !value;
				current.Use();
			}

			Color onColor  = new Color(0.5f, 0.5f, 0.5f, 1f);
			Color offColor = new Color(0.2f, 0.2f, 0.2f, 1f);

			GUI.color = value ? onColor : offColor;
			GUI.DrawTexture(rect, value ? GrayOnTex : GrayOffTex, ScaleMode.StretchToFill, false, 0f, GUI.color, 0f, rect.height / 2f);

			float knobSize = rect.height - 4f;
			float knobX    = value ? rect.x + rect.width - knobSize - 2f : rect.x + 2f;

			GUI.color = Color.white;
			GUI.DrawTexture(
				new Rect(knobX, rect.y + 2f, knobSize, knobSize),
				WhiteTex, ScaleMode.StretchToFill, false, 0f, Color.white, 0f, knobSize / 2f);
			GUI.color = Color.white;

			return value;
		}
	}
}