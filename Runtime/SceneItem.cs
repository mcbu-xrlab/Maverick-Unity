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
        /// <summary>While something it rests on is held: its offset from that object (it has no position of its own
        /// then, hasPos is false). Set and cleared by SceneWorld.</summary>
        [System.NonSerialized] public double[] carry;

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
        [Tooltip("V1: pressing this button can be undone (a toggle). Off: a press cannot be undone, so a command with a " +
                 "press is not undoable.")]
        public bool reversiblePress;
        [Tooltip("States set_state may put it in, e.g. on, off / open, closed. Empty: none.")]
        public string[] states = new string[0];
    }
}
