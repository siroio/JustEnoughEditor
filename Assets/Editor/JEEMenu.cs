using UnityEditor;

namespace JustEnoughEditor
{
    public static class JEEMenu
    {
        private const string HierarchyToggleKey = "JEE_HierarchyExtensionEnabled";
        private const string ProjectToggleKey = "JEE_ProjectFavoriteColorsEnabled";

        public static bool IsHierarchyEnabled
        {
            get => EditorPrefs.GetBool(HierarchyToggleKey, true);
            set => EditorPrefs.SetBool(HierarchyToggleKey, value);
        }

        public static bool IsProjectColorsEnabled
        {
            get => EditorPrefs.GetBool(ProjectToggleKey, true);
            set => EditorPrefs.SetBool(ProjectToggleKey, value);
        }

        [MenuItem("JEE/Favorite Folders", false, 1)]
        public static void OpenFavoriteFoldersWindow()
        {
            FavoriteFoldersWindow.ShowWindow();
        }

        [MenuItem("JEE/Enable Hierarchy Extension", false, 50)]
        public static void ToggleHierarchyExtension()
        {
            IsHierarchyEnabled = !IsHierarchyEnabled;
            EditorApplication.RepaintHierarchyWindow();
        }

        [MenuItem("JEE/Enable Hierarchy Extension", true)]
        public static bool ValidateToggleHierarchyExtension()
        {
            Menu.SetChecked("JEE/Enable Hierarchy Extension", IsHierarchyEnabled);
            return true;
        }

        [MenuItem("JEE/Enable Favorite Folders Extension", false, 51)]
        public static void ToggleProjectColors()
        {
            IsProjectColorsEnabled = !IsProjectColorsEnabled;
            EditorApplication.RepaintProjectWindow();
        }

        [MenuItem("JEE/Enable Favorite Folders Extension", true)]
        public static bool ValidateToggleProjectColors()
        {
            Menu.SetChecked("JEE/Enable Favorite Folders Extension", IsProjectColorsEnabled);
            return true;
        }
    }
}
