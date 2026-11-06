using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace JustEnoughEditor
{
    internal static class JEEWindowGroupSettings
    {
        private const string Path = "Project/Just Enough Editor/Window Groups";

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
                    var canEdit = !group.maximized && JEEDockAdapter.IsOrdinaryDock(JEEDockAdapter.Dock(group));
                    using (new EditorGUI.DisabledScope(!canEdit))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label(group.titleContent.image, GUILayout.Width(18), GUILayout.Height(18));
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
                                EditorGUILayout.LabelField(new GUIContent(member.titleContent.text + "  #" + member.GetInstanceID(), member.titleContent.image, member.GetType().FullName));
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
