using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace JustEnoughEditor
{
    public class MissingScriptFinderWindow : EditorWindow
    {
        private readonly List<GameObject> targets = new();
        private Vector2 scrollPosition;

        private void OnGUI()
        {
            JEEEditorStyles.DrawHeader(
                "Missing Script Finder",
                "Find, select, ping, and remove missing MonoBehaviour references in open scenes.");
            DrawToolbar();
            DrawList();
        }

        public static void ShowWindow()
        {
            var window = GetWindow<MissingScriptFinderWindow>("Missing Script Finder");
            window.minSize = new Vector2(520, 300);
            window.Refresh();
            window.Show();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(JEEEditorStyles.ToolbarCard))
            {
                GUILayout.Label(EditorGUIUtility.IconContent("console.erroricon").image, GUILayout.Width(18),
                    GUILayout.Height(18));
                GUILayout.Label("Missing Script Objects", JEEEditorStyles.SectionTitle, GUILayout.Width(145));
                GUILayout.Label(targets.Count.ToString(), JEEEditorStyles.Pill, GUILayout.Width(48));
                GUILayout.FlexibleSpace();

                if (GUILayout.Button(new GUIContent("Refresh", EditorGUIUtility.IconContent("d_Refresh").image),
                        GUILayout.Width(92)))
                    Refresh();

                using (new EditorGUI.DisabledScope(targets.Count == 0))
                {
                    if (GUILayout.Button(
                            new GUIContent("Remove All", EditorGUIUtility.IconContent("TreeEditor.Trash").image),
                            GUILayout.Width(112)))
                        RemoveAllMissingScripts();
                }
            }
        }

        private void DrawList()
        {
            if (targets.Count == 0)
            {
                JEEEditorStyles.DrawEmptyState(
                    "No missing scripts found",
                    "The open scenes do not currently contain missing MonoBehaviour references.",
                    "TestPassed");
                return;
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            foreach (var target in targets)
            {
                if (target == null) continue;

                using (new EditorGUILayout.HorizontalScope(JEEEditorStyles.Card))
                {
                    GUILayout.Label(EditorGUIUtility.IconContent("GameObject Icon").image, GUILayout.Width(20),
                        GUILayout.Height(20));
                    GUILayout.Label(GetHierarchyPath(target), JEEEditorStyles.MutedLabel, GUILayout.ExpandWidth(true));

                    if (GUILayout.Button(new GUIContent("Select", "Select GameObject"), GUILayout.Width(56)))
                        Selection.activeGameObject = target;

                    if (GUILayout.Button(new GUIContent("Ping", "Ping GameObject"), GUILayout.Width(44)))
                        EditorGUIUtility.PingObject(target);

                    if (GUILayout.Button(
                            new GUIContent("Remove", EditorGUIUtility.IconContent("TreeEditor.Trash").image),
                            GUILayout.Width(78)))
                    {
                        Undo.RegisterCompleteObjectUndo(target, "Remove Missing Scripts");
                        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(target);
                        Refresh();
                        GUIUtility.ExitGUI();
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void Refresh()
        {
            targets.Clear();

            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null) continue;
                if (EditorUtility.IsPersistent(go)) continue;
                if (!go.scene.IsValid()) continue;
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go) > 0)
                    targets.Add(go);
            }

            Repaint();
        }

        private void RemoveAllMissingScripts()
        {
            if (!EditorUtility.DisplayDialog(
                    "Remove Missing Scripts",
                    $"Remove missing scripts from {targets.Count} GameObject(s)?",
                    "Remove",
                    "Cancel"))
                return;

            foreach (var target in targets)
            {
                if (target == null) continue;
                Undo.RegisterCompleteObjectUndo(target, "Remove Missing Scripts");
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(target);
            }

            Refresh();
            EditorApplication.RepaintHierarchyWindow();
        }

        private static string GetHierarchyPath(GameObject target)
        {
            var path = target.name;
            var current = target.transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }
    }
}
