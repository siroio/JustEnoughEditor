using UnityEditor;
using UnityEngine;

namespace JustEnoughEditor
{
    public static class JEEHierarchyActions
    {
        [MenuItem("GameObject/JEE/Assign Custom Icon...", false, 30)]
        public static void AssignCustomIconFromHierarchy()
        {
            HierarchyColorAndIconDrawer.AssignCustomIcon(Selection.activeGameObject);
        }

        [MenuItem("GameObject/JEE/Assign Custom Icon...", true)]
        public static bool ValidateAssignCustomIconFromHierarchy()
        {
            return Selection.activeGameObject != null;
        }

        [MenuItem("GameObject/JEE/Remove Custom Icon", false, 31)]
        public static void RemoveCustomIconFromHierarchy()
        {
            HierarchyColorAndIconDrawer.RemoveCustomIcon(Selection.activeGameObject);
        }

        [MenuItem("GameObject/JEE/Remove Custom Icon", true)]
        public static bool ValidateRemoveCustomIconFromHierarchy()
        {
            return HierarchyColorAndIconDrawer.HasCustomIcon(Selection.activeGameObject);
        }

        [MenuItem("JEE/Create Section Separator", false, 20)]
        public static void CreateSectionSeparator()
        {
            CreateSeparator("--- Section ---", Selection.activeGameObject);
        }

        [MenuItem("JEE/Create Group Separator", false, 21)]
        public static void CreateGroupSeparator()
        {
            CreateSeparator("[Group]", Selection.activeGameObject);
        }

        [MenuItem("GameObject/JEE/Create Section Separator", false, 40)]
        public static void CreateSectionSeparatorFromHierarchy()
        {
            CreateSeparator("--- Section ---", Selection.activeGameObject);
        }

        [MenuItem("GameObject/JEE/Create Group Separator", false, 41)]
        public static void CreateGroupSeparatorFromHierarchy()
        {
            CreateSeparator("[Group]", Selection.activeGameObject);
        }

        private static void CreateSeparator(string name, GameObject reference)
        {
            var separator = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(separator, "Create Hierarchy Separator");

            if (reference != null)
            {
                separator.transform.SetParent(reference.transform.parent);
                separator.transform.SetSiblingIndex(reference.transform.GetSiblingIndex() + 1);
            }

            Selection.activeGameObject = separator;
            EditorApplication.RepaintHierarchyWindow();
        }
    }
}
