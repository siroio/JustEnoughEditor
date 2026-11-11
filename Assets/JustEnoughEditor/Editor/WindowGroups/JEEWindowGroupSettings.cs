using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace JustEnoughEditor
{
    internal static class JEEWindowGroupSettings
    {
        private const string Path = "Project/Just Enough Editor/Window Groups";
        internal const string AutoExpandCollapseKey = "JustEnoughEditor.WindowGroups.AutoExpandCollapse";
        internal static bool AutoExpandCollapse
        {
            get => EditorPrefs.GetBool(AutoExpandCollapseKey, true);
            set => EditorPrefs.SetBool(AutoExpandCollapseKey, value);
        }

        [SettingsProvider]
        internal static SettingsProvider CreateUserProvider() => new SettingsProvider("Preferences/Just Enough Editor/Window Groups", SettingsScope.User)
        {
            label = "Window Groups",
            guiHandler = _ =>
            {
                AutoExpandCollapse = EditorGUILayout.Toggle("Auto expand / collapse", AutoExpandCollapse);
                EditorGUILayout.LabelField("Expand a group when one of its tabs receives focus. Collapse other groups to their last-used tab. Turn off to use the tab context menu instead.", EditorStyles.wordWrappedLabel);
            },
            keywords = new[] { "tabs", "groups", "focus", "automatic", "collapse" }
        };

        [SettingsProvider]
        internal static SettingsProvider CreateProvider() => new SettingsProvider(Path, SettingsScope.Project)
        {
            label = "Window Groups",
            guiHandler = _ => Draw(),
            keywords = new[] { "tabs", "groups", "windows" }
        };

        [MenuItem("JEE/Window Groups/Settings…", false, 20)]
        internal static void Open() => SettingsService.OpenProjectSettings(Path);

        [MenuItem("JEE/Window Groups/Restore All Grouped Tabs", false, 21)]
        private static void RestoreAll()
        {
            try { JEEWindowGroups.RestoreAll(); }
            catch (Exception ex) { EditorUtility.DisplayDialog("Window Groups", ex.GetBaseException().Message, "OK"); }
            SettingsService.RepaintAllSettingsWindow();
        }

        private static void Draw()
        {
            EditorGUILayout.LabelField("Groups in your current editor layout. Right-click a tab to create a group. Colors are assigned automatically.", EditorStyles.wordWrappedLabel);
            if (GUILayout.Button("User Preferences…", GUILayout.Width(140)))
                SettingsService.OpenUserPreferences("Preferences/Just Enough Editor/Window Groups");
            if (JEEDockAdapter.UnavailableReason != null)
            {
                EditorGUILayout.HelpBox(JEEDockAdapter.UnavailableReason, MessageType.Warning);
                return;
            }
            var groups = Resources.FindObjectsOfTypeAll<JEEWindowGroup>().Where(g => !g.disposing).OrderBy(g => g.groupName).ToArray();
            if (groups.Length == 0) EditorGUILayout.HelpBox("No window groups in this layout.", MessageType.Info);
            foreach (var group in groups)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    var canEdit = !group.members.Any(w => w && w.maximized) && JEEDockAdapter.IsOrdinaryDock(group.ownerDock);
                    using (new EditorGUI.DisabledScope(!canEdit))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUI.DrawRect(GUILayoutUtility.GetRect(12, 18, GUILayout.Width(12)), group.groupColor);
                            EditorGUI.BeginChangeCheck();
                            var name = EditorGUILayout.DelayedTextField(group.groupName);
                            if (EditorGUI.EndChangeCheck())
                            {
                                group.groupName = string.IsNullOrWhiteSpace(name) ? "Window Group" : name.Trim();
                                group.RefreshTitle();
                            }
                            if (GUILayout.Button(group.collapsed ? "Expand" : "Collapse", GUILayout.Width(72)))
                                group.Schedule(() => JEEWindowGroups.SetCollapsed(group, !group.collapsed));
                            if (GUILayout.Button("Ungroup", GUILayout.Width(72)))
                                group.Schedule(() => JEEWindowGroups.Ungroup(group));
                        }
                        foreach (var member in group.members.Where(w => w).ToArray())
                        {
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                var title = group.MemberTitle(member);
                                EditorGUILayout.LabelField(new GUIContent(title.text + "  #" + member.GetInstanceID(), title.image, member.GetType().FullName));
                                if (GUILayout.Button(new GUIContent("Remove", "Keep this window as an ungrouped tab"), GUILayout.Width(72)))
                                    group.Schedule(() => JEEWindowGroups.RemoveMember(group, member));
                            }
                        }
                        if (GUILayout.Button("Add Tab…", GUILayout.Width(90)))
                        {
                            var menu = new GenericMenu();
                            group.AddTabItems(menu, "");
                            menu.ShowAsContext();
                        }
                    }
                    if (!canEdit) EditorGUILayout.HelpBox("Restore the group to an ordinary dock to edit it.", MessageType.Info);
                    if (!string.IsNullOrEmpty(group.lastError)) EditorGUILayout.HelpBox(group.lastError, MessageType.Warning);
                }
            }
        }
    }
}
