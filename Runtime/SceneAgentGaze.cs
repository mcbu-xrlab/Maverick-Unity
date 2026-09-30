// Sets SceneWorld.gaze to the SceneItem the user points at, so "this" and "that" resolve: the object under the mouse
// pointer (flat screen) or at the centre of the camera (head gaze in XR). The objects need colliders. While the agent is
// answering, the gaze is left as it was, so the command sees what the user pointed at when they asked.
using System;
using UnityEngine;

namespace SceneAgent
{
    public class SceneAgentGaze : MonoBehaviour
    {
        public enum Source { MousePointer, CameraCentre }

        public SceneWorld world;
        public SceneAgentSession session;
        [Tooltip("Empty: Camera.main.")]
        public Camera viewCamera;
        [Tooltip("MousePointer falls back to the camera centre when the old Input Manager is off (Input System only).")]
        public Source source = Source.MousePointer;
        public float maxDistance = 30f;

        /// <summary>The id the user points at now, or null.</summary>
        public string Current { get; private set; }

        void Update()
        {
            if (world == null) world = FindObjectOfType<SceneWorld>();
            var cam = viewCamera != null ? viewCamera : Camera.main;
            if (world == null || cam == null || (session != null && session.Busy)) return;
            Ray ray = source == Source.MousePointer && TryMouse(out var p)
                ? cam.ScreenPointToRay(p) : cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            string id = null;
            if (Physics.Raycast(ray, out var hit, maxDistance))
            {
                var it = hit.collider.GetComponentInParent<SceneItem>();
                if (it != null && it.room == world.playerRoom) id = it.itemId;
            }
            Current = id;
            world.gaze = id;
        }

        static bool TryMouse(out Vector3 p)
        {
            try { p = Input.mousePosition; return true; }
            catch (InvalidOperationException) { p = default; return false; }   // Input System only
        }
    }
}
