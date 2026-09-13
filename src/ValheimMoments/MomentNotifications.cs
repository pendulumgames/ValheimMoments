using System;
using UnityEngine;

namespace ValheimMoments
{
    internal enum MomentNotificationStyle { Cinematic, ToastMinimap, ToastTopRight }
    internal enum MomentSoundMode { Off, OnCapture, OnCompletion, Both }

    internal sealed class MomentNotifications : IDisposable
    {
        private string title;
        private double shown, expires;
        private Font font;
        private Material panel;
        private readonly Vector3[] corners = new Vector3[4];
        private AudioSource audio;
        private AudioClip cue;

        internal void Show(string text, double now, bool sound, float volume)
        {
            title = text; shown = now; expires = now + 3.5;
            if (!sound || volume <= 0 || !Application.isFocused) return;
            try
            {
                if (audio == null)
                {
                    var obj = new GameObject("Valheim Moments notification audio");
                    UnityEngine.Object.DontDestroyOnLoad(obj);
                    audio = obj.AddComponent<AudioSource>();
                    audio.playOnAwake = false; audio.spatialBlend = 0;
                    // Original 160ms two-tone cue; no external assets or game audio mutation.
                    const int frequency = 22050;
                    var samples = new float[(int)(frequency * 0.16)];
                    for (int i = 0; i < samples.Length; i++)
                    {
                        double t = (double)i / frequency, envelope = Math.Sin(Math.PI * i / samples.Length);
                        samples[i] = (float)((Math.Sin(2 * Math.PI * 220 * t) + 0.3 * Math.Sin(2 * Math.PI * 330 * t)) * 0.25 * envelope * envelope);
                    }
                    cue = AudioClip.Create("Valheim Moments quiet cue", samples.Length, 1, frequency, false);
                    cue.SetData(samples, 0);
                }
                audio.PlayOneShot(cue, Mathf.Clamp01(volume));
            }
            catch { /* Optional feedback must never interrupt capture/gameplay. */ }
        }
        // Draw directly after the screenshot copy. OnGUI would bake feedback into capture.
        internal void Draw(double now, MomentNotificationStyle presentation)
        {
            if (title == null || now >= expires || !Application.isFocused) return;
            float alpha = Mathf.Clamp01((float)Math.Min((now - shown) / 0.25, (expires - now) / 0.45));
            if (alpha <= 0) return;
            if (font == null) font = Font.CreateDynamicFontFromOSFont("Georgia", 32);
            if (panel == null)
            {
                panel = new Material(Shader.Find("Hidden/Internal-Colored"));
                panel.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                panel.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                panel.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                panel.SetInt("_ZWrite", 0); panel.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            }
            float scale = Mathf.Clamp(Screen.height / 1080f, 0.7f, 1.6f);
            bool cinematic = presentation == MomentNotificationStyle.Cinematic;
            int size = Mathf.RoundToInt((cinematic ? 32 : 22) * scale);
            float width = Math.Min(Screen.width - 24, (cinematic ? 620 : 370) * scale), height = (cinematic ? 92 : 66) * scale;
            var area = cinematic ? new Rect((Screen.width - width) / 2, Screen.height * 0.19f, width, height) :
                new Rect(Screen.width - width - 24 * scale, 24 * scale, width, height);
            if (presentation == MomentNotificationStyle.ToastMinimap)
            {
                try
                {
                    var map = Minimap.instance;
                    var root = map == null ? null : HarmonyLib.AccessTools.Field(typeof(Minimap), "m_smallRoot").GetValue(map) as GameObject;
                    var image = map == null ? null : HarmonyLib.AccessTools.Field(typeof(Minimap), "m_mapImageSmall").GetValue(map) as Component;
                    var rect = image != null ? image.transform as RectTransform : root == null ? null : root.transform as RectTransform;
                    if (rect != null && root != null && root.activeInHierarchy)
                    {
                        rect.GetWorldCorners(corners);
                        float left = float.MaxValue, right = float.MinValue, bottom = float.MaxValue;
                        // The normal HUD is a screen-space overlay; transformed corners follow moved/resized minimaps.
                        foreach (var corner in corners) { left = Math.Min(left, corner.x); right = Math.Max(right, corner.x); bottom = Math.Min(bottom, corner.y); }
                        area.x = (left + right - width) / 2; area.y = Screen.height - bottom + 12 * scale;
                    }
                }
                catch { /* Replaced minimaps retain the top-right fallback. */ }
            }
            area.x = Mathf.Clamp(area.x, 12, Math.Max(12, Screen.width - width - 12));
            area.y = Mathf.Clamp(area.y, 12, Math.Max(12, Screen.height - height - 12));
            area.y -= (1 - alpha) * 14 * scale;
            string text = title;
            font.RequestCharactersInTexture(text, size, FontStyle.Bold);
            float textWidth = Measure(text, size);
            while (textWidth > width - 32 * scale && size > 12)
            { size--; font.RequestCharactersInTexture(text, size, FontStyle.Bold); textWidth = Measure(text, size); }
            var previous = RenderTexture.active;
            RenderTexture.active = null;
            GL.PushMatrix();
            try
            {
                GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0);
                panel.SetPass(0); GL.Begin(GL.QUADS);
                GL.Color(new Color(0.025f, 0.035f, 0.035f, 0.9f * alpha)); Quad(area);
                GL.Color(new Color(0.86f, 0.69f, 0.35f, alpha));
                Quad(new Rect(area.x + 12 * scale, area.y, width - 24 * scale, 2 * scale));
                Quad(new Rect(area.x + 12 * scale, area.yMax - 2 * scale, width - 24 * scale, 2 * scale));
                GL.End();
                font.material.SetPass(0); GL.Begin(GL.QUADS);
                GL.Color(new Color(1f, 0.89f, 0.65f, alpha));
                float x = area.center.x - textWidth / 2, baseline = area.center.y + size * 0.35f;
                foreach (char c in text)
                {
                    CharacterInfo glyph;
                    if (!font.GetCharacterInfo(c, out glyph, size, FontStyle.Bold)) continue;
                    Vertex(x + glyph.minX, baseline - glyph.maxY, glyph.uvTopLeft);
                    Vertex(x + glyph.maxX, baseline - glyph.maxY, glyph.uvTopRight);
                    Vertex(x + glyph.maxX, baseline - glyph.minY, glyph.uvBottomRight);
                    Vertex(x + glyph.minX, baseline - glyph.minY, glyph.uvBottomLeft);
                    x += glyph.advance;
                }
                GL.End();
            }
            finally { GL.PopMatrix(); RenderTexture.active = previous; }
        }
        private float Measure(string text, int size)
        {
            float width = 0;
            foreach (char c in text) { CharacterInfo glyph; if (font.GetCharacterInfo(c, out glyph, size, FontStyle.Bold)) width += glyph.advance; }
            return width;
        }
        private static void Vertex(float x, float y, Vector2 uv) { GL.TexCoord2(uv.x, uv.y); GL.Vertex3(x, y, 0); }
        private static void Quad(Rect r)
        { GL.Vertex3(r.xMin, r.yMin, 0); GL.Vertex3(r.xMax, r.yMin, 0); GL.Vertex3(r.xMax, r.yMax, 0); GL.Vertex3(r.xMin, r.yMax, 0); }
        internal void Clear() { title = null; if (audio != null) audio.Stop(); }
        public void Dispose()
        {
            Clear();
            if (audio != null) UnityEngine.Object.Destroy(audio.gameObject);
            if (cue != null) UnityEngine.Object.Destroy(cue);
            if (font != null) UnityEngine.Object.Destroy(font);
            if (panel != null) UnityEngine.Object.Destroy(panel);
            audio = null; cue = null; font = null; panel = null;
        }
    }
}
