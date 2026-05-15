using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace JustEnoughEditor
{
    public enum JEEComponentPriorityMode
    {
        UserScriptsFirst,
        BuiltInFirst,
        SceneOrder
    }

    [Serializable]
    public class JEEExportedSettings
    {
        public bool hierarchyEnabled;
        public bool projectColorsEnabled;
        public bool hierarchyColorEnabled;
        public bool hierarchyIconEnabled;
        public int overlapGuard;
        public int maxIconCount;
        public JEEComponentPriorityMode componentPriorityMode;
        public List<JEEStringBoolSetting> iconFilters = new();
    }

    [Serializable]
    public class JEEStringBoolSetting
    {
        public string key;
        public bool value;
    }

    public static class JEEPrefs
    {
        private const string k_HierarchyToggleKey = "JEE_HierarchyExtensionEnabled";
        private const string k_ProjectToggleKey = "JEE_ProjectFavoriteColorsEnabled";
        private const string k_HierarchyColorToggleKey = "JEE_HierarchyColorEnabled";
        private const string k_HierarchyIconToggleKey = "JEE_HierarchyIconEnabled";
        private const string k_OverlapGuardKey = "JEE_IconOverlapGuard";
        private const string k_MaxIconCountKey = "JEE_MaxIconCount";
        private const string k_ComponentPriorityModeKey = "JEE_ComponentPriorityMode";

        public static bool IsHierarchyEnabled
        {
            get => EditorPrefs.GetBool(k_HierarchyToggleKey, true);
            set => EditorPrefs.SetBool(k_HierarchyToggleKey, value);
        }

        public static bool IsProjectColorsEnabled
        {
            get => EditorPrefs.GetBool(k_ProjectToggleKey, true);
            set => EditorPrefs.SetBool(k_ProjectToggleKey, value);
        }

        public static bool IsHierarchyColorEnabled
        {
            get => EditorPrefs.GetBool(k_HierarchyColorToggleKey, true);
            set => EditorPrefs.SetBool(k_HierarchyColorToggleKey, value);
        }

        public static bool IsHierarchyIconEnabled
        {
            get => EditorPrefs.GetBool(k_HierarchyIconToggleKey, true);
            set => EditorPrefs.SetBool(k_HierarchyIconToggleKey, value);
        }

        public static int OverlapGuard
        {
            get
            {
                var clamped = Mathf.Clamp(EditorPrefs.GetInt(k_OverlapGuardKey, 80), 40, 200);
                EditorPrefs.SetInt(k_OverlapGuardKey, clamped);
                return clamped;
            }
            set => EditorPrefs.SetInt(k_OverlapGuardKey, Mathf.Clamp(value, 40, 200));
        }

        public static int MaxIconCount
        {
            get
            {
                var clamped = Mathf.Clamp(EditorPrefs.GetInt(k_MaxIconCountKey, 4), 2, 8);
                EditorPrefs.SetInt(k_MaxIconCountKey, clamped);
                return clamped;
            }
            set => EditorPrefs.SetInt(k_MaxIconCountKey, Mathf.Clamp(value, 2, 8));
        }

        public static JEEComponentPriorityMode ComponentPriorityMode
        {
            get
            {
                var value = EditorPrefs.GetInt(k_ComponentPriorityModeKey,
                    (int)JEEComponentPriorityMode.UserScriptsFirst);
                if (!Enum.IsDefined(typeof(JEEComponentPriorityMode), value))
                    value = (int)JEEComponentPriorityMode.UserScriptsFirst;
                return (JEEComponentPriorityMode)value;
            }
            set => EditorPrefs.SetInt(k_ComponentPriorityModeKey, (int)value);
        }

        public static JEEExportedSettings Capture()
        {
            return new JEEExportedSettings
            {
                hierarchyEnabled = IsHierarchyEnabled,
                projectColorsEnabled = IsProjectColorsEnabled,
                hierarchyColorEnabled = IsHierarchyColorEnabled,
                hierarchyIconEnabled = IsHierarchyIconEnabled,
                overlapGuard = OverlapGuard,
                maxIconCount = MaxIconCount,
                componentPriorityMode = ComponentPriorityMode,
                iconFilters = CaptureIconFilters()
            };
        }

        public static void Apply(JEEExportedSettings settings)
        {
            if (settings == null) return;

            IsHierarchyEnabled = settings.hierarchyEnabled;
            IsProjectColorsEnabled = settings.projectColorsEnabled;
            IsHierarchyColorEnabled = settings.hierarchyColorEnabled;
            IsHierarchyIconEnabled = settings.hierarchyIconEnabled;
            OverlapGuard = settings.overlapGuard;
            MaxIconCount = settings.maxIconCount;
            ComponentPriorityMode = settings.componentPriorityMode;
            ApplyIconFilters(settings.iconFilters);

            EditorApplication.RepaintHierarchyWindow();
            EditorApplication.RepaintProjectWindow();
        }

        private static List<JEEStringBoolSetting> CaptureIconFilters()
        {
            var settings = new List<JEEStringBoolSetting>();
            var seenKeys = new HashSet<string>();

            foreach (var component in Resources.FindObjectsOfTypeAll<Component>())
            {
                if (component == null) continue;
                var type = component.GetType();
                if (type == typeof(Transform)) continue;

                var key = $"JEE_IconFilter_{type.FullName}";
                if (!seenKeys.Add(key)) continue;
                if (!EditorPrefs.HasKey(key)) continue;

                settings.Add(new JEEStringBoolSetting
                {
                    key = key,
                    value = EditorPrefs.GetBool(key, true)
                });
            }

            return settings;
        }

        private static void ApplyIconFilters(List<JEEStringBoolSetting> settings)
        {
            if (settings == null) return;

            foreach (var setting in settings)
            {
                if (string.IsNullOrEmpty(setting.key)) continue;
                if (!setting.key.StartsWith("JEE_IconFilter_", StringComparison.Ordinal)) continue;
                EditorPrefs.SetBool(setting.key, setting.value);
            }
        }
    }
}
