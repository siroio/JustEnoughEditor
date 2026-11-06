using UnityEditor;
using UnityEngine;

namespace JustEnoughEditor
{
    // Keep a selected idle pane so every real member's Focus triggers activation.
    internal class JEEWindowGroupParking : EditorWindow
    {
        [SerializeField] internal JEEWindowGroup owner;
    }
}
