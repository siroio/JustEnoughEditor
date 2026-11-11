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
        private static bool shuttingDown, attemptedCompatibilityRecovery;
        private static EditorWindow maximizedWindow, lastFocusedWindow;
        private static double nextReconcile;
        internal static bool IsMutating => mutationDepth != 0;
        internal static IEnumerable<JEEWindowGroup> InDock(ScriptableObject dock) =>
            Groups.Where(g => g && !g.disposing && g.ownerDock == dock);

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
                Reconcile();
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
            var group = ScriptableObject.CreateInstance<JEEWindowGroup>();
            group.groupId = Guid.NewGuid().ToString("N");
            group.groupName = string.IsNullOrWhiteSpace(name) ? "Window Group" : name.Trim();
            var usedColors = InDock(dock).Where(g => g != group).Select(g => g.groupColor).ToArray();
            group.groupColor = Enumerable.Range(0, usedColors.Length + 1)
                .Select(i => Color.HSVToRGB(Mathf.Repeat(0.58f + i * 0.618034f, 1f), 0.55f, 0.9f))
                .First(color => !usedColors.Contains(color));
            group.members = tabs.Where(w => windows.Contains(w)).ToList();
            group.lastActiveMember = windows.Contains(JEEDockAdapter.Active(dock)) ? JEEDockAdapter.Active(dock) : windows[0];
            group.ownerDock = dock;
            mutationDepth++;
            try
            {
                EnsureStorage(group);
                Arrange(group);
                JEEDockAdapter.Select(dock, group.lastActiveMember);
                group.RefreshTitle();
                return group;
            }
            catch { Ungroup(group); JEEDockAdapter.Order(dock, tabs); throw; }
            finally { mutationDepth--; }
        }

        private static void EnsureStorage(JEEWindowGroup group)
        {
            if (!group.parking) group.parking = JEEDockAdapter.CreateParking(group.ownerDock, group);
            // Migrate old layout headings into the offscreen host without recreating members.
            if (JEEDockAdapter.Dock(group) != group.parking)
            {
                if (!group.lastActiveMember || !group.members.Contains(group.lastActiveMember))
                    group.lastActiveMember = group.members.FirstOrDefault(w => w);
                if (group.collapsed && group.lastActiveMember && JEEDockAdapter.Dock(group.lastActiveMember) == group.parking)
                    JEEDockAdapter.Move(group.lastActiveMember, group.ownerDock,
                        Array.IndexOf(JEEDockAdapter.Tabs(group.ownerDock), group));
                var selected = JEEDockAdapter.Active(group.ownerDock);
                JEEDockAdapter.Move(group, group.parking);
                JEEDockAdapter.Idle(group.parking);
                JEEDockAdapter.Select(group.ownerDock, selected == group ? group.lastActiveMember : selected);
            }
        }

        internal static void SetCollapsed(JEEWindowGroup group, bool value)
        {
            JEEDockAdapter.RequireSupport();
            if (!group || group.disposing) return;
            var dock = group.ownerDock;
            if (!JEEDockAdapter.IsOrdinaryDock(dock) || group.members.Any(w => w && w.maximized))
                throw new InvalidOperationException("Restore the group to an ordinary dock before changing it.");
            mutationDepth++;
            var selected = JEEDockAdapter.Active(dock);
            try
            {
                EnsureStorage(group);
                if (group.members.Contains(selected)) group.lastActiveMember = selected;
                if (!group.lastActiveMember || !group.members.Contains(group.lastActiveMember))
                    group.lastActiveMember = group.members.FirstOrDefault(w => w);
                group.RestoreTitle();
                if (value)
                {
                    if (group.lastActiveMember && JEEDockAdapter.Dock(group.lastActiveMember) == group.parking)
                        JEEDockAdapter.Move(group.lastActiveMember, dock);
                    foreach (var member in group.members.Where(w => w && w != group.lastActiveMember && JEEDockAdapter.Dock(w) == dock))
                        JEEDockAdapter.Move(member, group.parking);
                    JEEDockAdapter.Idle(group.parking);
                    group.collapsed = true;
                    JEEDockAdapter.Select(dock, group.members.Contains(selected) ? group.lastActiveMember : selected);
                }
                else
                {
                    RestoreMembers(group);
                    Arrange(group);
                    JEEDockAdapter.Select(dock, selected);
                }
                group.RefreshTitle();
                JEEDockAdapter.KeepTabVisible(JEEDockAdapter.Active(dock));
            }
            catch
            {
                RestoreMembers(group);
                Arrange(group);
                JEEDockAdapter.Select(dock, selected);
                throw;
            }
            finally { mutationDepth--; }
        }
        private static void Arrange(JEEWindowGroup group)
        {
            if (group.collapsed) return;
            var tabs = JEEDockAdapter.Tabs(group.ownerDock);
            var members = group.members.Where(w => w && JEEDockAdapter.Dock(w) == group.ownerDock).ToArray();
            var index = Array.FindIndex(tabs, w => members.Contains(w));
            if (index < 0) return;
            var order = tabs.Where(w => !members.Contains(w)).ToList();
            order.InsertRange(Math.Min(index, order.Count), members);
            JEEDockAdapter.Order(group.ownerDock, order.ToArray());
        }
        private static void RestoreMembers(JEEWindowGroup group)
        {
            group.RestoreTitle();
            var dock = group.ownerDock;
            if (!dock || JEEDockAdapter.IsDestroying(dock)) return;
            var tabs = JEEDockAdapter.Tabs(dock);
            var index = Array.FindIndex(tabs, w => group.members.Contains(w));
            if (index < 0) index = tabs.Length;
            foreach (var member in group.members.Where(w => w).ToArray())
                if (JEEDockAdapter.Dock(member) == group.parking || !JEEDockAdapter.Parent(member))
                    JEEDockAdapter.Move(member, dock, index++);
            group.collapsed = false;
        }
        internal static void Ungroup(JEEWindowGroup group)
        {
            if (!group || group.disposing) return;
            mutationDepth++;
            try
            {
                RestoreMembers(group);
                Arrange(group);
                var parking = group.parking;
                var placeholder = group.transferPlaceholder;
                group.disposing = true;
                Groups.Remove(group);
                group.Close();
                JEEDockAdapter.DestroyParking(parking);
                if (placeholder) placeholder.Close();
            }
            finally { mutationDepth--; }
        }
        internal static void RemoveMember(JEEWindowGroup group, EditorWindow member)
        {
            if (!group || !group.members.Contains(member)) return;
            var collapsed = group.collapsed;
            SetCollapsed(group, false);
            group.UnwatchMember(member);
            group.members.Remove(member);
            if (group.members.Count == 0) Ungroup(group);
            else SetCollapsed(group, collapsed);
        }
        internal static void AddMembers(JEEWindowGroup group, IReadOnlyList<EditorWindow> windows)
        {
            JEEDockAdapter.RequireSupport();
            if (!group || group.disposing || windows == null || windows.Count == 0) throw new ArgumentException("Select at least one window.");
            var dock = group.ownerDock;
            if (!JEEDockAdapter.IsOrdinaryDock(dock) || windows.Distinct().Count() != windows.Count
                || windows.Any(w => !IsEligible(w) || JEEDockAdapter.Dock(w) != dock))
                throw new ArgumentException("Select ungrouped windows from this group's dock.");
            var additions = JEEDockAdapter.Tabs(dock).Where(w => windows.Contains(w)).ToArray();
            var collapsed = group.collapsed;
            mutationDepth++;
            try
            {
                SetCollapsed(group, false);
                group.members.AddRange(additions);
                Arrange(group);
                SetCollapsed(group, collapsed);
            }
            catch
            {
                RestoreMembers(group);
                group.members.RemoveAll(w => additions.Contains(w));
                Arrange(group);
                throw;
            }
            finally { mutationDepth--; }
        }
        internal static void RestoreAll()
        {
            JEEDockAdapter.RequireRecoverySupport();
            foreach (var group in Resources.FindObjectsOfTypeAll<JEEWindowGroup>()) Ungroup(group);
        }

        private static void Activated(ScriptableObject host, EditorWindow member)
        {
            if (IsMutating || shuttingDown || !member || member is JEEWindowGroupParking || member is JEEWindowGroup) return;
            if (member.maximized) { maximizedWindow = member; return; }
            if (member == maximizedWindow && JEEDockAdapter.IsDock(host))
            {
                var panes = JEEDockAdapter.Tabs(host);
                var index = Array.IndexOf(panes, member) + 1;
                if (index < panes.Length)
                {
                    var copy = panes[index];
                    var saved = FindGroup(copy);
                    if (saved)
                    {
                        saved.members[saved.members.IndexOf(copy)] = member;
                        if (saved.lastActiveMember == copy) saved.lastActiveMember = member;
                        if (saved.labeledMember == copy) saved.labeledMember = member;
                    }
                }
                maximizedWindow = null;
            }
            var group = FindGroup(member);
            if (!group) return;
            group.lastActiveMember = member;
            if (host != group.parking) return;
            try { SetCollapsed(group, false); member.Focus(); }
            catch (Exception ex) { Report(group, ex); }
        }
        internal static void BeforeMemberDetached(JEEWindowGroup group)
        {
            if (IsMutating || shuttingDown || !group || group.disposing || !group.ownerDock
                || JEEDockAdapter.IsDestroying(group.ownerDock) || group.transferPlaceholder) return;
            // DetachFromPanel precedes native RemoveTab/KillIfEmpty, even when sendEvents=false.
            // An inactive placeholder keeps parked members alive without switching render trees.
            var tabs = JEEDockAdapter.Tabs(group.ownerDock);
            if (tabs.Length == 1 && group.members.Contains(tabs[0]))
                group.transferPlaceholder = JEEDockAdapter.KeepDockAlive(group.ownerDock, group);
        }
        internal static void GroupDestroyed(JEEWindowGroup group)
        {
            Groups.Remove(group);
            if (shuttingDown || group.disposing || !group.ownerDock || JEEDockAdapter.IsDestroying(group.ownerDock)) return;
            mutationDepth++;
            try
            {
                RestoreMembers(group);
                Arrange(group);
                JEEDockAdapter.DestroyParking(group.parking);
                if (group.transferPlaceholder) group.transferPlaceholder.Close();
            }
            catch (Exception ex) { Debug.LogException(ex); }
            finally { mutationDepth--; }
        }
        private static void Update()
        {
            if (IsMutating || shuttingDown || EditorApplication.isCompiling || JEEDockAdapter.IsDragging) return;
            if (EditorApplication.timeSinceStartup >= nextReconcile)
            {
                nextReconcile = EditorApplication.timeSinceStartup + 0.25;
                Reconcile();
            }
            FollowFocus();
        }
        internal static void FollowFocus()
        {
            var focused = EditorWindow.focusedWindow;
            if (!focused || focused == lastFocusedWindow || focused is JEEWindowGroup || focused is JEEWindowGroupParking
                || !JEEDockAdapter.IsOrdinaryDock(JEEDockAdapter.Dock(focused)) || focused.maximized
                || focused.GetType().Name == "ProjectSettingsWindow" || focused.GetType().Name == "PreferencesWindow") return;
            lastFocusedWindow = focused;
            var activeGroup = FindGroup(focused);
            if (activeGroup) activeGroup.lastActiveMember = focused;
            if (!JEEWindowGroupSettings.AutoExpandCollapse) return;
            foreach (var group in Groups.Where(g => g && !g.disposing).ToArray())
            {
                if (group.collapsed == (group != activeGroup) || group.members.Any(w => w && w.maximized)) continue;
                try { SetCollapsed(group, group != activeGroup); }
                catch (Exception ex) { Report(group, ex); }
            }
            if (focused && EditorWindow.focusedWindow != focused) focused.Focus();
        }
        internal static void Reconcile()
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
                if (group.disposing || group.members.Any(w => w && w.maximized)) continue;
                mutationDepth++;
                try
                {
                    if (!JEEDockAdapter.IsOrdinaryDock(group.ownerDock)) continue;
                    EnsureStorage(group);
                    group.members.RemoveAll(w => !w);
                    var representative = group.labeledMember;
                    if (group.collapsed && representative && JEEDockAdapter.IsOrdinaryDock(JEEDockAdapter.Dock(representative))
                        && JEEDockAdapter.Dock(representative) != group.ownerDock && JEEDockAdapter.Dock(representative) != group.parking)
                    {
                        JEEDockAdapter.ReparentParking(group.parking, JEEDockAdapter.Dock(representative));
                        group.ownerDock = JEEDockAdapter.Dock(representative);
                    }
                    foreach (var member in group.members.Where(w => JEEDockAdapter.Dock(w)
                        && JEEDockAdapter.Dock(w) != group.ownerDock && JEEDockAdapter.Dock(w) != group.parking).ToArray())
                    {
                        group.UnwatchMember(member);
                        group.members.Remove(member);
                    }
                    if (group.members.Count == 0) { Ungroup(group); continue; }
                    if (group.collapsed && (!group.lastActiveMember || JEEDockAdapter.Dock(group.lastActiveMember) != group.ownerDock))
                    {
                        group.lastActiveMember = group.members.First();
                        JEEDockAdapter.Move(group.lastActiveMember, group.ownerDock);
                        JEEDockAdapter.Idle(group.parking);
                    }
                    if (!group.collapsed)
                    {
                        var order = JEEDockAdapter.Tabs(group.ownerDock).Where(group.members.Contains).ToList();
                        if (order.Count == group.members.Count) group.members = order;
                        Arrange(group);
                    }
                    if (group.transferPlaceholder) { group.transferPlaceholder.Close(); group.transferPlaceholder = null; }
                    group.RefreshTitle();
                }
                catch (Exception ex) { Report(group, ex); }
                finally { mutationDepth--; }
            }
        }
        private static void Report(JEEWindowGroup group, Exception ex)
        {
            var message = ex.GetBaseException().Message;
            if (group.lastError == message) return;
            group.lastError = message;
            Debug.LogError("Window Groups: " + message);
        }
    }
}
