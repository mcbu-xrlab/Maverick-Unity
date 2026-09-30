using UnityEngine;

namespace SceneAgent
{
    /// <summary>Marks a GameObject as addressable by the agent. Ids must be unique and stable.</summary>
    public class SceneItem : MonoBehaviour
    {
        public string itemId;
        public string label;            // cube, mug, table, lamp, door, button, ...
        public string color;            // optional
        public string room;
        public string on = "floor";     // support id, "floor", "left_hand", "right_hand" or "both_hands"
        public string relation;         // "on" / "in" once placed
        public string orientation;      // set by place(); null = default
        public string state;            // for devices: on/off/open/closed/...
        public bool hasPos = true;
        public double[] pos = new double[3];   // logical position (bottom of the object), metres
        public double yaw, pitch, roll;

        [Header("Affordances")]
        [Tooltip("Off: what this object can do comes from the model's catalogue of ~200 object types, looked up by its " +
                 "label (SceneCatalog). On: the fields below decide instead; use it for labels the catalogue does not know.")]
        public bool overrideAffordances;
        public bool grabbable;
        [Tooltip("Things can be placed on it; surfaceHeight is where they land, above the object's bottom (metres).")]
        public bool isSurface;
        public float surfaceHeight = 0.75f;
        [Tooltip("Things can be placed in it (unless its state is closed or locked).")]
        public bool isContainer;
        [Tooltip("Cannot be moved or rotated (doors, built-in appliances, ...).")]
        public bool isFixed;
        public bool isButton;
        [Tooltip("States set_state may put it in, e.g. on, off / open, closed. Empty: none.")]
        public string[] states = new string[0];
    }
}
