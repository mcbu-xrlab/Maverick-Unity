// Sets SceneWorld.gaze to the SceneItem the user points at, so "this" and "that" resolve: the object under the mouse
// pointer (flat screen), at the centre of the camera (head gaze in XR), or along a ray (a controller). The objects need
// colliders. While the agent is answering, the gaze is left as it was, so the command sees what the user pointed at when
// they asked.
// V1 (schema 1.1): a target held for dwellSeconds is reported to the attention ledger as this person's "gaze" or
// "point"; looking away for as long releases it. One component per person and act; remote people are fed the same way
// by your networking code (SceneWorld.ReportAttention / ReleaseAttention).
using System;
using UnityEngine;

namespace SceneAgent
{
    public class SceneAgentGaze : MonoBehaviour
    {
        public enum Source { MousePointer, CameraCentre, RayFromTransform }

        public SceneWorld world;
        public SceneAgentSession session;
        [Tooltip("Empty: Camera.main.")]
        public Camera viewCamera;
        [Tooltip("MousePointer falls back to the camera centre when the old Input Manager is off (Input System only). " +
                 "RayFromTransform: along rayOrigin's forward axis (a hand controller for pointing).")]
        public Source source = Source.MousePointer;
        public Transform rayOrigin;
        public float maxDistance = 30f;
        [Header("V1 attention")]
        [Tooltip("The person this is (a SceneWorld.people id).")]
        public string person = "user";
        [Tooltip("gaze or point")]
        public string act = "gaze";
        [Tooltip("How long a target must be held before it counts (and how long looking away must last to release it).")]
        public float dwellSeconds = 0.5f;

        /// <summary>The id the user points at now, or null.</summary>
        public string Current { get; private set; }

        string candidate, reported;
        float since;

        void Update()
        {
            if (world == null) world = FindObjectOfType<SceneWorld>();
            var cam = viewCamera != null ? viewCamera : Camera.main;
            if (world == null || (session != null && session.Busy)) return;
            Ray ray;
            if (source == Source.RayFromTransform && rayOrigin != null) ray = new Ray(rayOrigin.position, rayOrigin.forward);
            else if (cam == null) return;
            else ray = source == Source.MousePointer && TryMouse(out var p)
                ? cam.ScreenPointToRay(p) : cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            string id = null;
            if (Physics.Raycast(ray, out var hit, maxDistance))
            {
                var it = hit.collider.GetComponentInParent<SceneItem>();
                if (it != null && it.room == world.playerRoom) id = it.itemId;
            }
            Current = id;
            if (act == "gaze" && person == "user") world.gaze = id;
            Dwell(id);
        }

        float reportedAt;
        const float RefreshSeconds = 10f;

        void Dwell(string id)
        {
            if (id != candidate) { candidate = id; since = Time.unscaledTime; return; }
            if (Time.unscaledTime - since < dwellSeconds) return;
            if (id == null)
            {
                if (reported != null) world.ReleaseAttention(person, act);
                reported = null;
                return;
            }
            // a target held longer is reported again every 10 s (the scene shows attention up to 30 s old), and at
            // once when the ledger lost it, e.g. after a history reset (audit 04)
            var l = world.attention.Get(person, act);
            bool shown = l != null && l.Count > 0 && l[0].id == id && l[0].active;
            if (id == reported && shown && Time.unscaledTime - reportedAt < RefreshSeconds) return;
            world.ReportAttention(person, act, id);
            reported = id;
            reportedAt = Time.unscaledTime;
        }

        static bool TryMouse(out Vector3 p)
        {
            try { p = Input.mousePosition; return true; }
            catch (InvalidOperationException) { p = default; return false; }   // Input System only
        }
    }
}
