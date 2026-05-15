using UnityEditor;
using UnityEngine;

namespace JustEnoughEditor
{
    public class JEESettingsWindow : EditorWindow
    {
        private static Vector2 s_scrollPosition;

        private void OnGUI()
        {
            s_scrollPosition = EditorGUILayout.BeginScrollView(s_scrollPosition);
            DrawSettingsContent();
            EditorGUILayout.EndScrollView();
        }

        public static void ShowWindow()
        {
            var window = GetWindow<JEESettingsWindow>("JEE Settings");
            window.minSize = new Vector2(430, 360);
            window.Show();
        }

        public static void DrawSettingsContent()
        {
            JEEEditorStyles.DrawHeader(
                "Just Enough Editor",
                "Hierarchy visibility, favorite assets, icon filters, and personal editor settings.");
            DrawStatusSummary();
            DrawHierarchySettings();
            EditorGUILayout.Space(8);
            DrawFavoriteAssetSettings();
            EditorGUILayout.Space(8);
            DrawUtilityButtons();
        }

        private static void DrawStatusSummary()
        {
            using (new EditorGUILayout.HorizontalScope(JEEEditorStyles.ToolbarCard))
            {
                GUILayout.Label("Status", JEEEditorStyles.SectionTitle, GUILayout.Width(48));
                JEEEditorStyles.DrawStatusPill("Hierarchy", JEEMenu.IsHierarchyEnabled, GUILayout.Width(118));
                JEEEditorStyles.DrawStatusPill("Colors", JEEMenu.IsHierarchyColorEnabled, GUILayout.Width(98));
                JEEEditorStyles.DrawStatusPill("Icons", JEEMenu.IsHierarchyIconEnabled, GUILayout.Width(92));
                JEEEditorStyles.DrawStatusPill("Project", JEEMenu.IsProjectColorsEnabled, GUILayout.Width(98));
                GUILayout.FlexibleSpace();
            }
        }

        private static void DrawHierarchySettings()
        {
            JEEEditorStyles.BeginCard("Hierarchy", "d_SceneHierarchyWindow Icon");

            EditorGUI.BeginChangeCheck();
            var hierarchyEnabled = EditorGUILayout.ToggleLeft("Enable Hierarchy Extension", JEEMenu.IsHierarchyEnabled);
            var colorsEnabled = EditorGUILayout.ToggleLeft("Draw hierarchy colors", JEEMenu.IsHierarchyColorEnabled);
            var iconsEnabled = EditorGUILayout.ToggleLeft("Draw component icons", JEEMenu.IsHierarchyIconEnabled);
            EditorGUILayout.Space(4);
            var overlapGuard = EditorGUILayout.IntSlider("Icon Overlap Guard", JEEMenu.OverlapGuard, 40, 200);
            var maxIconCount = EditorGUILayout.IntSlider("Max Icon Count", JEEPrefs.MaxIconCount, 2, 8);
            var priorityMode =
                (JEEComponentPriorityMode)EditorGUILayout.EnumPopup("Component Priority",
                    JEEPrefs.ComponentPriorityMode);

            if (EditorGUI.EndChangeCheck())
            {
                JEEMenu.IsHierarchyEnabled = hierarchyEnabled;
                JEEMenu.IsHierarchyColorEnabled = colorsEnabled;
                JEEMenu.IsHierarchyIconEnabled = iconsEnabled;
                JEEMenu.OverlapGuard = overlapGuard;
                JEEPrefs.MaxIconCount = maxIconCount;
                JEEPrefs.ComponentPriorityMode = priorityMode;
                EditorApplication.RepaintHierarchyWindow();
            }

            EditorGUILayout.Space(4);
            GUILayout.Label(
                "Overlap Guard keeps icons away from object names. Priority controls which component icons win when space is limited.",
                JEEEditorStyles.MutedLabel);
            JEEEditorStyles.EndCard();
        }

        private static void DrawFavoriteAssetSettings()
        {
            JEEEditorStyles.BeginCard("Favorite Assets", "Folder Icon");

            EditorGUI.BeginChangeCheck();
            var projectColorsEnabled = EditorGUILayout.ToggleLeft("Draw favorite item colors in Project window",
                JEEMenu.IsProjectColorsEnabled);

            if (EditorGUI.EndChangeCheck())
            {
                JEEMenu.IsProjectColorsEnabled = projectColorsEnabled;
                EditorApplication.RepaintProjectWindow();
            }

            EditorGUILayout.Space(4);
            GUILayout.Label(
                "Favorite items can be stored privately in EditorPrefs or shared through a project asset.",
                JEEEditorStyles.MutedLabel);
            JEEEditorStyles.EndCard();
        }

        private static void DrawUtilityButtons()
        {
            JEEEditorStyles.BeginCard("Tools", "d_Toolbar Plus");

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(
                        new GUIContent(" Favorite Assets", EditorGUIUtility.IconContent("Folder Icon").image),
                        GUILayout.Height(28)))
                    FavoriteAssetsWindow.ShowWindow();

                if (GUILayout.Button(
                        new GUIContent(" Icon Filters", EditorGUIUtility.IconContent("d_FilterByType").image),
                        GUILayout.Height(28)))
                    IconFilterWindow.ShowWindow();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(
                        new GUIContent(" Missing Scripts", EditorGUIUtility.IconContent("console.erroricon").image),
                        GUILayout.Height(28)))
                    MissingScriptFinderWindow.ShowWindow();

                if (GUILayout.Button(new GUIContent(" Export", EditorGUIUtility.IconContent("d_SaveAs").image),
                        GUILayout.Height(28)))
                    JEESettingsIO.ExportSettings();

                if (GUILayout.Button(new GUIContent(" Import", EditorGUIUtility.IconContent("Import").image),
                        GUILayout.Height(28)))
                    JEESettingsIO.ImportSettings();
            }

            JEEEditorStyles.EndCard();
        }
    }
}
