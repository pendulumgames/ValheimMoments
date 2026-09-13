using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using ValheimMoments.Core;

namespace ValheimMoments
{
    internal enum CinematicMovement { Orbit, RiseAndReveal, ZoomIn }
    internal sealed class CinematicCamera : IDisposable
    {
        internal const string SpawnStamp = "ValheimMoments_CinematicSpawn";
        private sealed class Shot
        {
            internal string Key; internal byte Kind; internal double Started;
            internal RaidMedia Media; internal Task<string> Worker; internal CaptureBuffer.Clip Clip;
            internal byte[] Data;
            internal string Caption;
            internal bool Letterbox;
        }
        private readonly List<Shot> shots = new List<Shot>();
        private readonly string root, helper;
        private readonly Action<string> log;
        private Camera camera;
        private RenderTexture target;
        private CaptureBuffer buffer;
        private Shot active;
        private Transform subject;
        private Vector3 focus, direction;
        private float radius;
        private float distanceScale, panDegrees, focusHeight;
        private bool rise, zoomIn, flipImage;
        private double nextFrame, submitted;
        private AsyncGPUReadbackRequest request;
        private bool reading, disposed;
        private int quality;
        internal bool Busy { get { return active != null; } }
        internal CinematicCamera(string root, string helper, Action<string> log)
        { this.root = root; this.helper = helper; this.log = log; }

        internal static string Subject(Character character)
        { var zdo = character?.GetComponent<ZNetView>()?.GetZDO(); return zdo == null ? null : zdo.m_uid.ToString(); }

        // Prefix observes the native creation/loading distinction before Awake consumes it.
        private static void BeforeView(out bool __state)
        { __state = AccessTools.Field(typeof(ZNetView), "m_initZDO")?.GetValue(null) == null; }
        private static void AfterView(ZNetView __instance, bool __state)
        {
            try
            {
                var c = __instance.GetComponent<Character>();
                if (__state && c != null && !(c is Player) && __instance.IsOwner() && ZNet.instance != null)
                    __instance.GetZDO()?.Set(SpawnStamp, ZNet.instance.GetTime().Ticks);
            }
            catch { }
        }
        internal static void Install(Harmony harmony)
        { harmony.Patch(AccessTools.Method(typeof(ZNetView), "Awake"), new HarmonyMethod(typeof(CinematicCamera), nameof(BeforeView)), new HarmonyMethod(typeof(CinematicCamera), nameof(AfterView))); }

        internal bool Start(string key, byte kind, Vector3 center, Transform follow, float size, double now, int q,
            float distanceMultiplier = 2, float sweepDegrees = 70, CinematicMovement movement = CinematicMovement.Orbit,
            string caption = null, bool letterbox = true, bool flip = true)
        {
            if (disposed || active != null || shots.Exists(s => s.Worker != null) || Camera.main == null || string.IsNullOrEmpty(key) ||
                shots.Exists(s => s.Key == key && s.Kind == kind) || Vector3.Distance(Camera.main.transform.position, center) > 100) return false;
            Tick(now);
            if (shots.Count >= 4) return false;
            try
            {
                var go = new GameObject("Valheim Moments cinematic camera") { hideFlags = HideFlags.HideAndDontSave };
                camera = go.AddComponent<Camera>(); camera.CopyFrom(Camera.main); camera.enabled = false;
                camera.tag = "Untagged"; camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.fieldOfView = 55; camera.nearClipPlane = 0.15f; camera.farClipPlane = Mathf.Min(camera.farClipPlane, 250);
                camera.cullingMask &= ~(1 << 5); // No screen-space or world-space UI.
                target = new RenderTexture(640, 360, 24, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };
                target.Create(); camera.targetTexture = target; camera.aspect = 640f / 360;
                buffer = new CaptureBuffer(640, 360, 10, 0.1, 4, 48L * 1048576);
                buffer.TryTriggerSegment(now, 4);
                active = new Shot { Key = key, Kind = kind, Started = now, Media = new RaidMedia(root), Caption = caption ?? CinematicPacket.Label(kind), Letterbox = letterbox };
                shots.Add(active); subject = follow; focus = center; quality = q;
                radius = Mathf.Clamp(size * 2.8f, 7, 24);
                focusHeight = radius / 5.6f;
                distanceScale = Mathf.Clamp(distanceMultiplier, 1, 3); panDegrees = Mathf.Clamp(sweepDegrees, 0, 180);
                rise = movement == CinematicMovement.RiseAndReveal; zoomIn = movement == CinematicMovement.ZoomIn; flipImage = flip;
                direction = Camera.main.transform.position - center; direction.y = 0;
                if (direction.sqrMagnitude < 0.1f) direction = Vector3.back;
                direction.Normalize(); nextFrame = now;
                return true;
            }
            catch (Exception e) { log("Camera setup skipped: " + e.GetType().Name); CancelActive(); return false; }
        }
        internal void Render(double now)
        {
            if (active == null || disposed) return;
            try
            {
                Drain();
                if (now >= active.Started + 4)
                {
                    if (reading) return;
                    var clip = buffer.TryComplete(now);
                    if (clip == null || clip.Count < 10) { clip?.Release(); CancelActive(); return; }
                    var shot = active; shot.Clip = clip; int q = quality; bool flip = flipImage;
                    shot.Worker = shot.Media.Start(token => {
                        string result = EncoderClient.Encode(clip, helper, shot.Media.Opening, 640, 360, q, flip, token);
                        EncoderClient.Compose(helper, shot.Media.Opening, null, shot.Media.Combined, q, token, shot.Caption, true, shot.Letterbox);
                        if (new FileInfo(shot.Media.Combined).Length <= 4 * 1048576) shot.Data = File.ReadAllBytes(shot.Media.Combined);
                        return result;
                    });
                    active = null; ReleaseCamera(); buffer = null; return;
                }
                if (reading || now < nextFrame) return;
                nextFrame = now + 0.1;
                float t = Mathf.SmoothStep(0, 1, (float)((now - active.Started) / 4));
                if (subject != null) focus = Vector3.Lerp(focus, subject.position + Vector3.up * focusHeight, 0.15f);
                float distance = (rise ? Mathf.Lerp(7, 15, t) : radius * Mathf.Lerp(1, 0.88f, t)) * distanceScale;
                float height = (rise ? Mathf.Lerp(3, 15, t) : radius * 0.35f) * distanceScale;
                // ZoomIn is a straight approach from 2x to 1x framing, with a fixed lens.
                if (zoomIn)
                {
                    float approach = Mathf.Lerp(2, 1, t);
                    distance = radius * approach;
                    height = radius * 0.35f * approach;
                }
                float angle = zoomIn ? 0 : Mathf.Lerp(-panDegrees / 2, panDegrees / 2, t);
                Vector3 offset = Quaternion.AngleAxis(angle, Vector3.up) * direction * distance + Vector3.up * height;
                offset = Vector3.ClampMagnitude(offset, 80); // Keep the experiment near loaded surroundings.
                var desired = focus + offset;
                RaycastHit hit;
                int mask = LayerMask.GetMask("Default", "static_solid", "terrain", "piece", "Default_small", "small_solid");
                // Check a corridor rather than a single ray, then try higher viewpoints above vegetation.
                if (Blocked(focus, desired, mask, out hit))
                {
                    for (int step = 1; step <= 3; step++)
                    {
                        var elevated = focus + Vector3.ClampMagnitude(offset + Vector3.up * (step * 6), 80);
                        RaycastHit elevatedHit;
                        if (!Blocked(focus, elevated, mask, out elevatedHit)) { desired = elevated; break; }
                    }
                }
                if (Blocked(focus, desired, mask, out hit))
                {
                    if (hit.distance < 2) { CancelActive(); return; }
                    desired = hit.point + (focus - hit.point).normalized * 0.5f;
                }
                camera.transform.SetPositionAndRotation(desired, Quaternion.LookRotation(focus - desired, Vector3.up));
                var previous = RenderTexture.active;
                try { camera.Render(); } finally { RenderTexture.active = previous; }
                submitted = now; request = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32); reading = true;
            }
            catch (Exception e) { log("Camera shot skipped: " + e.GetType().Name); CancelActive(); }
        }
        private static bool Blocked(Vector3 center, Vector3 destination, int mask, out RaycastHit hit)
        {
            Vector3 delta = destination - center;
            return Physics.SphereCast(center, 0.6f, delta.normalized, out hit, delta.magnitude, mask, QueryTriggerInteraction.Ignore);
        }
        private void Drain()
        {
            if (!reading || !request.done) return;
            reading = false;
            if (request.hasError) throw new IOException("Camera readback");
            var pixels = request.GetData<byte>(); var data = new byte[pixels.Length]; pixels.CopyTo(data);
            buffer.AddFrame(data, submitted);
        }
        internal void Tick(double now)
        {
            foreach (var shot in new List<Shot>(shots))
            {
                if (shot.Worker != null && shot.Worker.IsCompleted)
                {
                    if (shot.Worker.IsFaulted || shot.Worker.IsCanceled) shot.Data = null;
                    var ignored = shot.Worker.Exception;
                    shot.Clip?.Release(); shot.Clip = null; shot.Worker = null; shot.Media.Dispose();
                    if (shot.Data == null) shots.Remove(shot);
                }
                if (shot != active && shot.Worker == null && now - shot.Started > 1800) shots.Remove(shot);
            }
        }
        internal List<CinematicPacket.Shot> Take(string key)
        {
            var result = new List<CinematicPacket.Shot>();
            foreach (var shot in new List<Shot>(shots))
                if (shot.Key == key && shot.Worker == null && shot.Data != null)
                { result.Add(new CinematicPacket.Shot { Kind = shot.Kind, Pixels = shot.Data }); shots.Remove(shot); }
            result.Sort((a, b) => a.Kind.CompareTo(b.Kind)); return result;
        }
        internal bool Pending(string key) { return shots.Exists(s => s.Key == key && (s == active || s.Worker != null)); }
        private void ReleaseCamera()
        {
            // A cancelled request must finish before releasing its GPU resource.
            if (reading) { request.WaitForCompletion(); reading = false; }
            if (camera != null) UnityEngine.Object.Destroy(camera.gameObject); camera = null;
            if (target != null) { target.Release(); UnityEngine.Object.Destroy(target); } target = null;
            subject = null;
        }
        private void CancelActive()
        {
            if (active != null) { active.Media.Dispose(); shots.Remove(active); active = null; }
            ReleaseCamera(); buffer = null;
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true; CancelActive();
            // Main-thread clip ownership requires completion before releasing pools.
            foreach (var shot in shots) { shot.Media.Dispose(); if (shot.Worker != null) { try { shot.Worker.GetAwaiter().GetResult(); } catch { } } shot.Clip?.Release(); }
            shots.Clear();
        }
    }
}
