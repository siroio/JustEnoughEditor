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
        }

        [Test]
        public void CreateKeepsDistinctSameTypeMembersAndNativeTabOrder()
        {
            var group = Group(second, first);
            CollectionAssert.AreEqual(new EditorWindow[] { first, second }, group.members);
            CollectionAssert.AreEqual(new EditorWindow[] { anchor, group, first, second }, Tabs(dock));
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
            Assert.That(callback, Is.Not.Null, "Menu item must be enabled");
            callback();
        }
        private static IEnumerator WaitForMenuAction()
        {
            var called = false;
            EditorApplication.delayCall += () => called = true;
            var deadline = EditorApplication.timeSinceStartup + 3;
            while (!called && EditorApplication.timeSinceStartup < deadline) yield return null;
            Assert.That(called, Is.True, "Deferred native menu actions must run on the editor loop");
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
            Assert.That(Dock(group), Is.SameAs(dock));
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

        private static object[] HeadingMenuItems(JEEWindowGroup group)
        {
            var menu = new GenericMenu();
            group.AddItemsToMenu(menu);
            return ((IEnumerable)typeof(GenericMenu).GetProperty("menuItems", Flags).GetValue(menu)).Cast<object>().ToArray();
        }

        [UnityTest]
        public IEnumerator QuickAddHeadingMenuTargetsExactWindowAndPreservesCollapse()
        {
            var group = Group(first);
            JEEWindowGroups.SetCollapsed(group, true);
            Window(40, true);
            var items = HeadingMenuItems(group);
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
        public IEnumerator QuickAddHeadingMenuRejectsTabMovedWhileMenuWasOpen()
        {
            var group = Group(first);
            var items = HeadingMenuItems(group);
            var label = items.Select(MenuLabel).SingleOrDefault(s => s.StartsWith("Add Tab/") && s.EndsWith("(#" + second.GetInstanceID() + ")"));
            Assert.That(label, Is.Not.Null);
            var outside = Window(40, true);
            Move(second, Dock(outside));
            ClickMenuItem(items, label);
            yield return WaitForMenuAction();
            CollectionAssert.AreEqual(new[] { first }, group.members);
        }

        private static Vector2 HeadingTabPosition(JEEWindowGroup group)
        {
            group.SendEvent(new Event { type = EventType.Layout });
            group.SendEvent(new Event { type = EventType.Repaint });
            var targetDock = Dock(group);
            var index = Array.IndexOf(Tabs(targetDock), group);
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
            var labels = HeadingMenuItems(group).Select(MenuLabel).ToArray();
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
        public void GroupIndicatorSpansOnlyItsHeadingAndMembersWithoutChangingTitles()
        {
            var original = new GUIContent(first.titleContent);
            var group = Group(first);
            var other = Group(second);
            HeadingTabPosition(group);
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
            group.SendEvent(new Event { type = EventType.Layout });
            group.SendEvent(new Event { type = EventType.Repaint });
            Assert.That(draws, Is.GreaterThan(0), "Unity must draw the indicator through the native dock's panel");
            JEEWindowGroups.SetCollapsed(group, true);
            HeadingTabPosition(group);
            var collapsed = GroupIndicatorBounds(group);
            Assert.That(collapsed.width, Is.LessThan(bounds.width), "Collapsed line must stop at the heading");
        }

        [Test]
        public void GroupIndicatorClipsAtTabStripAndExcludesScrollButtons()
        {
            for (var i = 0; i < 8; i++) Move(Window(100 + i), dock);
            var group = Group(Tabs(dock).Skip(1).ToArray());
            HeadingTabPosition(group);
            var area = (Rect)DockType.GetField("m_TabAreaRect", Flags).GetValue(dock);
            DockType.GetField("m_ScrollOffset", Flags).SetValue(dock, 50f);
            group.SendEvent(new Event { type = EventType.Repaint });
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
            HeadingTabPosition(group);
            JEEWindowGroups.Reconcile();
            var name = "jee-group-" + group.groupId;
            var originalRoot = DockVisualTree(dock);
            Assert.That(originalRoot.Q<IMGUIContainer>(name), Is.Not.Null);
            var destination = Window(3, true);
            Move(group, Dock(destination));
            JEEWindowGroups.Reconcile();
            Assert.That(originalRoot.Q<IMGUIContainer>(name), Is.Null);
            var root = DockVisualTree(Dock(group));
            Assert.That(root.Q<IMGUIContainer>(name), Is.Not.Null);
            JEEWindowGroups.Ungroup(group);
            Assert.That(root.Q<IMGUIContainer>(name), Is.Null);
        }

        private static void TabInput(JEEWindowGroup group, EventType type, Vector2 position, int button = 0)
        {
            group.SendEvent(new Event { type = type, mousePosition = position, button = button, clickCount = 1 });
        }

        [UnityTest]
        public IEnumerator NativeHeadingClickSelectsWithoutTogglingExpandedOrCollapsedGroup()
        {
            var group = Group(first, second);
            anchor.Focus();
            var point = HeadingTabPosition(group);
            TabInput(group, EventType.MouseDown, point);
            Assert.That(Active(dock), Is.SameAs(group), "The test must hit Unity's real heading tab");
            yield return WaitForMenuAction();
            Assert.That(group.collapsed, Is.False, "Press alone must still allow dragging");
            TabInput(group, EventType.MouseUp, point);
            yield return WaitForMenuAction();
            Assert.That(group.collapsed, Is.False, "Selecting a heading must not fold its windows");
            JEEWindowGroups.SetCollapsed(group, true);
            point = HeadingTabPosition(group);
            TabInput(group, EventType.MouseDown, point);
            TabInput(group, EventType.MouseUp, point);
            yield return WaitForMenuAction();
            Assert.That(group.collapsed, Is.True, "Clicking an already-selected heading must not expand it");
            CollectionAssert.AreEqual(new[] { first, second }, group.members);
        }

        [UnityTest]
        public IEnumerator NativeHeadingFocusAndBodyClicksDoNotToggleGroup()
        {
            var group = Group(first, second);
            JEEWindowGroups.SetCollapsed(group, true);
            anchor.Focus(); group.Focus();
            yield return WaitForMenuAction();
            Assert.That(group.collapsed, Is.True);
            TabInput(group, EventType.MouseDown, new Vector2(10, 35));
            TabInput(group, EventType.MouseUp, new Vector2(10, 35));
            yield return WaitForMenuAction();
            Assert.That(group.collapsed, Is.True);
        }

        [UnityTest]
        public IEnumerator NativeHeadingDragAndRightButtonDoNotToggleGroup()
        {
            var group = Group(first, second);
            var point = HeadingTabPosition(group);
            TabInput(group, EventType.MouseDown, point, 1);
            TabInput(group, EventType.MouseUp, point, 1);
            yield return WaitForMenuAction();
            Assert.That(group.collapsed, Is.False);
            TabInput(group, EventType.MouseDown, point);
            TabInput(group, EventType.MouseDrag, point + new Vector2(14, 0));
            TabInput(group, EventType.MouseUp, point);
            yield return WaitForMenuAction();
            Assert.That(group.collapsed, Is.False);
            CollectionAssert.AreEqual(new[] { first, second }, group.members);
        }

        [UnityTest]
        public IEnumerator ExplicitHeadingMenuKeepsHeadingVisibleWhenExpandedTabsOverflow()
        {
            for (var i = 0; i < 8; i++) Move(Window(100 + i), dock);
            var group = Group(Tabs(dock).Where(w => w != anchor).ToArray());
            JEEWindowGroups.SetCollapsed(group, true);
            var point = HeadingTabPosition(group);
            ClickMenuItem(HeadingMenuItems(group), "Expand Group");
            yield return WaitForMenuAction();
            Assert.That(group.collapsed, Is.False);
            point = HeadingTabPosition(group);
            var area = (Rect)DockType.GetField("m_TabAreaRect", Flags).GetValue(dock);
            Assert.That(area.Contains(point), Is.True, "Explicit expansion must leave the heading visible");
            ClickMenuItem(HeadingMenuItems(group), "Collapse Group");
            yield return WaitForMenuAction();
            Assert.That(group.collapsed, Is.True);
            Assert.That(group.members.Count, Is.EqualTo(10));
        }

        [UnityTest]
        public IEnumerator NativeHeadingTabScrollerDoesNotToggleGroupUnderneath()
        {
            for (var i = 0; i < 5; i++) Move(Window(100 + i), dock);
            var group = Group(Tabs(dock).Skip(Tabs(dock).Length - 2).ToArray());
            group.groupName = "A very long group heading";
            group.RefreshTitle();
            JEEWindowGroups.SetCollapsed(group, true);
            HeadingTabPosition(group);
            var area = (Rect)DockType.GetField("m_TabAreaRect", Flags).GetValue(dock);
            var x = (float)DockType.GetMethod("CalculateTabXPosition", Flags).Invoke(dock, new object[] { Array.IndexOf(Tabs(dock), group) });
            DockType.GetField("m_ScrollOffset", Flags).SetValue(dock, x - area.width + 60);
            group.SendEvent(new Event { type = EventType.Repaint });
            var scroller = (Rect)DockType.GetField("m_ScrollRightRect", Flags).GetValue(dock);
            Assert.That(scroller.width, Is.GreaterThan(0), "The fixture must have a real tab scroller");
            TabInput(group, EventType.MouseDown, scroller.center);
            TabInput(group, EventType.MouseUp, scroller.center);
            yield return WaitForMenuAction();
            Assert.That(group.collapsed, Is.True, "Scrolling the tab strip must not click the heading underneath the button");
        }

        [TestCase(0)]
        [TestCase(1)]
        public void CollapsedMemberNativeFocusExpandsTheExactInstance(int index)
        {
            var group = Group(first, second);
            var ids = new[] { first.GetInstanceID(), second.GetInstanceID() };
            JEEWindowGroups.SetCollapsed(group, true);
            CollectionAssert.AreEqual(new EditorWindow[] { anchor, group }, Tabs(dock));
            Assert.That(Dock(first), Is.Not.Null.And.Not.SameAs(dock));
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
        public void CollapsingSelectedMemberSelectsHeading()
        {
            var group = Group(first, second);
            second.Focus();
            JEEWindowGroups.SetCollapsed(group, true);
            Assert.That(Active(dock), Is.SameAs(group));
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
        public void ClosingHeadingRestoresCollapsedMembers()
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

        [TestCase(false)]
        [TestCase(true)]
        public void MovingHeadingMovesMembersAndKeepsCollapseState(bool collapsed)
        {
            var group = Group(first, second);
            JEEWindowGroups.SetCollapsed(group, collapsed);
            var other = Window(3, true); var otherDock = Dock(other);
            Move(group, otherDock);
            JEEWindowGroups.Reconcile();
            Assert.That(group.collapsed, Is.EqualTo(collapsed));
            Assert.That(Tabs(dock), Is.EqualTo(new EditorWindow[] { anchor }));
            JEEWindowGroups.SetCollapsed(group, false);
            CollectionAssert.AreEqual(new EditorWindow[] { other, group, first, second }, Tabs(otherDock));
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
        public void ClosingOnlyVisibleHeadingKeepsItsHiddenMembersAlive()
        {
            var group = Group(anchor, first, second);
            JEEWindowGroups.SetCollapsed(group, true);
            Assert.That(Tabs(dock), Is.EqualTo(new EditorWindow[] { group }));
            group.Close();
            CollectionAssert.AreEqual(new EditorWindow[] { anchor, first, second }, Tabs(dock));
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
            first.Focus();
            Assert.That(a.collapsed, Is.False);
            Assert.That(b.collapsed, Is.True);
            CollectionAssert.Contains(Tabs(dock), first);
            CollectionAssert.DoesNotContain(Tabs(dock), second);
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
            Assert.That(Resources.FindObjectsOfTypeAll<JEEWindowGroupParking>().Length, Is.EqualTo(before));
            CollectionAssert.AreEqual(new EditorWindow[] { anchor, group, first, second }, Tabs(dock));
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
        public void HeadingRendersManagementControlsForBothStates()
        {
            var group = Group(first, second);
            foreach (var collapsed in new[] { false, true })
            {
                JEEWindowGroups.SetCollapsed(group, collapsed);
                group.SendEvent(new Event { type = EventType.Layout });
                group.SendEvent(new Event { type = EventType.Repaint });
                Assert.That(group.titleContent.text, Does.Contain("Work").And.Contain("(2)"));
                Assert.That(group.titleContent.image, Is.Not.Null);
            }
        }

        [Test]
        public void UnsavedMemberRemainsInTheContainersNativeViewHierarchy()
        {
            var group = Group(first);
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
            var group = Group(first);
            var other = Window(3, true); var destination = Dock(other);
            first.SetDirty(true);
            try
            {
                JEEWindowGroups.SetCollapsed(group, true);
                Move(group, destination);
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
        public void FailedExpandedGroupMoveRestoresMembershipAndOrder()
        {
            var group = Group(first, second);
            var after = Window(41); Move(after, dock);
            anchor.Focus();
            var other = Window(3, true); var destination = Dock(other);
            var sourceOrder = Tabs(dock); var destinationOrder = Tabs(destination);
            second.throwOnNextAdd = true;
            LogAssert.Expect(LogType.Error, "Window Groups: Disposable window rejected the destination");
            Move(group, destination);
            JEEWindowGroups.Reconcile();
            Assert.That(Dock(group), Is.SameAs(dock));
            Assert.That(group.collapsed, Is.False);
            CollectionAssert.AreEqual(new EditorWindow[] { first, second }, group.members);
            CollectionAssert.AreEqual(sourceOrder, Tabs(dock));
            CollectionAssert.AreEqual(destinationOrder, Tabs(destination));
        }
        [Test]
        public void MovingCollapsedHeadingDoesNotReactivateMembersDuringNativeDetach()
        {
            var group = Group(first, second);
            JEEWindowGroups.SetCollapsed(group, true);
            var parking = Dock(first);
            var other = Window(3, true); var destination = Dock(other);
            second.throwOnNextAdd = true;
            try
            {
                Move(group, destination);
                JEEWindowGroups.Reconcile();
                Assert.That(group.collapsed, Is.True);
                Assert.That(Dock(first), Is.SameAs(parking));
                Assert.That(Dock(second), Is.SameAs(parking));
                Assert.That(second.throwOnNextAdd, Is.True, "Moving a collapsed group must not re-add or activate its members.");
                CollectionAssert.AreEqual(new EditorWindow[] { anchor }, Tabs(dock));
                CollectionAssert.AreEqual(new EditorWindow[] { other, group }, Tabs(destination));
            }
            finally { second.throwOnNextAdd = false; }
        }
    }
}
