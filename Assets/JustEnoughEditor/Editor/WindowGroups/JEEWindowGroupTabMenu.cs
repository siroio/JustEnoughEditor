using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace JustEnoughEditor
{
    [InitializeOnLoad]
    internal static class JEEWindowGroupTabMenu
    {
        private const string Menu = "Window Groups/";

        static JEEWindowGroupTabMenu()
        {
            JEEDockAdapter.PopulateTabMenu += Populate;
        }

        private static void Populate(GenericMenu menu, EditorWindow window)
        {
            if (JEEDockAdapter.UnavailableReason != null || !window
                || window is JEEWindowGroup || window is JEEWindowGroupParking) return;
            var dock = JEEDockAdapter.Dock(window);
            if (!JEEDockAdapter.IsOrdinaryDock(dock)) return;

            var owner = JEEWindowGroups.FindGroup(window);
            if (owner)
            {
                menu.AddItem(new GUIContent(Menu + "Settings…"), false, JEEWindowGroupSettings.Open);
                menu.AddItem(new GUIContent(Menu + "Remove from Group"), false,
                    () => Schedule(window, dock, () =>
                    {
                        if (owner && JEEWindowGroups.FindGroup(window) == owner) JEEWindowGroups.RemoveMember(owner, window);
                    }));
            }
            else
            {
                menu.AddItem(new GUIContent(Menu + "New Group"), false,
                    () => Schedule(window, dock, () =>
                    {
                        if (JEEWindowGroups.IsEligible(window))
                            JEEWindowGroups.Create(new[] { window }, window.titleContent.text);
                    }));
                var groups = JEEDockAdapter.Tabs(dock).OfType<JEEWindowGroup>().Where(g => !g.disposing).ToArray();
                if (groups.Length == 0) menu.AddDisabledItem(new GUIContent(Menu + "Add to Group (No Groups in This Dock)"));
                foreach (var group in groups)
                {
                    // Names may contain '/' or repeat; keep one distinct menu item per group.
                    var label = group.groupName.Replace('/', '∕') + " (#" + group.GetInstanceID() + ")";
                    menu.AddItem(new GUIContent(Menu + "Add to Group/" + label), false,
                        () => Schedule(window, dock, () =>
                        {
                            if (group && !group.disposing && JEEDockAdapter.Dock(group) == dock && JEEWindowGroups.IsEligible(window))
                                JEEWindowGroups.AddMembers(group, new[] { window });
                        }));
                }
            }
            menu.AddSeparator("");
        }

        private static void Schedule(EditorWindow window, ScriptableObject dock, Action action)
        {
            EditorApplication.delayCall += () =>
            {
                // A menu can outlive the clicked window or its original dock.
                if (!window || !dock || JEEDockAdapter.Dock(window) != dock || !JEEDockAdapter.IsOrdinaryDock(dock)
                    || JEEDockAdapter.UnavailableReason != null) return;
                try { action(); }
                catch (Exception ex) { EditorUtility.DisplayDialog("Window Groups", ex.GetBaseException().Message, "OK"); }
            };
        }
    }
}
