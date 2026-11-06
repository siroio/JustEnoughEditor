using UnityEditor;
using UnityEngine;

namespace JustEnoughEditor.Tests
{
    public class GroupTestWindow : EditorWindow
    {
        public int value;
        public bool throwOnNextAdd;
        private void OnAddedAsTab()
        {
            if (!throwOnNextAdd) return;
            throwOnNextAdd = false;
            throw new System.InvalidOperationException("Disposable window rejected the destination");
        }
        public void SetDirty(bool value)
        {
            hasUnsavedChanges = value;
            saveChangesMessage = "Disposable group test";
        }
        private void OnGUI() { GUILayout.Label("Window group test: " + value); }
    }
}
