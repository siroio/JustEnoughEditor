using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace JustEnoughEditor
{
    public class JEEWindowGroup : EditorWindow, IHasCustomMenu
    {
        [SerializeField] internal string groupId;
        [SerializeField] internal string groupName;
        [SerializeField] internal Color groupColor;
        [SerializeField] internal List<EditorWindow> members = new List<EditorWindow>();
        [SerializeField] internal bool collapsed;
        [SerializeField] internal ScriptableObject parking;
        [SerializeField] internal ScriptableObject ownerDock;
        [SerializeField] internal JEEWindowGroupParking transferPlaceholder;
        [System.NonSerialized] internal bool disposing;
        [NonSerialized] internal EditorWindow[] detachOrder;
        [NonSerialized] internal EditorWindow detachActive;
        [NonSerialized] internal string lastError;
        [NonSerialized] private Texture2D colorIcon;
        [NonSerialized] private Color iconColor;
        [NonSerialized] private IMGUIContainer tabIndicator;

        private void OnEnable()
        {
            minSize = new Vector2(180, 100);
            JEEWindowGroups.Register(this);
            RefreshTitle();
        }
        private void OnDestroy()
        {
            JEEWindowGroups.HeadingDestroyed(this);
            if (colorIcon) DestroyImmediate(colorIcon);
        }
        private void OnDisable()
        {
            tabIndicator?.RemoveFromHierarchy();
            tabIndicator?.Dispose();
            tabIndicator = null;
        }
        private void OnBeforeRemovedAsTab() { JEEWindowGroups.BeforeHeadingRemoved(this); }
        private void OnAddedAsTab() { JEEWindowGroups.HeadingAdded(this); }

        internal void RefreshTitle()
        {
            JEEDockAdapter.UpdateGroupIndicator(this, ref tabIndicator);
            if (!colorIcon)
            {
                colorIcon = new Texture2D(16, 16, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                iconColor = new Color(-1, -1, -1, -1);
            }
            if (iconColor != groupColor)
            {
                iconColor = groupColor;
                var pixels = new Color[256];
                for (var y = 3; y < 13; y++)
                    for (var x = 3; x < 13; x++) pixels[y * 16 + x] = new Color(groupColor.r, groupColor.g, groupColor.b, 1);
                colorIcon.SetPixels(pixels);
                colorIcon.Apply();
            }
            var title = (collapsed ? "> " : "v ") + (string.IsNullOrWhiteSpace(groupName) ? "Window Group" : groupName)
                + " (" + members.Count(w => w) + ")";
            if (titleContent.text == title && titleContent.image == colorIcon) return;
            titleContent = new GUIContent(title, colorIcon, "Right-click to expand, collapse, add tabs or open Project Settings.");
            Repaint();
        }

        private void OnGUI()
        {
            if (JEEDockAdapter.UnavailableReason != null)
            {
                EditorGUILayout.HelpBox(JEEDockAdapter.UnavailableReason, MessageType.Warning);
                return;
            }
            if (maximized)
            {
                EditorGUILayout.HelpBox("Restore this view to manage the group's tabs.", MessageType.Info);
                if (GUILayout.Button("Restore Group View")) Schedule(() => maximized = false);
                return;
            }
            GUILayout.Space(8);
            EditorGUILayout.LabelField(groupName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(members.Count(w => w) + " tabs · " + (collapsed ? "Collapsed" : "Expanded"));
            EditorGUILayout.LabelField("Right-click the heading for group actions. Edit names and tabs in Project Settings > Just Enough Editor > Window Groups.", EditorStyles.wordWrappedMiniLabel);
            if (!string.IsNullOrEmpty(lastError)) EditorGUILayout.HelpBox(lastError, MessageType.Warning);
        }
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
        public void AddItemsToMenu(GenericMenu menu)
        {
            if (maximized)
            {
                menu.AddItem(new GUIContent("Restore Group View"), false, () => Schedule(() => maximized = false));
                return;
            }
            menu.AddItem(new GUIContent(collapsed ? "Expand Group" : "Collapse Group"), false,
                () => Schedule(() => JEEWindowGroups.SetCollapsed(this, !collapsed)));
            AddTabItems(menu, "Add Tab/");
            menu.AddItem(new GUIContent("Settings…"), false, JEEWindowGroupSettings.Open);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Ungroup (Keep Windows)"), false, () => Schedule(() => JEEWindowGroups.Ungroup(this)));
        }

        internal void AddTabItems(GenericMenu menu, string prefix)
        {
            var dock = JEEDockAdapter.Dock(this);
            var available = JEEDockAdapter.Tabs(dock).Where(JEEWindowGroups.IsEligible).ToArray();
            if (available.Length == 0) menu.AddDisabledItem(new GUIContent(prefix + "No Ungrouped Tabs in This Dock"));
            foreach (var window in available)
            {
                var label = window.titleContent.text.Replace('/', '∕') + " (#" + window.GetInstanceID() + ")";
                menu.AddItem(new GUIContent(prefix + label, window.titleContent.image), false, () => Schedule(() =>
                {
                    if (JEEDockAdapter.Dock(this) == dock && JEEDockAdapter.Dock(window) == dock && JEEWindowGroups.IsEligible(window))
                        JEEWindowGroups.AddMembers(this, new[] { window });
                }));
            }
        }
    }
}
