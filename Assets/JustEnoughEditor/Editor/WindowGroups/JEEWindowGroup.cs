using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace JustEnoughEditor
{
    // Serialized with the native layout in the offscreen parking dock, never a visible heading.
    // Keep this type and its serialized fields so existing layouts can be migrated in place.
    public class JEEWindowGroup : EditorWindow
    {
        [SerializeField] internal string groupId;
        [SerializeField] internal string groupName;
        [SerializeField] internal Color groupColor;
        [SerializeField] internal List<EditorWindow> members = new List<EditorWindow>();
        [SerializeField] internal bool collapsed;
        [SerializeField] internal ScriptableObject parking;
        [SerializeField] internal ScriptableObject ownerDock;
        [SerializeField] internal JEEWindowGroupParking transferPlaceholder;
        [SerializeField] internal EditorWindow lastActiveMember;
        [SerializeField] internal EditorWindow labeledMember;
        [SerializeField] internal GUIContent originalTitle;
        [System.NonSerialized] internal bool disposing;
        [NonSerialized] internal string lastError;
        [NonSerialized] private IMGUIContainer tabIndicator;

        private void OnEnable()
        {
            JEEWindowGroups.Register(this);
        }
        private void OnDestroy()
        {
            RestoreTitle();
            JEEWindowGroups.GroupDestroyed(this);
        }
        private void OnDisable()
        {
            tabIndicator?.RemoveFromHierarchy();
            tabIndicator?.Dispose();
            tabIndicator = null;
            foreach (var member in members.Where(w => w))
                member.rootVisualElement.UnregisterCallback<DetachFromPanelEvent>(MemberDetached);
        }

        private void MemberDetached(DetachFromPanelEvent evt)
        {
            if (members.Any(w => w && ReferenceEquals(w.rootVisualElement, evt.target)))
                JEEWindowGroups.BeforeMemberDetached(this);
        }
        internal void UnwatchMember(EditorWindow member)
        {
            if (member) member.rootVisualElement.UnregisterCallback<DetachFromPanelEvent>(MemberDetached);
        }

        internal void RefreshTitle()
        {
            JEEDockAdapter.UpdateGroupIndicator(this, ref tabIndicator);
            foreach (var member in members.Where(w => w))
                member.rootVisualElement.RegisterCallback<DetachFromPanelEvent>(MemberDetached);
            if (labeledMember && (!collapsed || labeledMember != lastActiveMember)) RestoreTitle();
            if (collapsed && lastActiveMember)
            {
                if (!labeledMember)
                {
                    labeledMember = lastActiveMember;
                    originalTitle = new GUIContent(labeledMember.titleContent);
                }
                var label = groupName + " (" + members.Count(w => w) + ")";
                if (labeledMember.titleContent.text != label)
                    labeledMember.titleContent = new GUIContent(label, originalTitle.image,
                        originalTitle.text + " · " + groupName + " — Right-click for Window Groups.");
            }
        }
        internal void RestoreTitle()
        {
            if (labeledMember && originalTitle != null) labeledMember.titleContent = new GUIContent(originalTitle);
            labeledMember = null;
            originalTitle = null;
        }

        internal GUIContent MemberTitle(EditorWindow member) => member == labeledMember && originalTitle != null
            ? originalTitle : member.titleContent;
        internal void Schedule(Action action)
        {
            EditorApplication.delayCall += () =>
            {
                if (!this || disposing) return;
                try { lastError = null; action(); }
                catch (Exception ex) { lastError = ex.GetBaseException().Message; Repaint(); }
                SettingsService.RepaintAllSettingsWindow();
            };
        }
        internal void AddItemsToMenu(GenericMenu menu, string prefix)
        {
            menu.AddItem(new GUIContent(prefix + (collapsed ? "Expand Group" : "Collapse Group")), false,
                () => Schedule(() => JEEWindowGroups.SetCollapsed(this, !collapsed)));
            AddTabItems(menu, prefix + "Add Tab/");
            menu.AddItem(new GUIContent(prefix + "Settings…"), false, JEEWindowGroupSettings.Open);
            menu.AddItem(new GUIContent(prefix + "Ungroup (Keep Windows)"), false, () => Schedule(() => JEEWindowGroups.Ungroup(this)));
        }

        internal void AddTabItems(GenericMenu menu, string prefix)
        {
            var dock = ownerDock;
            var available = JEEDockAdapter.Tabs(dock).Where(JEEWindowGroups.IsEligible).ToArray();
            if (available.Length == 0) menu.AddDisabledItem(new GUIContent(prefix + "No Ungrouped Tabs in This Dock"));
            foreach (var window in available)
            {
                var label = window.titleContent.text.Replace('/', '∕') + " (#" + window.GetInstanceID() + ")";
                menu.AddItem(new GUIContent(prefix + label, window.titleContent.image), false, () => Schedule(() =>
                {
                    if (ownerDock == dock && JEEDockAdapter.Dock(window) == dock && JEEWindowGroups.IsEligible(window))
                        JEEWindowGroups.AddMembers(this, new[] { window });
                }));
            }
        }
    }
}
