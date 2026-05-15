using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace JustEnoughEditor
{
    /// <summary>
    /// コンポーネントアイコンのフィルタリング設定ウィンドウ。
    /// 現在開いているシーンに存在するコンポーネント型の一覧を表示し、
    /// 各型のアイコン表示/非表示を EditorPrefs に保存して制御する。
    /// </summary>
    public class IconFilterWindow : EditorWindow
    {
        /// <summary>シーンから収集したコンポーネント型の一覧（Transform 除外・FullName 昇順）。</summary>
        private List<Type> m_componentTypes = new();

        /// <summary>スクロールビューの現在位置。</summary>
        private Vector2 m_scrollPosition;

        /// <summary>コンポーネント型一覧の検索文字列。</summary>
        private string m_searchQuery = "";

        private void OnEnable()
        {
            RefreshComponentTypes();
        }

        private void OnGUI()
        {
            JEEEditorStyles.DrawHeader(
                "Icon Filters",
                "Choose which component types appear as icons in the Hierarchy.");
            DrawHeader();
            DrawSearchField();

            m_scrollPosition = EditorGUILayout.BeginScrollView(m_scrollPosition);
            var visibleCount = 0;
            foreach (var type in m_componentTypes)
            {
                if (!MatchesSearch(type))
                    continue;

                visibleCount++;
                var key = $"JEE_IconFilter_{type.FullName}";
                var currentValue = EditorPrefs.GetBool(key, true);
                using (new EditorGUILayout.HorizontalScope(JEEEditorStyles.Card))
                {
                    Texture icon = AssetPreview.GetMiniTypeThumbnail(type);
                    if (icon != null)
                        GUILayout.Label(icon, GUILayout.Width(20), GUILayout.Height(20));

                    var newValue = EditorGUILayout.ToggleLeft(type.FullName, currentValue, GUILayout.Height(20));
                    if (newValue != currentValue)
                    {
                        EditorPrefs.SetBool(key, newValue);
                        EditorApplication.RepaintHierarchyWindow();
                    }
                }
            }

            EditorGUILayout.EndScrollView();

            if (m_componentTypes.Count == 0)
                JEEEditorStyles.DrawEmptyState(
                    "No component types found",
                    "Open a scene with components, then refresh this list.",
                    "d_FilterByType");
            else if (visibleCount == 0)
                JEEEditorStyles.DrawEmptyState(
                    "No components match the search",
                    "Try a class name or namespace from the current scene.",
                    "Search Icon");

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope(JEEEditorStyles.ToolbarCard))
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(
                        new GUIContent("Reset All Filters", EditorGUIUtility.IconContent("d_Refresh").image),
                        GUILayout.Width(150), GUILayout.Height(24)))
                {
                    foreach (var type in m_componentTypes)
                        EditorPrefs.DeleteKey($"JEE_IconFilter_{type.FullName}");
                    EditorApplication.RepaintHierarchyWindow();
                }
            }
        }

        /// <summary>ウィンドウを開く、または既存ウィンドウにフォーカスする。</summary>
        public static void ShowWindow()
        {
            var window = GetWindow<IconFilterWindow>("Icon Filter Settings");
            window.minSize = new Vector2(460, 320);
        }

        /// <summary>
        /// 現在開いているシーンをスキャンしてコンポーネント型の一覧を再収集する。
        /// Transform は除外し、FullName の昇順でソートする。
        /// </summary>
        private void RefreshComponentTypes()
        {
            m_componentTypes.Clear();
            var allComponents = FindObjectsByType<Component>(FindObjectsSortMode.None);
            var typeSet = new HashSet<Type>();
            foreach (var comp in allComponents)
            {
                if (comp == null) continue;
                var t = comp.GetType();
                if (t == typeof(Transform)) continue;
                typeSet.Add(t);
            }

            m_componentTypes = typeSet.OrderBy(t => t.FullName).ToList();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(JEEEditorStyles.ToolbarCard);
            GUILayout.Label(EditorGUIUtility.IconContent("d_FilterByType").image, GUILayout.Width(18),
                GUILayout.Height(18));
            GUILayout.Label("Component Icon Filters", JEEEditorStyles.SectionTitle, GUILayout.Width(150));
            GUILayout.Label($"{m_componentTypes.Count} types", JEEEditorStyles.Pill, GUILayout.Width(84));
            GUILayout.Label($"{GetHiddenFilterCount()} hidden", JEEEditorStyles.Pill, GUILayout.Width(84));
            GUILayout.FlexibleSpace();

            if (GUILayout.Button(new GUIContent("Refresh", EditorGUIUtility.IconContent("d_Refresh").image),
                    GUILayout.Width(92)))
                RefreshComponentTypes();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSearchField()
        {
            EditorGUILayout.BeginHorizontal(JEEEditorStyles.ToolbarCard);
            GUILayout.Label(EditorGUIUtility.IconContent("Search Icon").image, GUILayout.Width(18),
                GUILayout.Height(18));
            var searchStyle = JEEEditorStyles.SearchFieldStyle();
            m_searchQuery = GUILayout.TextField(m_searchQuery, searchStyle, GUILayout.MinWidth(120),
                GUILayout.ExpandWidth(true));
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(m_searchQuery)))
            {
                if (GUILayout.Button("Clear", GUILayout.Width(52)))
                {
                    m_searchQuery = "";
                    GUI.FocusControl(null);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private int GetHiddenFilterCount()
        {
            var count = 0;
            foreach (var type in m_componentTypes)
            {
                var key = $"JEE_IconFilter_{type.FullName}";
                if (!EditorPrefs.GetBool(key, true))
                    count++;
            }

            return count;
        }

        private bool MatchesSearch(Type type)
        {
            if (string.IsNullOrWhiteSpace(m_searchQuery))
                return true;

            return type.FullName.IndexOf(m_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   type.Name.IndexOf(m_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
