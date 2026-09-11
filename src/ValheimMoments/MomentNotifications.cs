using System;
using UnityEngine;

namespace ValheimMoments
{
    internal enum MomentSoundMode { Off, OnCapture, OnCompletion, Both }

    internal sealed class MomentNotifications : IDisposable
    {
        private string title;
        private double shown, expires;
        private GUIStyle style;
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
        internal void Draw(double now)
        {
            if (title == null || now >= expires || !Application.isFocused) return;
            float alpha = (float)Math.Min(1, Math.Min((now - shown) / 0.2, (expires - now) / 0.7));
            if (alpha <= 0) return;
            if (style == null) style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = true };
            float scale = Mathf.Clamp(Screen.height / 1080f, 0.7f, 1.6f);
            style.fontSize = Mathf.RoundToInt(34 * scale);
            float width = Math.Min(Screen.width - 24, 620 * scale), height = 92 * scale;
            var area = new Rect((Screen.width - width) / 2, Screen.height * 0.19f, width, height);
            Color previous = GUI.color;
            try
            {
                GUI.color = new Color(0.025f, 0.035f, 0.035f, 0.8f * alpha);
                GUI.DrawTexture(area, Texture2D.whiteTexture);
                GUI.color = new Color(0.86f, 0.69f, 0.35f, alpha);
                GUI.DrawTexture(new Rect(area.x + 24 * scale, area.y, area.width - 48 * scale, 2 * scale), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(area.x + 24 * scale, area.yMax - 2 * scale, area.width - 48 * scale, 2 * scale), Texture2D.whiteTexture);
                style.normal.textColor = new Color(1, 0.89f, 0.65f);
                GUI.Label(area, title, style);
            }
            finally { GUI.color = previous; }
        }
        internal void Clear() { title = null; if (audio != null) audio.Stop(); }
        public void Dispose()
        {
            Clear();
            if (audio != null) UnityEngine.Object.Destroy(audio.gameObject);
            if (cue != null) UnityEngine.Object.Destroy(cue);
            audio = null; cue = null;
        }
    }
}
