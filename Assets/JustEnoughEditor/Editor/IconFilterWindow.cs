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
        private List<Type> m_componentTypes = new List<Type>();

        /// <summary>スクロールビューの現在位置。</summary>
        private Vector2 m_scrollPosition;

        /// <summary>ウィンドウを開く、または既存ウィンドウにフォーカスする。</summary>
        public static void ShowWindow()
        {
            GetWindow<IconFilterWindow>("Icon Filter Settings");
        }

        private void OnEnable()
        {
            RefreshComponentTypes();
        }

        /// <summary>
        /// 現在開いているシーンをスキャンしてコンポーネント型の一覧を再収集する。
        /// Transform は除外し、FullName の昇順でソートする。
        /// </summary>
        private void RefreshComponentTypes()
        {
            m_componentTypes.Clear();
            var allComponents = FindObjectsOfType<Component>();
            var typeSet = new HashSet<Type>();
            foreach (var comp in allComponents)
            {
                if (comp == null) continue;
                Type t = comp.GetType();
                if (t == typeof(Transform)) continue;
                typeSet.Add(t);
            }
            m_componentTypes = typeSet.OrderBy(t => t.FullName).ToList();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Component Icon Filters", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            if (GUILayout.Button("Refresh"))
                RefreshComponentTypes();

            EditorGUILayout.Space();

            m_scrollPosition = EditorGUILayout.BeginScrollView(m_scrollPosition);
            foreach (var type in m_componentTypes)
            {
                string key = $"JEE_IconFilter_{type.FullName}";
                bool currentValue = EditorPrefs.GetBool(key, true);
                bool newValue = EditorGUILayout.Toggle(type.FullName, currentValue);
                if (newValue != currentValue)
                {
                    EditorPrefs.SetBool(key, newValue);
                    EditorApplication.RepaintHierarchyWindow();
                }
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();

            // すべてのフィルター設定を初期状態（全表示）に戻す
            if (GUILayout.Button("Reset All Filters"))
            {
                foreach (var type in m_componentTypes)
                    EditorPrefs.DeleteKey($"JEE_IconFilter_{type.FullName}");
                EditorApplication.RepaintHierarchyWindow();
            }
        }
    }
}
