using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace JustEnoughEditor
{
    [InitializeOnLoad]
    internal static class JEEWindowGroups
    {
        private static readonly HashSet<JEEWindowGroup> Groups = new HashSet<JEEWindowGroup>();
        private static int mutationDepth;
        private static bool shuttingDown;
        private static bool attemptedCompatibilityRecovery;
        private static EditorWindow maximizedWindow;
        private static double nextReconcile;
        internal static bool IsMutating => mutationDepth != 0;

        static JEEWindowGroups()
        {
            JEEDockAdapter.Activated += Activated;
            EditorApplication.update += Update;
            EditorApplication.quitting += () => shuttingDown = true;
            AssemblyReloadEvents.beforeAssemblyReload += () => shuttingDown = true;
            EditorApplication.delayCall += () =>
            {
                maximizedWindow = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.maximized);
                foreach (var group in Resources.FindObjectsOfTypeAll<JEEWindowGroup>()) Register(group);
                SafeReconcile();
            };
        }
        internal static void Register(JEEWindowGroup group) { if (group) Groups.Add(group); }
        internal static JEEWindowGroup FindGroup(EditorWindow member) => Groups.FirstOrDefault(g => g && !g.disposing && g.members.Contains(member));
        internal static bool IsEligible(EditorWindow window) => window && !(window is JEEWindowGroup) && !(window is JEEWindowGroupParking)
            && JEEDockAdapter.IsOrdinaryDock(JEEDockAdapter.Dock(window)) && !FindGroup(window);

        internal static JEEWindowGroup Create(IReadOnlyList<EditorWindow> windows, string name)
        {
            JEEDockAdapter.RequireSupport();
            if (windows == null || windows.Count == 0) throw new ArgumentException("Select at least one window.");
            var dock = JEEDockAdapter.Dock(windows[0]);
            if (!JEEDockAdapter.IsOrdinaryDock(dock) || windows.Distinct().Count() != windows.Count
                || windows.Any(w => !IsEligible(w) || JEEDockAdapter.Dock(w) != dock))
                throw new ArgumentException("Select ungrouped windows from the same ordinary dock.");
            var tabs = JEEDockAdapter.Tabs(dock);
            var ordered = tabs.Where(w => windows.Contains(w)).ToList();
            var group = ScriptableObject.CreateInstance<JEEWindowGroup>();
            group.groupId = Guid.NewGuid().ToString("N");
            group.groupName = string.IsNullOrWhiteSpace(name) ? "Window Group" : name.Trim();
            var usedColors = tabs.OfType<JEEWindowGroup>().Select(g => g.groupColor).ToArray();
            group.groupColor = Enumerable.Range(0, usedColors.Length + 1)
                .Select(i => Color.HSVToRGB(Mathf.Repeat(0.58f + i * 0.618034f, 1f), 0.55f, 0.9f))
                .First(color => !usedColors.Contains(color));
            group.members = ordered;
            group.ownerDock = dock;
            mutationDepth++;
            try
            {
                JEEDockAdapter.Move(group, dock, Array.IndexOf(tabs, ordered[0]));
                Arrange(group);
                JEEDockAdapter.Select(dock, group);
                group.RefreshTitle();
                return group;
            }
            catch
            {
                group.disposing = true;
                group.Close();
                JEEDockAdapter.Order(dock, tabs);
                throw;
            }
            finally { mutationDepth--; }
        }

        internal static void SetCollapsed(JEEWindowGroup group, bool value)
        {
            JEEDockAdapter.RequireSupport();
            if (!group || group.disposing) return;
            var dock = JEEDockAdapter.Dock(group);
            if (!JEEDockAdapter.IsOrdinaryDock(dock)) throw new InvalidOperationException("Restore the group to an ordinary dock before changing it.");
            mutationDepth++;
            var selected = JEEDockAdapter.Active(dock);
            try
            {
                group.ownerDock = dock;
                if (value)
                {
                    if (group.collapsed && group.parking) return;
                    group.parking = JEEDockAdapter.CreateParking(dock, group);
                    foreach (var member in group.members.Where(w => w)) JEEDockAdapter.Move(member, group.parking);
                    JEEDockAdapter.Idle(group.parking);
                    group.collapsed = true;
                    JEEDockAdapter.Select(dock, group.members.Contains(selected) ? group : selected);
                }
                else
                {
                    foreach (var member in group.members.Where(w => w && JEEDockAdapter.Dock(w) == group.parking).ToArray())
                        JEEDockAdapter.Move(member, dock);
                    JEEDockAdapter.DestroyParking(group.parking);
                    group.parking = null;
                    group.collapsed = false;
                    Arrange(group);
                    JEEDockAdapter.Select(dock, selected);
                }
                group.RefreshTitle();
            }
            catch
            {
                // Recover a partially completed collapse without destroying live members.
                foreach (var member in group.members.Where(w => w && JEEDockAdapter.Dock(w) == group.parking).ToArray())
                    JEEDockAdapter.Move(member, dock);
                JEEDockAdapter.DestroyParking(group.parking);
                group.parking = null;
                group.collapsed = false;
                Arrange(group);
                JEEDockAdapter.Select(dock, selected);
                throw;
            }
            finally { mutationDepth--; }
            if (JEEDockAdapter.Active(dock) == group) JEEDockAdapter.KeepTabVisible(group);
        }
        private static void Arrange(JEEWindowGroup group)
        {
            var dock = JEEDockAdapter.Dock(group);
            if (!dock || group.collapsed) return;
            var members = group.members.Where(w => w && JEEDockAdapter.Dock(w) == dock).ToArray();
            var order = JEEDockAdapter.Tabs(dock).Where(w => !members.Contains(w)).ToList();
            var index = order.IndexOf(group);
            if (index < 0) return;
            order.InsertRange(index + 1, members);
            JEEDockAdapter.Order(dock, order.ToArray());
        }
        internal static void Ungroup(JEEWindowGroup group)
        {
            if (!group || group.disposing) return;
            SetCollapsed(group, false);
            group.disposing = true;
            Groups.Remove(group);
            group.members.Clear();
            group.Close();
        }
        internal static void RemoveMember(JEEWindowGroup group, EditorWindow member)
        {
            if (!group || !group.members.Contains(member)) return;
            mutationDepth++;
            try
            {
                var dock = JEEDockAdapter.Dock(group);
                if (member && JEEDockAdapter.Dock(member) == group.parking)
                    JEEDockAdapter.Move(member, dock, Array.IndexOf(JEEDockAdapter.Tabs(dock), group) + 1);
                group.members.Remove(member);
                if (group.members.Count == 0) Ungroup(group);
                else { Arrange(group); group.RefreshTitle(); }
            }
            finally { mutationDepth--; }
        }
        internal static void AddMembers(JEEWindowGroup group, IReadOnlyList<EditorWindow> windows)
        {
            JEEDockAdapter.RequireSupport();
            if (!group || group.disposing || windows == null || windows.Count == 0) throw new ArgumentException("Select at least one window.");
            var dock = JEEDockAdapter.Dock(group);
            if (!JEEDockAdapter.IsOrdinaryDock(dock) || windows.Distinct().Count() != windows.Count
                || windows.Any(w => !IsEligible(w) || JEEDockAdapter.Dock(w) != dock))
                throw new ArgumentException("Select ungrouped windows from this group's dock.");
            var additions = JEEDockAdapter.Tabs(dock).Where(w => windows.Contains(w)).ToArray();
            var wasCollapsed = group.collapsed;
            mutationDepth++;
            try
            {
                SetCollapsed(group, false);
                group.members.AddRange(additions);
                Arrange(group);
                SetCollapsed(group, wasCollapsed);
                group.RefreshTitle();
            }
            catch
            {
                SetCollapsed(group, false);
                group.members.RemoveAll(w => additions.Contains(w));
                Arrange(group);
                throw;
            }
            finally { mutationDepth--; }
        }
        internal static void RestoreAll()
        {
            JEEDockAdapter.RequireRecoverySupport();
            foreach (var group in Resources.FindObjectsOfTypeAll<JEEWindowGroup>())
            {
                if (group.maximized) group.maximized = false;
                RestoreMembers(group);
                group.disposing = true;
                Groups.Remove(group);
                group.members.Clear();
                group.Close();
            }
        }
        private static void RestoreMembers(JEEWindowGroup group)
        {
            JEEDockAdapter.RequireRecoverySupport();
            var dock = JEEDockAdapter.Dock(group);
            if (!dock) dock = group.ownerDock;
            if (!dock || JEEDockAdapter.IsDestroying(dock)) throw new InvalidOperationException("The group's original dock is no longer available.");
            mutationDepth++;
            try
            {
                var index = Array.IndexOf(JEEDockAdapter.Tabs(dock), group) + 1;
                foreach (var member in group.members.Where(w => w).ToArray())
                {
                    if (JEEDockAdapter.Dock(member) == group.parking || !JEEDockAdapter.Parent(member))
                        JEEDockAdapter.Move(member, dock, index);
                    index++;
                }
                JEEDockAdapter.DestroyParking(group.parking);
                group.parking = null;
                group.collapsed = false;
                if (group.transferPlaceholder)
                {
                    group.transferPlaceholder.Close();
                    group.transferPlaceholder = null;
                }
                group.RefreshTitle();
            }
            finally { mutationDepth--; }
        }
        private static void Activated(ScriptableObject host, EditorWindow member)
        {
            if (IsMutating || shuttingDown || !member || member is JEEWindowGroupParking) return;
            if (member.maximized) { maximizedWindow = member; return; }
            if (member == maximizedWindow && JEEDockAdapter.IsDock(host))
            {
                // Native unmaximize inserts the surviving window immediately before
                // its serialized copy, then removes that copy after this callback.
                var panes = JEEDockAdapter.Tabs(host);
                var copyIndex = Array.IndexOf(panes, member) + 1;
                if (copyIndex < panes.Length)
                {
                    var copy = panes[copyIndex];
                    if (member is JEEWindowGroup live && copy is JEEWindowGroup saved && live.groupId == saved.groupId)
                    {
                        live.members = new List<EditorWindow>(saved.members);
                        live.parking = saved.parking;
                        live.ownerDock = host;
                        live.collapsed = saved.collapsed;
                        foreach (var idle in JEEDockAdapter.Tabs(live.parking).OfType<JEEWindowGroupParking>()) idle.owner = live;
                        saved.disposing = true;
                        Groups.Remove(saved);
                        saved.members.Clear();
                        saved.parking = null;
                    }
                    else
                    {
                        var owner = FindGroup(copy);
                        if (owner) owner.members[owner.members.IndexOf(copy)] = member;
                    }
                }
                maximizedWindow = null;
            }
            var group = Groups.FirstOrDefault(g => g && g.parking == host && g.members.Contains(member));
            if (!group || !JEEDockAdapter.IsOrdinaryDock(JEEDockAdapter.Dock(group))) return;
            try { SetCollapsed(group, false); member.Focus(); }
            catch (Exception ex) { Report(group, ex); }
        }
        internal static void BeforeHeadingRemoved(JEEWindowGroup group)
        {
            if (IsMutating || shuttingDown || !group || group.disposing) return;
            // Close() removes the heading before OnDestroy. Retain the source dock
            // so KillIfEmpty cannot destroy its parked members with the heading.
            // Keep a placeholder until native removal has completed; switching real
            // tabs inside this callback corrupts Unity's render tree during dragging.
            try
            {
                group.detachActive = JEEDockAdapter.Active(JEEDockAdapter.Dock(group));
                group.detachOrder = JEEDockAdapter.Tabs(JEEDockAdapter.Dock(group));
                if (group.collapsed && group.parking && !group.transferPlaceholder)
                    group.transferPlaceholder = JEEDockAdapter.KeepDockAlive(group.ownerDock, group);
            }
            catch (Exception ex)
            {
                Report(group, ex);
            }
        }
        internal static void HeadingAdded(JEEWindowGroup group)
        {
            if (IsMutating || shuttingDown || !group || group.disposing || group.members.Count == 0) return;
            // OnAddedAsTab runs inside Unity's insertion/render-tree transition.
            // Moving other panes here can corrupt the UI Toolkit render tree.
            EditorApplication.delayCall -= SafeReconcile;
            EditorApplication.delayCall += SafeReconcile;
        }
        internal static void HeadingDestroyed(JEEWindowGroup group)
        {
            Groups.Remove(group);
            if (shuttingDown || group.disposing) return;
            var dock = group.ownerDock;
            if (!dock || JEEDockAdapter.IsDestroying(dock)) return;
            var members = group.members.ToArray();
            var parking = group.parking;
            var placeholder = group.transferPlaceholder;
            var index = group.detachOrder == null ? JEEDockAdapter.Tabs(dock).Length : Array.IndexOf(group.detachOrder, group);
            if (Event.current != null)
                EditorApplication.delayCall += () => RestoreClosedGroup(dock, members, parking, placeholder, index);
            else RestoreClosedGroup(dock, members, parking, placeholder, index);
        }
        private static void RestoreClosedGroup(ScriptableObject dock, EditorWindow[] members, ScriptableObject parking,
            JEEWindowGroupParking placeholder, int index)
        {
            if (shuttingDown || !dock || JEEDockAdapter.IsDestroying(dock)) return;
            mutationDepth++;
            try
            {
                foreach (var member in members.Where(w => w && JEEDockAdapter.Dock(w) == parking))
                    JEEDockAdapter.Move(member, dock, index++);
                JEEDockAdapter.DestroyParking(parking);
                if (placeholder) placeholder.Close();
            }
            catch (Exception ex) { Debug.LogException(ex); }
            finally { mutationDepth--; }
        }
        private static void Update()
        {
            if (EditorApplication.timeSinceStartup < nextReconcile) return;
            nextReconcile = EditorApplication.timeSinceStartup + 0.25;
            SafeReconcile();
        }
        private static void SafeReconcile()
        {
            if (IsMutating || shuttingDown || EditorApplication.isCompiling) return;
            if (JEEDockAdapter.UnavailableReason != null)
            {
                if (!attemptedCompatibilityRecovery)
                {
                    attemptedCompatibilityRecovery = true;
                    try { RestoreAll(); }
                    catch (Exception ex) { Debug.LogWarning("Window Groups recovery: " + ex.GetBaseException().Message); }
                }
                return;
            }
            foreach (var group in Groups.ToArray())
            {
                if (!group) { Groups.Remove(group); continue; }
                try { ReconcileGroup(group); }
                catch (Exception ex) { Report(group, ex); }
            }
        }
        internal static void Reconcile()
        {
            SafeReconcile();
        }
        private static void ReconcileGroup(JEEWindowGroup group)
        {
            if (group.disposing) return;
            var dock = JEEDockAdapter.Dock(group);
            if (!dock || !JEEDockAdapter.IsOrdinaryDock(dock) || JEEDockAdapter.IsDestroying(dock)) return;
            mutationDepth++;
            try
            {
                var oldDock = group.ownerDock;
                if (oldDock && oldDock != dock)
                {
                    var moved = new List<EditorWindow>();
                    var oldOrder = group.detachOrder;
                    var oldActive = group.detachActive;
                    var destinationActive = JEEDockAdapter.Active(dock);
                    try
                    {
                        JEEDockAdapter.ReparentParking(group.parking, dock);
                        foreach (var member in group.members.Where(w => w && JEEDockAdapter.Dock(w) == oldDock))
                        {
                            JEEDockAdapter.Move(member, dock);
                            moved.Add(member);
                        }
                    }
                    catch
                    {
                        foreach (var member in moved) JEEDockAdapter.Move(member, oldDock);
                        JEEDockAdapter.ReparentParking(group.parking, oldDock);
                        JEEDockAdapter.Move(group, oldDock, oldOrder == null ? -1 : Array.IndexOf(oldOrder, group));
                        if (oldOrder != null && oldOrder.Length == JEEDockAdapter.Tabs(oldDock).Length)
                            JEEDockAdapter.Order(oldDock, oldOrder);
                        JEEDockAdapter.Select(oldDock, oldActive);
                        JEEDockAdapter.Select(dock, destinationActive);
                        throw;
                    }
                    group.ownerDock = dock;
                    group.detachOrder = null;
                    group.detachActive = null;
                    JEEDockAdapter.CloseEmptyDock(oldDock);
                }
                else group.ownerDock = dock;
                if (group.transferPlaceholder)
                {
                    group.transferPlaceholder.Close();
                    group.transferPlaceholder = null;
                }
                var missing = group.members.RemoveAll(w => !w);
                if (missing > 0) group.ShowNotification(new GUIContent("Some group windows are no longer available."));
                var memberDock = group.collapsed && group.parking ? group.parking : dock;
                group.members.RemoveAll(w => JEEDockAdapter.Dock(w) && JEEDockAdapter.Dock(w) != memberDock);
                if (group.members.Count == 0 && !string.IsNullOrEmpty(group.groupId)) { Ungroup(group); return; }
                if (!group.collapsed)
                {
                    var order = JEEDockAdapter.Tabs(dock).Where(w => group.members.Contains(w)).ToList();
                    if (order.Count == group.members.Count) group.members = order;
                    Arrange(group);
                }
                group.RefreshTitle();
            }
            finally { mutationDepth--; }
        }
        private static void Report(JEEWindowGroup group, Exception ex)
        {
            var message = ex.GetBaseException().Message;
            if (group)
            {
                if (group.lastError == message) return;
                group.lastError = message;
                group.ShowNotification(new GUIContent(message));
            }
            Debug.LogError("Window Groups: " + message);
        }
    }
}
