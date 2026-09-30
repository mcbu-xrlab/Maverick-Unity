// Generated from the object catalogue the model was trained on. Do not edit.
// SceneWorld.cs checks tool calls against these, exactly as the training simulator does.
using System.Collections.Generic;

namespace SceneAgent
{
    public static class SceneCatalog
    {
        public static readonly Dictionary<string, double> Top = new Dictionary<string, double> { { "armchair", 0.45 }, { "bed", 0.5 }, { "bench", 0.45 }, { "cart", 0.8 }, { "coffee maker", 0.3 }, { "coffee table", 0.45 }, { "counter", 0.9 }, { "cutting board", 0.75 }, { "desk", 0.75 }, { "dining table", 0.75 }, { "dresser", 0.9 }, { "nightstand", 0.6 }, { "ottoman", 0.4 }, { "pallet", 0.15 }, { "shelf", 1.2 }, { "side table", 0.6 }, { "sofa", 0.45 }, { "stove", 0.9 }, { "table", 0.75 }, { "toilet paper hanger", 0.6 }, { "towel holder", 1.0 }, { "tv stand", 0.5 }, { "workbench", 0.9 } };
        public static readonly HashSet<string> Grasp = new HashSet<string> { "alarm clock", "aluminum foil", "apple", "backpack", "ball", "banana", "baseball bat", "basketball", "battery", "blanket", "blender", "book", "boots", "bottle", "bowl", "box", "bread", "bucket", "butter knife", "cable", "calculator", "camera", "candle", "carrot", "cd", "charger", "clipboard", "cloth", "computer mouse", "controller", "credit card", "cube", "cup", "cutting board", "drill", "drone", "dumbbell", "egg", "envelope", "fire extinguisher", "first aid kit", "flashlight", "flower", "folder", "fork", "globe", "gloves", "hair dryer", "hammer", "hand towel", "headphones", "helmet", "jar", "kettle", "key", "key chain", "keyboard", "knife", "ladle", "laptop", "lettuce", "marker", "microphone", "mug", "newspaper", "notebook", "orange", "pan", "paper towel roll", "pen", "pencil", "pepper shaker", "phone", "picture frame", "pillow", "plate", "pliers", "plunger", "pot", "potato", "radio", "remote", "ruler", "salt shaker", "saw", "scissors", "screw box", "screwdriver", "scrub brush", "soap", "soap bottle", "spatula", "sponge", "spoon", "spray bottle", "stapler", "statue", "suitcase", "tablet", "tape", "tape measure", "teddy bear", "tennis racket", "thermos", "tissue box", "toilet paper", "tomato", "toolbox", "toothbrush", "towel", "traffic cone", "trophy", "umbrella", "vr headset", "watch", "water bottle", "watering can", "wine bottle", "wrench" };
        public static readonly HashSet<string> Container = new HashSet<string> { "backpack", "barrel", "basket", "bathtub", "bowl", "box", "bucket", "cabinet", "candle holder", "chest", "cup", "desk organizer", "dishwasher", "drawer", "filing cabinet", "first aid kit", "fridge", "jar", "laundry hamper", "microwave", "oven", "pan", "pot", "safe", "screw box", "sink", "suitcase", "toaster", "toilet", "toolbox", "trash bin", "trash can", "vase", "wardrobe" };
        public static readonly HashSet<string> Fixed = new HashSet<string> { "air conditioner", "bathtub", "blinds", "button", "cabinet", "ceiling light", "clock", "control panel", "dishwasher", "door", "drawer", "faucet", "filing cabinet", "fridge", "garage door", "hologram panel", "lever", "light switch", "mirror", "oven", "painting", "poster", "robot arm", "shower curtain", "shower door", "shower head", "sink", "stove", "stove knob", "terminal", "toilet", "toilet paper hanger", "towel holder", "valve", "wardrobe", "whiteboard", "window" };
        public static readonly HashSet<string> Button = new HashSet<string> { "button", "control panel", "lever" };
        public static readonly Dictionary<string, string[]> States = new Dictionary<string, string[]> {
            { "air conditioner", new[] { "on", "off" } },
            { "blender", new[] { "on", "off" } },
            { "blinds", new[] { "open", "closed" } },
            { "cabinet", new[] { "open", "closed" } },
            { "candle", new[] { "on", "off" } },
            { "ceiling light", new[] { "on", "off" } },
            { "chest", new[] { "open", "closed", "locked", "unlocked" } },
            { "coffee maker", new[] { "on", "off" } },
            { "desk lamp", new[] { "on", "off" } },
            { "dishwasher", new[] { "on", "off", "open", "closed" } },
            { "door", new[] { "open", "closed", "half_open" } },
            { "drawer", new[] { "open", "closed" } },
            { "fan", new[] { "on", "off" } },
            { "faucet", new[] { "on", "off" } },
            { "filing cabinet", new[] { "open", "closed" } },
            { "flashlight", new[] { "on", "off" } },
            { "floor lamp", new[] { "on", "off" } },
            { "fridge", new[] { "open", "closed" } },
            { "garage door", new[] { "open", "closed" } },
            { "hair dryer", new[] { "on", "off" } },
            { "hologram panel", new[] { "on", "off" } },
            { "kettle", new[] { "open", "closed" } },
            { "lamp", new[] { "on", "off" } },
            { "laptop", new[] { "on", "off", "open", "closed" } },
            { "light switch", new[] { "on", "off" } },
            { "microwave", new[] { "on", "off", "open", "closed" } },
            { "monitor", new[] { "on", "off" } },
            { "oven", new[] { "on", "off", "open", "closed" } },
            { "printer", new[] { "on", "off" } },
            { "projector", new[] { "on", "off" } },
            { "radio", new[] { "on", "off" } },
            { "robot arm", new[] { "on", "off" } },
            { "safe", new[] { "open", "closed", "locked", "unlocked" } },
            { "shower curtain", new[] { "open", "closed" } },
            { "shower door", new[] { "open", "closed" } },
            { "shower head", new[] { "on", "off" } },
            { "speaker", new[] { "on", "off" } },
            { "stove", new[] { "on", "off" } },
            { "stove knob", new[] { "on", "off" } },
            { "terminal", new[] { "on", "off" } },
            { "toaster", new[] { "on", "off" } },
            { "toilet", new[] { "open", "closed" } },
            { "tv", new[] { "on", "off" } },
            { "valve", new[] { "open", "closed" } },
            { "wardrobe", new[] { "open", "closed" } },
            { "window", new[] { "open", "closed" } } };
    }
}
