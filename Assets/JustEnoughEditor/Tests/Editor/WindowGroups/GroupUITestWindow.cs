using UnityEditor;
using UnityEngine.UIElements;

namespace JustEnoughEditor.Tests
{
    public class GroupUITestWindow : EditorWindow
    {
        public int value;
        public void CreateGUI() { rootVisualElement.Add(new Label("UI Toolkit group test")); }
    }
}
