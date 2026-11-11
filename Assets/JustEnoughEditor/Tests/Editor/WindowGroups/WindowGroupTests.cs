using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace JustEnoughEditor.Tests
{
    public class WindowGroupTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Type DockType = typeof(EditorWindow).Assembly.GetType("UnityEditor.DockArea");
        private static readonly FieldInfo Parent = typeof(EditorWindow).GetField("m_Parent", Flags);
        private readonly List<EditorWindow> owned = new List<EditorWindow>();
        private GroupTestWindow anchor, first, second;
        private object dock;
        private bool autoMode, hadAutoPreference;

        private static object Dock(EditorWindow window) => Parent.GetValue(window);
        private static EditorWindow[] Tabs(object target) => ((IEnumerable)DockType.GetField("m_Panes", Flags).GetValue(target)).Cast<EditorWindow>().ToArray();
        private static EditorWindow Active(object target) => (EditorWindow)DockType.GetProperty("actualView", Flags).GetValue(target);
        private static void Move(EditorWindow window, object target)
        {
            if (Dock(window) != null)
                DockType.GetMethod("RemoveTab", Flags, null, new[] { typeof(EditorWindow), typeof(bool), typeof(bool) }, null)
                    .Invoke(Dock(window), new object[] { window, false, true });
            DockType.GetMethod("AddTab", Flags, null, new[] { typeof(int), typeof(EditorWindow), typeof(bool) }, null)
                .Invoke(target, new object[] { Tabs(target).Length, window, true });
        }
        private GroupTestWindow Window(int value, bool show = false)
        {
            var window = ScriptableObject.CreateInstance<GroupTestWindow>();
            window.value = value;
            window.titleContent = new GUIContent("Same title");
            owned.Add(window);
            if (show) window.Show();
            return window;
        }
        private JEEWindowGroup Group(params EditorWindow[] windows)
        {
            var group = JEEWindowGroups.Create(windows, "Work");
            owned.Add(group);
            return group;
        }
        [SetUp]
        public void SetUp()
        {
            hadAutoPreference = EditorPrefs.HasKey(JEEWindowGroupSettings.AutoExpandCollapseKey);
            autoMode = JEEWindowGroupSettings.AutoExpandCollapse;
            JEEWindowGroupSettings.AutoExpandCollapse = false;
            anchor = Window(0, true); dock = Dock(anchor);
            first = Window(17); second = Window(29);
            Move(first, dock); Move(second, dock);
        }
        [TearDown]
        public void TearDown()
        {
            foreach (var group in owned.OfType<JEEWindowGroup>().Where(w => w).ToArray()) group.Close();
            foreach (var window in owned.Where(w => w).Reverse().ToArray()) window.Close();
            owned.Clear();
            if (hadAutoPreference) JEEWindowGroupSettings.AutoExpandCollapse = autoMode;
            else EditorPrefs.DeleteKey(JEEWindowGroupSettings.AutoExpandCollapseKey);
        }

        [Test]
        public void CreateKeepsDistinctSameTypeMembersAndNativeTabOrder()
        {
            var group = Group(second, first);
            CollectionAssert.AreEqual(new EditorWindow[] { first, second }, group.members);
            CollectionAssert.AreEqual(new EditorWindow[] { anchor, first, second }, Tabs(dock));
            Assert.That(group.groupId, Is.Not.Empty);
        }

        private static object[] NativeMenuItems(EditorWindow target)
        {
            var menu = new GenericMenu();
            Dock(target).GetType().GetMethod("AddDefaultItemsToMenu", Flags)
                .Invoke(Dock(target), new object[] { menu, target });
            return ((IEnumerable)typeof(GenericMenu).GetProperty("menuItems", Flags).GetValue(menu)).Cast<object>().ToArray();
        }
        private static string MenuLabel(object item) => ((GUIContent)item.GetType().GetField("content").GetValue(item)).text;
        private static void ClickMenuItem(object[] items, string label)
        {
            var item = items.SingleOrDefault(i => MenuLabel(i) == label);
            Assert.That(item, Is.Not.Null, "Native tab menu must contain " + label);
            var callback = item.GetType().GetField("func").GetValue(item) as GenericMenu.MenuFunction;
            var callbackWithData = item.GetType().GetField("func2").GetValue(item) as GenericMenu.MenuFunction2;
            Assert.That(callback != null || callbackWithData != null, Is.True, "Menu item must be enabled");
            if (callbackWithData != null) callbackWithData(item.GetType().GetField("userData").GetValue(item));
            else callback();
        }
        private static IEnumerator WaitForMenuAction()
        {
            var called = false;
            EditorApplication.delayCall += () => called = true;
            var deadline = EditorApplication.timeSinceStartup + 3;
            while (!called && EditorApplication.timeSinceStartup < deadline) yield return null;
            Assert.That(called, Is.True, "Deferred native menu actions must run on the editor loop");
        }

        private EditorWindow AddNativeInspector(object[] items, object destination)
        {
            var before = Resources.FindObjectsOfTypeAll<EditorWindow>();
            var inspectorType = typeof(EditorWindow).Assembly.GetType("UnityEditor.InspectorWindow");
            var item = items.Single(i => Equals(i.GetType().GetField("userData").GetValue(i), inspectorType));
            try { ClickMenuItem(items, MenuLabel(item)); }
            finally { owned.AddRange(Resources.FindObjectsOfTypeAll<EditorWindow>().Except(before)); }
            return Tabs(destination).Except(before).Single();
        }

        [UnityTest]
        public IEnumerator NativeAddTabJoinsClickedGroupAndShowsNewWindow()
        {
            var group = Group(first);
            var other = Group(second);
            foreach (var collapsed in new[] { false, true })
            {
                JEEWindowGroups.SetCollapsed(group, collapsed);
                second.Focus();
                var items = NativeMenuItems(group.lastActiveMember);
                yield return WaitForMenuAction();
                var added = AddNativeInspector(items, dock);
                yield return WaitForMenuAction();
                Assert.That(JEEWindowGroups.FindGroup(added), Is.SameAs(group), "Use the clicked tab's group, not the active tab's group");
                Assert.That(group.collapsed, Is.False, "Opening a new window must expose its content even in manual mode");
                Assert.That(Active(dock), Is.SameAs(added));
                Assert.That(EditorWindow.focusedWindow, Is.SameAs(added));
                CollectionAssert.AreEqual(new[] { second }, other.members);
                Assert.That(Tabs(dock).Any(w => w is JEEWindowGroup || w is JEEWindowGroupParking), Is.False);
            }
        }

        [UnityTest]
        public IEnumerator NativeAddTabOutsideGroupDoesNotInheritActiveGroup()
        {
            var group = Group(first, second);
            first.Focus();
            var items = NativeMenuItems(anchor);
            yield return WaitForMenuAction();
            var added = AddNativeInspector(items, dock);
            yield return WaitForMenuAction();
            Assert.That(JEEWindowGroups.FindGroup(added), Is.Null);
            CollectionAssert.AreEqual(new[] { first, second }, group.members);
        }

        [UnityTest]
        public IEnumerator NativeAddTabDoesNotUseGroupMovedAfterMenuOpened()
        {
            var group = Group(first);
            var items = NativeMenuItems(first);
            yield return WaitForMenuAction();
            var elsewhere = Window(93, true);
            Move(first, Dock(elsewhere));
            JEEWindowGroups.Reconcile();
            var added = AddNativeInspector(items, dock);
            yield return WaitForMenuAction();
            Assert.That(JEEWindowGroups.FindGroup(added), Is.Null);
            Assert.That(Dock(added), Is.SameAs(dock), "Preserve the native menu destination");
        }

        [UnityTest]
        public IEnumerator CancelledAddTabMenuDoesNotCaptureLaterWindow()
        {
            var group = Group(first);
            NativeMenuItems(first);
            yield return WaitForMenuAction();
            var added = Window(93);
            Move(added, dock);
            yield return WaitForMenuAction();
            Assert.That(JEEWindowGroups.FindGroup(added), Is.Null);
            CollectionAssert.AreEqual(new[] { first }, group.members);
        }

        [UnityTest]
        public IEnumerator NativeTabMenuCreatesGroupFromClickedTabInsteadOfFocusedTab()
        {
            second.Focus();
            ClickMenuItem(NativeMenuItems(first), "Window Groups/New Group");
            yield return WaitForMenuAction();
            Assert.That(Resources.FindObjectsOfTypeAll<EditorWindow>().Any(w => w.GetType().Name == "JEEWindowGroupSetup"), Is.False,
                "Creation must not open another window");
            var group = JEEWindowGroups.FindGroup(first);
            Assert.That(group, Is.Not.Null);
            owned.Add(group);
            CollectionAssert.AreEqual(new[] { first }, group.members);
            Assert.That(JEEWindowGroups.FindGroup(second), Is.Null);
            Assert.That(group.ownerDock, Is.SameAs(dock));
            CollectionAssert.DoesNotContain(Tabs(dock), group);
        }

        [UnityTest]
        public IEnumerator NativeTabMenuAddsExactTabToChosenSameDockGroup()
        {
            var one = Group(first);
            var two = Group(second);
            one.groupName = two.groupName = "Tools / Debug";
            JEEWindowGroups.SetCollapsed(one, true);
            JEEWindowGroups.SetCollapsed(two, true);
            var otherWindow = Window(40, true);
            var outside = Group(otherWindow);
            var items = NativeMenuItems(anchor);
            var additions = items.Where(i => MenuLabel(i).StartsWith("Window Groups/Add to Group/")).ToArray();
            Assert.That(additions.Length, Is.EqualTo(2), "Only groups in the clicked tab's dock belong in the menu");
            Assert.That(additions.Select(MenuLabel).Distinct().Count(), Is.EqualTo(2));
            Assert.That(additions.All(i => MenuLabel(i).Count(c => c == '/') == 2), Is.True, "Group names must not create submenus");
            ClickMenuItem(items, MenuLabel(additions.Single(i => MenuLabel(i).EndsWith("(#" + two.GetInstanceID() + ")"))));
            yield return WaitForMenuAction();
            Assert.That(two.collapsed, Is.True);
            CollectionAssert.AreEqual(new EditorWindow[] { second, anchor }, two.members);
            CollectionAssert.AreEqual(new[] { first }, one.members);
            CollectionAssert.AreEqual(new[] { otherWindow }, outside.members);
        }

        [UnityTest]
        public IEnumerator NativeTabMenuRemovesOnlyClickedMemberWithoutClosingIt()
        {
            var group = Group(first, second);
            ClickMenuItem(NativeMenuItems(first), "Window Groups/Remove from Group");
            yield return WaitForMenuAction();
            Assert.That(first, Is.Not.Null);
            Assert.That(Dock(first), Is.SameAs(dock));
            CollectionAssert.AreEqual(new[] { second }, group.members);
            Assert.That(JEEWindowGroups.FindGroup(first), Is.Null);
        }

        [UnityTest]
        public IEnumerator NativeTabMenuDoesNotAddATabMovedAfterTheMenuOpened()
        {
            var group = Group(first);
            var items = NativeMenuItems(second);
            var label = items.Select(MenuLabel).SingleOrDefault(s => s.StartsWith("Window Groups/Add to Group/"));
            Assert.That(label, Is.Not.Null);
            var outside = Window(40, true);
            Move(second, Dock(outside));
            ClickMenuItem(items, label);
            yield return WaitForMenuAction();
            CollectionAssert.AreEqual(new[] { first }, group.members);
            Assert.That(JEEWindowGroups.FindGroup(second), Is.Null);
        }

        [Test]
        public void NativeSceneTabMenuOffersGroupCreation()
        {
            var scene = ScriptableObject.CreateInstance<SceneView>();
            owned.Add(scene);
            Move(scene, dock);
            var labels = NativeMenuItems(scene).Select(MenuLabel).ToArray();
            CollectionAssert.Contains(labels, "Window Groups/New Group");
            CollectionAssert.Contains(labels, "Close Tab");
        }

        private static object[] GroupMenuItems(JEEWindowGroup group)
        {
            var menu = new GenericMenu();
            group.AddItemsToMenu(menu, "");
            return ((IEnumerable)typeof(GenericMenu).GetProperty("menuItems", Flags).GetValue(menu)).Cast<object>().ToArray();
        }

        [UnityTest]
        public IEnumerator QuickAddGroupMenuTargetsExactWindowAndPreservesCollapse()
        {
            var group = Group(first);
            JEEWindowGroups.SetCollapsed(group, true);
            Window(40, true);
            var items = GroupMenuItems(group);
            var additions = items.Where(i => MenuLabel(i).StartsWith("Add Tab/")).ToArray();
            Assert.That(additions.Length, Is.EqualTo(2), "Only ungrouped tabs from this dock can be added");
            var label = additions.Select(MenuLabel).Single(s => s.EndsWith("(#" + second.GetInstanceID() + ")"));
            ClickMenuItem(items, label);
            yield return WaitForMenuAction();
            CollectionAssert.AreEqual(new[] { first, second }, group.members);
            Assert.That(group.collapsed, Is.True);
            Assert.That(JEEWindowGroups.FindGroup(anchor), Is.Null);
        }

        [UnityTest]
        public IEnumerator QuickAddGroupMenuRejectsTabMovedWhileMenuWasOpen()
        {
            var group = Group(first);
            var items = GroupMenuItems(group);
            var label = items.Select(MenuLabel).SingleOrDefault(s => s.StartsWith("Add Tab/") && s.EndsWith("(#" + second.GetInstanceID() + ")"));
            Assert.That(label, Is.Not.Null);
            var outside = Window(40, true);
            Move(second, Dock(outside));
            ClickMenuItem(items, label);
            yield return WaitForMenuAction();
            CollectionAssert.AreEqual(new[] { first }, group.members);
        }

        private static Vector2 RepresentativeTabPosition(JEEWindowGroup group)
        {
            group.lastActiveMember.SendEvent(new Event { type = EventType.Layout });
            group.lastActiveMember.SendEvent(new Event { type = EventType.Repaint });
            var targetDock = group.ownerDock;
            var index = Array.IndexOf(Tabs(targetDock), group.lastActiveMember);
            var area = (Rect)DockType.GetField("m_TabAreaRect", Flags).GetValue(targetDock);
            var style = (GUIStyle)DockType.GetField("tabStyle", Flags).GetValue(targetDock);
            var x = (float)DockType.GetMethod("CalculateTabXPosition", Flags).Invoke(targetDock, new object[] { index });
            var width = (float)DockType.GetMethod("GetTabWidth", Flags, null, new[] { typeof(GUIStyle), typeof(int) }, null)
                .Invoke(targetDock, new object[] { style, index });
            var scroll = (float)DockType.GetField("m_ScrollOffset", Flags).GetValue(targetDock);
            return new Vector2(x + width / 2 - scroll - 1, area.center.y);
        }

        [UnityTest]
        public IEnumerator GroupSettingsUseProjectSettingsAndRemoveTheDedicatedSetupWindow()
        {
            var assembly = typeof(JEEWindowGroup).Assembly;
            var settings = assembly.GetType("JustEnoughEditor.JEEWindowGroupSettings");
            Assert.That(settings, Is.Not.Null);
            var provider = (SettingsProvider)settings.GetMethod("CreateProvider", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).Invoke(null, null);
            Assert.That(provider.scope, Is.EqualTo(SettingsScope.Project));
            Assert.That(provider.settingsPath, Is.EqualTo("Project/Just Enough Editor/Window Groups"));
            Assert.That(SettingsService.Exists(provider.settingsPath), Is.True, "Unity must discover the provider");
            Assert.That(assembly.GetType("JustEnoughEditor.JEEWindowGroupSetup"), Is.Null);
            var settingsWindow = SettingsService.OpenProjectSettings(provider.settingsPath);
            owned.Add(settingsWindow);
            yield return null;
            settingsWindow.SendEvent(new Event { type = EventType.Layout });
            settingsWindow.SendEvent(new Event { type = EventType.Repaint });
            var group = Group(first, second);
            JEEWindowGroups.SetCollapsed(group, true);
            settingsWindow.SendEvent(new Event { type = EventType.Layout });
            settingsWindow.SendEvent(new Event { type = EventType.Repaint });
            Assert.That(group.collapsed, Is.True, "Viewing settings must not expand a group");
        }

        [Test]
        public void GroupSettingsMenusReplaceDedicatedManagementActions()
        {
            var group = Group(first);
            var labels = GroupMenuItems(group).Select(MenuLabel).ToArray();
            CollectionAssert.Contains(labels, "Settings…");
            CollectionAssert.DoesNotContain(labels, "Rename / Color…");
            CollectionAssert.DoesNotContain(labels, "Add Windows…");
            var memberLabels = NativeMenuItems(first).Select(MenuLabel).ToArray();
            CollectionAssert.Contains(memberLabels, "Window Groups/Settings…");
            CollectionAssert.DoesNotContain(memberLabels, "Window Groups/Manage Group");
        }

        [UnityTest]
        public IEnumerator GroupSettingsAutomaticColorsAreDistinctAndRemainStable()
        {
            ClickMenuItem(NativeMenuItems(first), "Window Groups/New Group");
            yield return WaitForMenuAction();
            var one = JEEWindowGroups.FindGroup(first); owned.Add(one);
            ClickMenuItem(NativeMenuItems(second), "Window Groups/New Group");
            yield return WaitForMenuAction();
            var two = JEEWindowGroups.FindGroup(second); owned.Add(two);
            Assert.That(one.groupColor, Is.Not.EqualTo(two.groupColor));
            Assert.That(one.groupColor.a, Is.EqualTo(1));
            var color = two.groupColor;
            two.groupName = "Renamed";
            JEEWindowGroups.SetCollapsed(two, true);
            JEEWindowGroups.SetCollapsed(two, false);
            JEEWindowGroups.Reconcile();
            Assert.That(two.groupColor, Is.EqualTo(color));
            var json = EditorJsonUtility.ToJson(two);
            two.groupColor = Color.clear;
            EditorJsonUtility.FromJsonOverwrite(json, two);
            Assert.That(two.groupColor, Is.EqualTo(color), "Color is stored with the serialized heading");
        }
        private static VisualElement DockVisualTree(object target)
        {
            return (VisualElement)typeof(EditorWindow).Assembly.GetType("UnityEditor.GUIView")
                .GetProperty("visualTree", Flags).GetValue(target);
        }

        private static Rect GroupIndicatorBounds(JEEWindowGroup group)
        {
            var method = typeof(JEEDockAdapter).GetMethod("GroupIndicatorBounds", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "A group must have a tab-strip indicator");
            return (Rect)method.Invoke(null, new object[] { group });
        }

        [Test]
        public void GroupIndicatorSpansOnlyItsMembersWithoutChangingExpandedTitles()
        {
            var original = new GUIContent(first.titleContent);
            var group = Group(first);
            var other = Group(second);
            RepresentativeTabPosition(group);
            JEEWindowGroups.Reconcile();
            var bounds = GroupIndicatorBounds(group);
            var next = GroupIndicatorBounds(other);
            Assert.That(bounds.width, Is.GreaterThan(0));
            Assert.That(bounds.xMax, Is.LessThanOrEqualTo(next.xMin));
            Assert.That(first.titleContent.text, Is.EqualTo(original.text));
            Assert.That(first.titleContent.image, Is.SameAs(original.image));
            var indicator = DockVisualTree(dock).Q<IMGUIContainer>("jee-group-" + group.groupId);
            Assert.That(indicator, Is.Not.Null);
            Assert.That(indicator.pickingMode, Is.EqualTo(PickingMode.Ignore));
            Assert.That(indicator.focusable, Is.False);
            var draws = 0;
            var draw = indicator.onGUIHandler;
            indicator.onGUIHandler = () => { if (Event.current.type == EventType.Repaint) draws++; draw(); };
            group.lastActiveMember.SendEvent(new Event { type = EventType.Layout });
            group.lastActiveMember.SendEvent(new Event { type = EventType.Repaint });
            Assert.That(draws, Is.GreaterThan(0), "Unity must draw the indicator through the native dock's panel");
            JEEWindowGroups.SetCollapsed(group, true);
            RepresentativeTabPosition(group);
            var collapsed = GroupIndicatorBounds(group);
            Assert.That(collapsed.width, Is.LessThan(bounds.width), "Collapsed line must stop at the heading");
        }

        [Test]
        public void GroupIndicatorClipsAtTabStripAndExcludesScrollButtons()
        {
            for (var i = 0; i < 8; i++) Move(Window(100 + i), dock);
            var group = Group(Tabs(dock).Skip(1).ToArray());
            RepresentativeTabPosition(group);
            var area = (Rect)DockType.GetField("m_TabAreaRect", Flags).GetValue(dock);
            DockType.GetField("m_ScrollOffset", Flags).SetValue(dock, 50f);
            group.lastActiveMember.SendEvent(new Event { type = EventType.Repaint });
            var bounds = GroupIndicatorBounds(group);
            var left = (Rect)DockType.GetField("m_ScrollLeftRect", Flags).GetValue(dock);
            var right = (Rect)DockType.GetField("m_ScrollRightRect", Flags).GetValue(dock);
            Assert.That(bounds.width, Is.GreaterThan(0));
            Assert.That(bounds.xMin, Is.GreaterThanOrEqualTo(area.xMin));
            Assert.That(bounds.xMax, Is.LessThanOrEqualTo(area.xMax));
            Assert.That(bounds.Overlaps(left), Is.False);
            Assert.That(bounds.Overlaps(right), Is.False);
        }

        [Test]
        public void GroupIndicatorFollowsMovedGroupAndIsRemovedOnUngroup()
        {
            var group = Group(first, second);
            RepresentativeTabPosition(group);
            JEEWindowGroups.Reconcile();
            var name = "jee-group-" + group.groupId;
            var originalRoot = DockVisualTree(dock);
            Assert.That(originalRoot.Q<IMGUIContainer>(name), Is.Not.Null);
            var destination = Window(3, true);
            JEEWindowGroups.SetCollapsed(group, true);
            Move(group.lastActiveMember, Dock(destination));
            JEEWindowGroups.Reconcile();
            Assert.That(originalRoot.Q<IMGUIContainer>(name), Is.Null);
            var root = DockVisualTree(group.ownerDock);
            Assert.That(root.Q<IMGUIContainer>(name), Is.Not.Null);
            JEEWindowGroups.Ungroup(group);
            Assert.That(root.Q<IMGUIContainer>(name), Is.Null);
        }

        private static void TabInput(JEEWindowGroup group, EventType type, Vector2 position, int button = 0)
        {
            group.lastActiveMember.SendEvent(new Event { type = type, mousePosition = position, button = button, clickCount = 1 });
        }

        [UnityTest]
        public IEnumerator FocusingRepresentativeExpandsWithoutOpeningAHeading()
        {
            var group = Group(first, second);
            second.Focus();
            JEEWindowGroups.SetCollapsed(group, true);
            CollectionAssert.AreEqual(new EditorWindow[] { anchor, second }, Tabs(dock));
            Assert.That(Active(dock), Is.SameAs(second));
            JEEWindowGroupSettings.AutoExpandCollapse = true;
            anchor.Focus(); JEEWindowGroups.FollowFocus();
            var point = RepresentativeTabPosition(group);
            TabInput(group, EventType.MouseDown, point);
            TabInput(group, EventType.MouseUp, point);
            yield return WaitForMenuAction();
            JEEWindowGroups.FollowFocus();
            Assert.That(group.collapsed, Is.False);
            Assert.That(Active(dock), Is.SameAs(second));
            Assert.That(Tabs(dock).OfType<JEEWindowGroup>(), Is.Empty);
            first.Focus(); JEEWindowGroups.FollowFocus();
            Assert.That(group.collapsed, Is.False);
            Assert.That(Active(dock), Is.SameAs(first));
        }

        [UnityTest]
        public IEnumerator FocusOutsideCollapsesToLastUsedMemberWithoutChangingItsContent()
        {
            var group = Group(first, second);
            JEEWindowGroupSettings.AutoExpandCollapse = true;
            first.Focus(); JEEWindowGroups.FollowFocus();
            second.Focus(); JEEWindowGroups.FollowFocus();
            var outside = Window(33, true);
            outside.Focus();
            yield return WaitForMenuAction();
            JEEWindowGroups.FollowFocus();
            Assert.That(group.collapsed, Is.True);
            Assert.That(Active(dock), Is.SameAs(second));
            Assert.That(EditorWindow.focusedWindow, Is.SameAs(outside));
            CollectionAssert.AreEqual(new EditorWindow[] { anchor, second }, Tabs(dock));
            Assert.That(second.value, Is.EqualTo(29));
            second.Focus(); JEEWindowGroups.FollowFocus();
            Assert.That(group.collapsed, Is.False);
        }

        [UnityTest]
        public IEnumerator ManualModeIgnoresFocusChangesAndUsesMemberContextMenu()
        {
            var group = Group(first, second);
            first.Focus(); JEEWindowGroups.FollowFocus();
            anchor.Focus(); JEEWindowGroups.FollowFocus();
            Assert.That(group.collapsed, Is.False);
            ClickMenuItem(NativeMenuItems(first), "Window Groups/Collapse Group");
            yield return WaitForMenuAction();
            first.Focus(); JEEWindowGroups.FollowFocus();
            Assert.That(group.collapsed, Is.True);
            ClickMenuItem(NativeMenuItems(first), "Window Groups/Expand Group");
            yield return WaitForMenuAction();
            Assert.That(group.collapsed, Is.False);
            Assert.That(Active(dock), Is.SameAs(first));
        }

        [UnityTest]
        public IEnumerator ExplicitMenuKeepsSelectedMemberVisibleWhenExpandedTabsOverflow()
        {
            for (var i = 0; i < 8; i++) Move(Window(100 + i), dock);
            var group = Group(Tabs(dock).Where(w => w != anchor).ToArray());
            JEEWindowGroups.SetCollapsed(group, true);
            var point = RepresentativeTabPosition(group);
            ClickMenuItem(GroupMenuItems(group), "Expand Group");
            yield return WaitForMenuAction();
            Assert.That(group.collapsed, Is.False);
            point = RepresentativeTabPosition(group);
            var area = (Rect)DockType.GetField("m_TabAreaRect", Flags).GetValue(dock);
            Assert.That(area.Contains(point), Is.True, "Explicit expansion must leave the selected member visible");
            ClickMenuItem(GroupMenuItems(group), "Collapse Group");
            yield return WaitForMenuAction();
            Assert.That(group.collapsed, Is.True);
            Assert.That(group.members.Count, Is.EqualTo(10));
        }

        [UnityTest]
        public IEnumerator NativeTabScrollerDoesNotToggleGroupUnderneath()
        {
            for (var i = 0; i < 5; i++) Move(Window(100 + i), dock);
            var group = Group(Tabs(dock).Skip(Tabs(dock).Length - 2).ToArray());
            group.groupName = "A very long group heading";
            group.RefreshTitle();
            JEEWindowGroups.SetCollapsed(group, true);
            RepresentativeTabPosition(group);
            var area = (Rect)DockType.GetField("m_TabAreaRect", Flags).GetValue(dock);
            var x = (float)DockType.GetMethod("CalculateTabXPosition", Flags).Invoke(dock, new object[] { Array.IndexOf(Tabs(dock), group.lastActiveMember) });
            DockType.GetField("m_ScrollOffset", Flags).SetValue(dock, x - area.width + 60);
            group.lastActiveMember.SendEvent(new Event { type = EventType.Repaint });
            var scroller = (Rect)DockType.GetField("m_ScrollRightRect", Flags).GetValue(dock);
            Assert.That(scroller.width, Is.GreaterThan(0), "The fixture must have a real tab scroller");
            TabInput(group, EventType.MouseDown, scroller.center);
            TabInput(group, EventType.MouseUp, scroller.center);
            yield return WaitForMenuAction();
            Assert.That(group.collapsed, Is.True, "Scrolling the tab strip must not toggle a group");
        }

        [TestCase(0)]
        [TestCase(1)]
        public void CollapsedMemberNativeFocusExpandsTheExactInstance(int index)
        {
            var group = Group(first, second);
            var ids = new[] { first.GetInstanceID(), second.GetInstanceID() };
            var representative = index == 0 ? second : first;
            representative.Focus();
            JEEWindowGroups.SetCollapsed(group, true);
            CollectionAssert.AreEqual(new EditorWindow[] { anchor, representative }, Tabs(dock));
            Assert.That(Dock(index == 0 ? first : second), Is.Not.Null.And.Not.SameAs(dock));
            var target = index == 0 ? first : second;
            target.Focus();
            Assert.That(group.collapsed, Is.False);
            Assert.That(Active(dock), Is.SameAs(target));
            Assert.That(first.GetInstanceID(), Is.EqualTo(ids[0]));
            Assert.That(second.GetInstanceID(), Is.EqualTo(ids[1]));
            Assert.That(first.value, Is.EqualTo(17));
            Assert.That(second.value, Is.EqualTo(29));
        }

        [Test]
        public void ExpandAndCollapsePreserveUnrelatedSelection()
        {
            var group = Group(first, second);
            anchor.Focus();
            JEEWindowGroups.SetCollapsed(group, true);
            Assert.That(Active(dock), Is.SameAs(anchor));
            JEEWindowGroups.SetCollapsed(group, false);
            Assert.That(Active(dock), Is.SameAs(anchor));
        }

        [Test]
        public void CollapsingKeepsSelectedMemberAndItsContent()
        {
            var group = Group(first, second);
            second.Focus();
            JEEWindowGroups.SetCollapsed(group, true);
            Assert.That(Active(dock), Is.SameAs(second));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UngroupRestoresEveryMemberWithoutRecreation(bool collapsed)
        {
            var group = Group(first, second);
            JEEWindowGroups.SetCollapsed(group, collapsed);
            JEEWindowGroups.Ungroup(group);
            Assert.That(group == null, Is.True);
            CollectionAssert.AreEqual(new EditorWindow[] { anchor, first, second }, Tabs(dock));
            Assert.That(first.value, Is.EqualTo(17));
            Assert.That(second.value, Is.EqualTo(29));
        }

        [Test]
        public void ClosingStorageRestoresCollapsedMembers()
        {
            var group = Group(first, second);
            JEEWindowGroups.SetCollapsed(group, true);
            group.Close();
            CollectionAssert.AreEqual(new EditorWindow[] { anchor, first, second }, Tabs(dock));
        }

        [Test]
        public void RemovingLastMemberDissolvesGroup()
        {
            var group = Group(first);
            JEEWindowGroups.SetCollapsed(group, true);
            JEEWindowGroups.RemoveMember(group, first);
            Assert.That(group == null, Is.True);
            Assert.That(Dock(first), Is.SameAs(dock));
            Assert.That(Tabs(dock), Does.Contain(first));
        }

        [Test]
        public void MovingCollapsedRepresentativeMovesGroupAndKeepsCollapseState()
        {
            var group = Group(first, second);
            first.Focus();
            JEEWindowGroups.SetCollapsed(group, true);
            var other = Window(3, true); var otherDock = Dock(other);
            Move(first, otherDock);
            JEEWindowGroups.Reconcile();
            Assert.That(group.collapsed, Is.True);
            Assert.That(group.ownerDock, Is.SameAs(otherDock));
            CollectionAssert.AreEqual(new EditorWindow[] { anchor }, Tabs(dock));
            JEEWindowGroups.SetCollapsed(group, false);
            CollectionAssert.AreEqual(new EditorWindow[] { other, first, second }, Tabs(otherDock));
        }

        [Test]
        public void DraggingMemberOutRemovesOnlyThatInstance()
        {
            var group = Group(first, second);
            var other = Window(3, true); var otherDock = Dock(other);
            Move(second, otherDock);
            JEEWindowGroups.Reconcile();
            CollectionAssert.AreEqual(new EditorWindow[] { first }, group.members);
            Assert.That(Dock(second), Is.SameAs(otherDock));
        }

        [Test]
        public void ReorderingMembersUpdatesSavedOrder()
        {
            var group = Group(first, second);
            Move(first, dock);
            JEEWindowGroups.Reconcile();
            CollectionAssert.AreEqual(new EditorWindow[] { second, first }, group.members);
        }

        [Test]
        public void MixedDocksAreRejectedBeforeMutation()
        {
            var other = Window(3, true);
            var before = Tabs(dock);
            Assert.Throws<ArgumentException>(() => Group(first, other));
            CollectionAssert.AreEqual(before, Tabs(dock));
            Assert.That(first, Is.Not.Null);
        }

        [Test]
        public void AlreadyGroupedMemberIsRejectedBeforeMutation()
        {
            var group = Group(first);
            var before = Tabs(dock);
            Assert.Throws<ArgumentException>(() => Group(first, second));
            CollectionAssert.AreEqual(before, Tabs(dock));
            Assert.That(group.members, Is.EqualTo(new EditorWindow[] { first }));
        }

        [Test]
        public void ClosingOnlyVisibleRepresentativeKeepsOtherMembersAlive()
        {
            var group = Group(anchor, first, second);
            first.Focus();
            JEEWindowGroups.SetCollapsed(group, true);
            CollectionAssert.AreEqual(new EditorWindow[] { first }, Tabs(dock));
            first.Close();
            JEEWindowGroups.Reconcile();
            Assert.That(group, Is.Not.Null);
            CollectionAssert.AreEqual(new EditorWindow[] { anchor, second }, group.members);
            CollectionAssert.AreEqual(new EditorWindow[] { anchor }, Tabs(dock));
            Assert.That(second.value, Is.EqualTo(29));
        }

        [Test]
        public void DestroyingWholeDockDoesNotResurrectMembersOrParking()
        {
            var group = Group(anchor, first, second);
            JEEWindowGroups.SetCollapsed(group, true);
            var parking = group.parking;
            UnityEngine.Object.DestroyImmediate((UnityEngine.Object)dock);
            Assert.That(group == null && first == null && second == null && anchor == null, Is.True);
            Assert.That(parking == null, Is.True);
        }

        [Test]
        public void MultipleGroupsCollapseAndRestoreIndependently()
        {
            var a = Group(first);
            var b = Group(second);
            JEEWindowGroups.SetCollapsed(a, true);
            JEEWindowGroups.SetCollapsed(b, true);
            JEEWindowGroupSettings.AutoExpandCollapse = true;
            first.Focus(); JEEWindowGroups.FollowFocus();
            Assert.That(a.collapsed, Is.False);
            Assert.That(b.collapsed, Is.True);
            CollectionAssert.Contains(Tabs(dock), first);
            CollectionAssert.Contains(Tabs(dock), second);
        }

        [Test]
        public void AddingToCollapsedGroupKeepsTheNewInstanceHiddenUntilActivation()
        {
            var group = Group(first);
            JEEWindowGroups.SetCollapsed(group, true);
            JEEWindowGroups.AddMembers(group, new EditorWindow[] { second });
            Assert.That(group.collapsed, Is.True);
            CollectionAssert.AreEqual(new EditorWindow[] { first, second }, group.members);
            CollectionAssert.DoesNotContain(Tabs(dock), second);
            second.Focus();
            Assert.That(Active(dock), Is.SameAs(second));
        }

        [Test]
        public void InvalidAdditionLeavesAllTabsAndMembershipUntouched()
        {
            var group = Group(first);
            var other = Window(3, true);
            var before = Tabs(dock);
            Assert.Throws<ArgumentException>(() => JEEWindowGroups.AddMembers(group, new EditorWindow[] { second, other }));
            CollectionAssert.AreEqual(before, Tabs(dock));
            CollectionAssert.AreEqual(new EditorWindow[] { first }, group.members);
        }

        [Test]
        public void RecoveryRestoresMembersFromEveryGroup()
        {
            var a = Group(first); var b = Group(second);
            JEEWindowGroups.SetCollapsed(a, true); JEEWindowGroups.SetCollapsed(b, true);
            JEEWindowGroups.RestoreAll();
            Assert.That(a == null && b == null, Is.True);
            CollectionAssert.AreEqual(new EditorWindow[] { anchor, first, second }, Tabs(dock));
        }

        [Test]
        public void RepeatedCollapseAndExpandDoNotLeaveParkingWindowsBehind()
        {
            var before = Resources.FindObjectsOfTypeAll<JEEWindowGroupParking>().Length;
            var group = Group(first, second);
            for (var i = 0; i < 5; i++)
            {
                JEEWindowGroups.SetCollapsed(group, true);
                JEEWindowGroups.SetCollapsed(group, true);
                JEEWindowGroups.SetCollapsed(group, false);
            }
            Assert.That(Resources.FindObjectsOfTypeAll<JEEWindowGroupParking>().Length, Is.EqualTo(before + 1));
            JEEWindowGroups.Ungroup(group);
            Assert.That(Resources.FindObjectsOfTypeAll<JEEWindowGroupParking>().Length, Is.EqualTo(before));
            CollectionAssert.AreEqual(new EditorWindow[] { anchor, first, second }, Tabs(dock));
        }

        [TestCase("UnityEditor.SceneView")]
        [TestCase("UnityEditor.GameView")]
        [TestCase("UnityEditor.InspectorWindow")]
        [TestCase("UnityEditor.ConsoleWindow")]
        [TestCase("UnityEditor.ProjectBrowser")]
        [TestCase("JustEnoughEditor.IconFilterWindow")]
        public void NativeWindowRetainsItsInstanceThroughCollapseAndActivation(string typeName)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(typeName)).First(t => t != null);
            var window = (EditorWindow)ScriptableObject.CreateInstance(type);
            owned.Add(window);
            Move(window, dock);
            var id = window.GetInstanceID();
            var group = Group(first, window);
            first.Focus();
            JEEWindowGroups.SetCollapsed(group, true);
            window.Focus();
            Assert.That(window.GetInstanceID(), Is.EqualTo(id));
            Assert.That(group.collapsed, Is.False);
            Assert.That(Active(dock), Is.SameAs(window));
        }

        [Test]
        public void UIToolkitWindowKeepsItsVisualTreeAndSerializedState()
        {
            var window = ScriptableObject.CreateInstance<GroupUITestWindow>();
            window.value = 57; owned.Add(window); Move(window, dock);
            var root = window.rootVisualElement;
            var group = Group(window);
            JEEWindowGroups.SetCollapsed(group, true);
            JEEWindowGroups.SetCollapsed(group, false);
            Assert.That(window.rootVisualElement, Is.SameAs(root));
            Assert.That(window.value, Is.EqualTo(57));
        }

        [Test]
        public void CollapsedRepresentativeRestoresItsNativeTitleAndIcon()
        {
            var title = new GUIContent(second.titleContent);
            var group = Group(first, second);
            second.Focus();
            JEEWindowGroups.SetCollapsed(group, true);
            Assert.That(second.titleContent.text, Is.EqualTo("Work (2)"));
            Assert.That(second.titleContent.image, Is.SameAs(title.image));
            Assert.That(Active(dock), Is.SameAs(second));
            JEEWindowGroups.SetCollapsed(group, false);
            Assert.That(second.titleContent.text, Is.EqualTo(title.text));
            Assert.That(second.titleContent.image, Is.SameAs(title.image));
            Assert.That(second.titleContent.tooltip, Is.EqualTo(title.tooltip));
            CollectionAssert.AreEqual(new EditorWindow[] { anchor, first, second }, Tabs(dock));
        }

        [Test]
        public void AutomaticModeIsUserScopedAndEnabledByDefault()
        {
            EditorPrefs.DeleteKey(JEEWindowGroupSettings.AutoExpandCollapseKey);
            Assert.That(JEEWindowGroupSettings.AutoExpandCollapse, Is.True);
            var provider = JEEWindowGroupSettings.CreateUserProvider();
            Assert.That(provider.scope, Is.EqualTo(SettingsScope.User));
            Assert.That(provider.settingsPath, Is.EqualTo("Preferences/Just Enough Editor/Window Groups"));
            JEEWindowGroupSettings.AutoExpandCollapse = false;
            Assert.That(EditorPrefs.GetBool(JEEWindowGroupSettings.AutoExpandCollapseKey, true), Is.False);
        }

        [UnityTest]
        public IEnumerator SettingsAndUtilityFocusDoNotFoldTheWorkingGroup()
        {
            var group = Group(first, second);
            JEEWindowGroupSettings.AutoExpandCollapse = true;
            first.Focus(); JEEWindowGroups.FollowFocus();
            var settings = SettingsService.OpenProjectSettings("Project/Just Enough Editor/Window Groups");
            owned.Add(settings);
            settings.Focus();
            yield return WaitForMenuAction();
            JEEWindowGroups.FollowFocus();
            Assert.That(group.collapsed, Is.False);
            var utility = Window(91);
            utility.ShowUtility(); utility.Focus();
            yield return WaitForMenuAction();
            JEEWindowGroups.FollowFocus();
            Assert.That(group.collapsed, Is.False);
        }

        [Test]
        public void RemovingTheRepresentativeRestoresItsTitleAndKeepsOtherMembers()
        {
            var title = new GUIContent(first.titleContent);
            var group = Group(first, second);
            first.Focus();
            JEEWindowGroups.SetCollapsed(group, true);
            JEEWindowGroups.RemoveMember(group, first);
            Assert.That(first.titleContent.text, Is.EqualTo(title.text));
            Assert.That(group.collapsed, Is.True);
            CollectionAssert.AreEqual(new[] { second }, group.members);
            Assert.That(Dock(second), Is.SameAs(dock));
            Assert.That(JEEWindowGroups.FindGroup(first), Is.Null);
        }

        [Test]
        public void MigratesAnExistingHeadingIntoHiddenStorageWithoutRecreatingTabs()
        {
            var group = Group(first, second);
            var parking = group.parking;
            Move(group, dock);
            group.parking = null;
            JEEDockAdapter.DestroyParking(parking);
            var ids = new[] { first.GetInstanceID(), second.GetInstanceID() };
            JEEWindowGroups.Reconcile();
            CollectionAssert.AreEqual(new EditorWindow[] { anchor, first, second }, Tabs(dock));
            CollectionAssert.AreEqual(ids, group.members.Select(w => w.GetInstanceID()).ToArray());
            Assert.That(group.ownerDock, Is.SameAs(dock));
            Assert.That(group.parking, Is.Not.Null);
            Assert.That(Dock(group), Is.SameAs(group.parking));
        }

        [Test]
        public void NativeDetachOfTheOnlyRepresentativeRetainsHiddenMembers()
        {
            var group = Group(anchor, first, second);
            first.Focus();
            JEEWindowGroups.SetCollapsed(group, true);
            var destination = Window(72, true);
            var target = Dock(destination);
            DockType.GetMethod("RemoveTab", Flags, null, new[] { typeof(EditorWindow), typeof(bool), typeof(bool) }, null)
                .Invoke(dock, new object[] { first, true, false });
            Assert.That(group, Is.Not.Null, "The old dock must not destroy the parked members during native removal");
            Assert.That(second, Is.Not.Null);
            Move(first, target);
            JEEWindowGroups.Reconcile();
            Assert.That(group.ownerDock, Is.SameAs(target));
            CollectionAssert.AreEqual(new EditorWindow[] { destination, first }, Tabs(target));
            JEEWindowGroups.SetCollapsed(group, false);
            CollectionAssert.AreEqual(new EditorWindow[] { destination, anchor, first, second }, Tabs(target));
        }

        [Test]
        public void UnsavedMemberRemainsInTheContainersNativeViewHierarchy()
        {
            var group = Group(first, second);
            second.Focus();
            first.SetDirty(true);
            try
            {
                JEEWindowGroups.SetCollapsed(group, true);
                var viewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.View");
                var children = ((IEnumerable)viewType.GetProperty("allChildren", Flags).GetValue(dock)).Cast<object>();
                Assert.That(children.Any(child => DockType.IsInstanceOfType(child) && Tabs(child).Contains(first)), Is.True);
                Assert.That(UnsavedWindows(dock), Does.Contain(first));
                Assert.That(first.hasUnsavedChanges, Is.True);
                JEEWindowGroups.Ungroup(group);
                Assert.That(first.hasUnsavedChanges, Is.True);
            }
            finally { if (first) first.SetDirty(false); }
        }

        private static EditorWindow[] UnsavedWindows(object target)
        {
            var container = DockType.GetProperty("window", Flags).GetValue(target);
            return ((IEnumerable)container.GetType().GetField("m_UnsavedEditorWindows", Flags).GetValue(container))
                .Cast<EditorWindow>().ToArray();
        }

        [Test]
        public void CollapsedGroupMoveTransfersUnsavedClosePromptToDestination()
        {
            var group = Group(first, second);
            second.Focus();
            var other = Window(3, true); var destination = Dock(other);
            first.SetDirty(true);
            try
            {
                JEEWindowGroups.SetCollapsed(group, true);
                Move(group.lastActiveMember, destination);
                JEEWindowGroups.Reconcile();
                Assert.That(UnsavedWindows(destination), Does.Contain(first));
                Assert.That(UnsavedWindows(dock), Has.No.Member(first));
            }
            finally { if (first) first.SetDirty(false); }
        }

        [Test]
        public void RecoveryWorksWhenOnlyFullGroupingCapabilityIsUnavailable()
        {
            var group = Group(first, second);
            JEEWindowGroups.SetCollapsed(group, true);
            var unavailable = typeof(JEEDockAdapter).GetProperty("UnavailableReason", BindingFlags.NonPublic | BindingFlags.Static);
            var original = unavailable.GetValue(null);
            try
            {
                // Simulate losing an activation/rendering hook while native moves still work.
                unavailable.SetValue(null, "Simulated activation capability unavailable");
                Assert.Throws<NotSupportedException>(() => JEEDockAdapter.RequireSupport());
                JEEWindowGroups.RestoreAll();
                Assert.That(group == null, Is.True);
                CollectionAssert.AreEqual(new EditorWindow[] { anchor, first, second }, Tabs(dock));
            }
            finally { unavailable.SetValue(null, original); }
        }

        [Test]
        public void FailedNativeInsertionRestoresSourceOrderAndBothSelections()
        {
            var other = Window(3, true); var destination = Dock(other);
            anchor.Focus(); other.Focus();
            var sourceOrder = Tabs(dock); var destinationOrder = Tabs(destination);
            first.throwOnNextAdd = true;
            Assert.Throws<TargetInvocationException>(() => JEEDockAdapter.Move(first, (ScriptableObject)destination));
            Assert.That(Dock(first), Is.SameAs(dock));
            CollectionAssert.AreEqual(sourceOrder, Tabs(dock));
            CollectionAssert.AreEqual(destinationOrder, Tabs(destination));
            Assert.That(Active(dock), Is.SameAs(anchor));
            Assert.That(Active(destination), Is.SameAs(other));
        }

        [Test]
        public void FailedCollapseRestoresAllMembersAndTheirSelection()
        {
            var group = Group(first, second);
            first.Focus();
            var order = Tabs(dock);
            second.throwOnNextAdd = true;
            Assert.Throws<TargetInvocationException>(() => JEEWindowGroups.SetCollapsed(group, true));
            Assert.That(group.collapsed, Is.False);
            CollectionAssert.AreEqual(order, Tabs(dock));
            Assert.That(Active(dock), Is.SameAs(first));
            CollectionAssert.AreEqual(new EditorWindow[] { first, second }, group.members);
        }
        [Test]
        public void MovingCollapsedRepresentativeDoesNotReactivateHiddenMembersDuringNativeDetach()
        {
            var group = Group(first, second);
            first.Focus();
            JEEWindowGroups.SetCollapsed(group, true);
            var parking = Dock(second);
            var other = Window(3, true); var destination = Dock(other);
            second.throwOnNextAdd = true;
            try
            {
                Move(group.lastActiveMember, destination);
                JEEWindowGroups.Reconcile();
                Assert.That(group.collapsed, Is.True);
                Assert.That(Dock(first), Is.SameAs(destination));
                Assert.That(Dock(second), Is.SameAs(parking));
                Assert.That(second.throwOnNextAdd, Is.True, "Moving a collapsed group must not re-add or activate its members.");
                CollectionAssert.AreEqual(new EditorWindow[] { anchor }, Tabs(dock));
                CollectionAssert.AreEqual(new EditorWindow[] { other, first }, Tabs(destination));
            }
            finally { second.throwOnNextAdd = false; }
        }
    }
}
