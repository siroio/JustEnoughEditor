using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace JustEnoughEditor
{
    // All version-sensitive docking operations live here. No native methods are patched.
    internal static class JEEDockAdapter
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Type DockType, ViewType, HostType;
        private static readonly FieldInfo WindowParent, Panes, Destroying, Contents;
        private static readonly PropertyInfo ViewParent, Container, Position, Selected, Actual, ShowMode;
        private static readonly MethodInfo AddTab, RemoveTab, AddChild, RemoveChild, KillIfEmpty, Repaint, UnsavedChanged;
        private static readonly EventInfo ActiveChanged;
        private static readonly FieldInfo ScrollToTab;
        private static readonly PropertyInfo DockVisualTree;
        private static readonly FieldInfo TabArea, TabStyle, TabScroll, ScrollLeft, ScrollRight, Dragging;
        private static readonly MethodInfo TabWidth;
        private static readonly PropertyInfo MenuItems = typeof(GenericMenu).GetProperty("menuItems", Flags);
        private static readonly FieldInfo MenuCallback = typeof(GenericMenu).GetNestedType("MenuItem", Flags)?.GetField("func2", Flags);
        private static readonly MethodInfo AddTabToHere;
        private static Delegate activeChangedHandler;
        private static EventInfo defaultMenuItems;
        private static readonly Action<GenericMenu, EditorWindow> menuHandler = OnPopulateTabMenu;
        internal static string UnavailableReason { get; private set; }
        internal static bool IsDragging => Dragging != null && (bool)Dragging.GetValue(null);
        internal static event Action<ScriptableObject, EditorWindow> Activated;
        internal static event Action<GenericMenu, EditorWindow> PopulateTabMenu;

        static JEEDockAdapter()
        {
            try
            {
                var assembly = typeof(EditorWindow).Assembly;
                DockType = assembly.GetType("UnityEditor.DockArea", true);
                ViewType = assembly.GetType("UnityEditor.View", true);
                HostType = assembly.GetType("UnityEditor.HostView", true);
                WindowParent = typeof(EditorWindow).GetField("m_Parent", Flags);
                Panes = DockType.GetField("m_Panes", Flags);
                Destroying = DockType.GetField("m_IsBeingDestroyed", Flags);
                Contents = DockType.GetField("s_GUIContents", Flags);
                ViewParent = ViewType.GetProperty("parent", Flags);
                Container = ViewType.GetProperty("window", Flags);
                Position = ViewType.GetProperty("position", Flags);
                Selected = DockType.GetProperty("selected", Flags);
                Actual = HostType.GetProperty("actualView", Flags);
                var containerType = assembly.GetType("UnityEditor.ContainerWindow", true);
                ShowMode = containerType.GetProperty("showMode", Flags);
                UnsavedChanged = containerType.GetMethod("UnsavedStateChanged", Flags);
                AddTab = DockType.GetMethod("AddTab", Flags, null, new[] { typeof(int), typeof(EditorWindow), typeof(bool) }, null);
                AddTabToHere = DockType.GetMethod("AddTabToHere", Flags, null, new[] { typeof(object) }, null);
                RemoveTab = DockType.GetMethod("RemoveTab", Flags, null, new[] { typeof(EditorWindow), typeof(bool), typeof(bool) }, null);
                AddChild = ViewType.GetMethod("AddChild", Flags, null, new[] { ViewType }, null);
                RemoveChild = ViewType.GetMethod("RemoveChild", Flags, null, new[] { ViewType }, null);
                KillIfEmpty = DockType.GetMethod("KillIfEmpty", Flags);
                Repaint = HostType.GetMethod("Repaint", Flags, null, Type.EmptyTypes, null);
                ActiveChanged = HostType.GetEvent("actualViewChanged", Flags);
                ScrollToTab = DockType.GetField("newlyAddedTabIndex", Flags);
                DockVisualTree = assembly.GetType("UnityEditor.GUIView", true).GetProperty("visualTree", Flags);
                TabArea = DockType.GetField("m_TabAreaRect", Flags);
                TabStyle = DockType.GetField("tabStyle", Flags);
                TabScroll = DockType.GetField("m_ScrollOffset", Flags);
                ScrollLeft = DockType.GetField("m_ScrollLeftRect", Flags);
                ScrollRight = DockType.GetField("m_ScrollRightRect", Flags);
                Dragging = DockType.GetField("s_IsDragging", Flags);
                TabWidth = DockType.GetMethod("GetTabWidth", Flags, null, new[] { typeof(GUIStyle), typeof(int) }, null);
                if (new MemberInfo[] { WindowParent, Panes, Destroying, Contents, ViewParent, Container, Position,
                    Selected, Actual, ShowMode, AddTab, RemoveTab, AddChild, RemoveChild, KillIfEmpty, Repaint, UnsavedChanged, ActiveChanged }.Any(m => m == null))
                    throw new NotSupportedException("Required Unity docking members are missing.");
                activeChangedHandler = Delegate.CreateDelegate(ActiveChanged.EventHandlerType,
                    typeof(JEEDockAdapter).GetMethod(nameof(OnActiveChanged), Flags));
                ActiveChanged.GetAddMethod(true).Invoke(null, new object[] { activeChangedHandler });
                AssemblyReloadEvents.beforeAssemblyReload += Unsubscribe;
            }
            catch (Exception ex)
            {
                UnavailableReason = "Window Groups is unavailable in Unity " + Application.unityVersion + ": " + ex.GetBaseException().Message;
            }
            // This optional entry point must not disable grouping on versions
            // without a tab-menu hook. The main JEE menu remains available.
            if (UnavailableReason == null)
            {
                try
                {
                    defaultMenuItems = HostType.GetEvent("populateDefaultMenuItems", Flags);
                    if (defaultMenuItems == null) throw new NotSupportedException("The native tab-menu hook is missing.");
                    defaultMenuItems.GetAddMethod(true).Invoke(null, new object[] { menuHandler });
                }
                catch (Exception ex)
                {
                    defaultMenuItems = null;
                    Debug.LogWarning("Window Groups: tab context menus are unavailable. Use the JEE menu. " + ex.GetBaseException().Message);
                }
            }
        }
        internal static void RequireSupport()
        {
            if (UnavailableReason != null) throw new NotSupportedException(UnavailableReason);
        }
        internal static void RequireRecoverySupport()
        {
            if (DockType == null || new MemberInfo[] { WindowParent, Panes, Destroying, AddTab, RemoveTab, ViewParent, RemoveChild }.Any(m => m == null))
                throw new NotSupportedException("This Unity version cannot restore grouped tabs automatically. Keep the layout and reopen it with the Unity version that created it.");
        }
        private static void Unsubscribe()
        {
            if (activeChangedHandler != null) ActiveChanged.GetRemoveMethod(true).Invoke(null, new object[] { activeChangedHandler });
            activeChangedHandler = null;
            defaultMenuItems?.GetRemoveMethod(true).Invoke(null, new object[] { menuHandler });
            defaultMenuItems = null;
        }
        private static void OnPopulateTabMenu(GenericMenu menu, EditorWindow window) => PopulateTabMenu?.Invoke(menu, window);
        internal static void AfterNewTabFromMenu(GenericMenu menu, ScriptableObject dock, Action<EditorWindow[]> added)
        {
            if (MenuItems == null || MenuCallback == null || AddTabToHere == null) return;
            // HostView raises the menu event before DockArea appends its native Add Tab items.
            // Keep their labels, types and creation callbacks; decorate only this menu instance.
            EditorApplication.delayCall += () =>
            {
                foreach (var item in (IEnumerable)MenuItems.GetValue(menu))
                {
                    var create = MenuCallback.GetValue(item) as GenericMenu.MenuFunction2;
                    if (create == null || create.Method != AddTabToHere || !ReferenceEquals(create.Target, dock)) continue;
                    MenuCallback.SetValue(item, (GenericMenu.MenuFunction2)(data =>
                    {
                        if (!IsOrdinaryDock(dock)) return;
                        var before = Tabs(dock);
                        create(data);
                        added(Tabs(dock).Except(before).ToArray());
                    }));
                }
            };
        }
        private static void OnActiveChanged(object host)
        {
            if (host is ScriptableObject obj && obj) Activated?.Invoke(obj, Actual.GetValue(host) as EditorWindow);
        }
        internal static ScriptableObject Parent(EditorWindow window) => window ? WindowParent?.GetValue(window) as ScriptableObject : null;
        internal static ScriptableObject Dock(EditorWindow window)
        {
            var parent = Parent(window);
            return IsDock(parent) ? parent : null;
        }
        internal static void UpdateGroupIndicator(JEEWindowGroup group, ref IMGUIContainer indicator)
        {
            var dock = group.ownerDock;
            if (UnavailableReason != null || !IsOrdinaryDock(dock) || DockVisualTree == null
                || TabArea == null || TabStyle == null || TabScroll == null || ScrollLeft == null || ScrollRight == null || TabWidth == null)
            {
                indicator?.RemoveFromHierarchy();
                return;
            }
            var root = DockVisualTree.GetValue(dock) as VisualElement;
            if (root == null) return;
            if (indicator == null)
            {
                // Decoration only: never take focus, mouse events or native tab ownership.
                indicator = new IMGUIContainer(() =>
                {
                    if (!group || group.disposing || Event.current.type != EventType.Repaint) return;
                    var rect = GroupIndicatorBounds(group);
                    if (rect.width <= 2) return;
                    EditorGUI.DrawRect(new Rect(rect.x + 1, rect.yMax - 3, rect.width - 2, 3), group.groupColor);
                    EditorGUI.DrawRect(new Rect(rect.x + 1, rect.yMax - 6, 2, 3), group.groupColor);
                    EditorGUI.DrawRect(new Rect(rect.xMax - 3, rect.yMax - 6, 2, 3), group.groupColor);
                }) { name = "jee-group-" + group.groupId, pickingMode = PickingMode.Ignore, focusable = false };
                indicator.style.position = UnityEngine.UIElements.Position.Absolute;
                indicator.style.left = indicator.style.right = indicator.style.top = indicator.style.bottom = 0;
            }
            if (indicator.parent != root) root.Add(indicator);
            indicator.MarkDirtyRepaint();
        }

        internal static Rect GroupIndicatorBounds(JEEWindowGroup group)
        {
            var dock = group.ownerDock;
            if (!IsOrdinaryDock(dock) || group.members.Any(w => w && w.maximized) || IsDragging) return Rect.zero;
            var area = (Rect)TabArea.GetValue(dock);
            var style = TabStyle.GetValue(dock) as GUIStyle;
            if (style == null || area.width <= 0) return Rect.zero;
            var tabs = Tabs(dock);
            var start = Array.FindIndex(tabs, w => group.members.Contains(w));
            if (start < 0) return Rect.zero;
            var end = start;
            while (!group.collapsed && end + 1 < tabs.Length && group.members.Contains(tabs[end + 1])) end++;
            var x = area.xMin - (float)TabScroll.GetValue(dock) - 1;
            var left = x;
            for (var i = 0; i <= end; i++)
            {
                if (i == start) left = x;
                x += (float)TabWidth.Invoke(dock, new object[] { style, i });
            }
            var clipLeft = area.xMin;
            var clipRight = area.xMax;
            var previous = (Rect)ScrollLeft.GetValue(dock);
            var next = (Rect)ScrollRight.GetValue(dock);
            if (previous.width > 0 && previous.Overlaps(area)) clipLeft = Mathf.Max(clipLeft, previous.xMax);
            if (next.width > 0 && next.Overlaps(area)) clipRight = Mathf.Min(clipRight, next.xMin);
            left = Mathf.Max(left, clipLeft);
            var right = Mathf.Min(x, clipRight);
            return right > left ? new Rect(left, area.y, right - left, area.height) : Rect.zero;
        }

        internal static void KeepTabVisible(EditorWindow window)
        {
            var dock = Dock(window);
            var index = Array.IndexOf(Tabs(dock), window);
            if (index < 0 || ScrollToTab == null) return;
            // Adding members schedules native scrolling to the last added tab.
            // Keep the selected real member visible through that same native mechanism.
            ScrollToTab.SetValue(dock, index);
            Repaint.Invoke(dock, null);
        }
        internal static bool IsDock(ScriptableObject view) => view && DockType != null && DockType.IsInstanceOfType(view);
        internal static bool IsDestroying(ScriptableObject dock) => !dock || (bool)Destroying.GetValue(dock);
        internal static EditorWindow Active(ScriptableObject dock) => dock ? Actual?.GetValue(dock) as EditorWindow : null;
        internal static EditorWindow[] Tabs(ScriptableObject dock) => IsDock(dock)
            ? ((IEnumerable)Panes.GetValue(dock)).Cast<EditorWindow>().Where(w => w).ToArray() : Array.Empty<EditorWindow>();
        internal static bool IsOrdinaryDock(ScriptableObject dock)
        {
            if (!IsDock(dock) || IsDestroying(dock)) return false;
            var container = Container.GetValue(dock) as ScriptableObject;
            if (!container) return false;
            var mode = ShowMode.GetValue(container).ToString();
            return mode == "NormalWindow" || mode == "MainWindow";
        }
        internal static void Move(EditorWindow window, ScriptableObject destination, int index = -1)
        {
            RequireRecoverySupport();
            if (!window || !IsDock(destination) || IsDestroying(destination)) throw new ArgumentException("A live window and destination dock are required.");
            var source = Dock(window);
            var sourceIndex = Array.IndexOf(Tabs(source), window);
            var sourceActive = Active(source);
            var destinationActive = Active(destination);
            if (source && sourceIndex < 0) throw new InvalidOperationException("The source dock does not own this window.");
            try
            {
                if (source) RemoveTab.Invoke(source, new object[] { window, false, true });
                var count = Tabs(destination).Length;
                AddTab.Invoke(destination, new object[] { index < 0 ? count : Mathf.Clamp(index, 0, count), window, true });
            }
            catch
            {
                // Third-party OnAddedAsTab can throw after native insertion/parenting.
                if (window && source && !IsDestroying(source))
                {
                    var current = Dock(window);
                    if (current && Tabs(current).Contains(window)) RemoveTab.Invoke(current, new object[] { window, false, true });
                    AddTab.Invoke(source, new object[] { Mathf.Clamp(sourceIndex, 0, Tabs(source).Length), window, true });
                    Select(source, sourceActive);
                    if (destination != source) Select(destination, destinationActive);
                }
                throw;
            }
        }
        internal static JEEWindowGroupParking KeepDockAlive(ScriptableObject dock, JEEWindowGroup group)
        {
            // Native RemoveTab must select the placeholder itself. AddTab would
            // switch the active visual tree reentrantly during native dragging.
            var placeholder = ScriptableObject.CreateInstance<JEEWindowGroupParking>();
            placeholder.owner = group;
            placeholder.titleContent = new GUIContent("Moving Window Group");
            ((IList)Panes.GetValue(dock)).Add(placeholder);
            WindowParent.SetValue(placeholder, dock);
            return placeholder;
        }
        internal static ScriptableObject CreateParking(ScriptableObject owner, JEEWindowGroup group)
        {
            var parking = ScriptableObject.CreateInstance(DockType);
            try
            {
                parking.name = "JEE Window Group Parking";
                Position.SetValue(parking, new Rect(-10000, -10000, 1, 1));
                AddChild.Invoke(owner, new object[] { parking });
                var idle = ScriptableObject.CreateInstance<JEEWindowGroupParking>();
                idle.owner = group;
                idle.titleContent = new GUIContent("Window Group Parking");
                Move(idle, parking);
                return parking;
            }
            catch { DestroyParking(parking); throw; }
        }
        internal static void Idle(ScriptableObject parking)
        {
            var index = Array.FindIndex(Tabs(parking), w => w is JEEWindowGroupParking);
            if (index < 0) throw new InvalidOperationException("The parking host has lost its idle window.");
            Selected.SetValue(parking, index);
        }
        internal static void ReparentParking(ScriptableObject parking, ScriptableObject destination)
        {
            if (!parking || ReferenceEquals(ViewParent.GetValue(parking), destination)) return;
            var previous = Container.GetValue(parking) as ScriptableObject;
            AddChild.Invoke(destination, new object[] { parking });
            var current = Container.GetValue(parking) as ScriptableObject;
            // Moving a View bypasses AddTab's native unsaved-window bookkeeping.
            if (previous) UnsavedChanged.Invoke(previous, null);
            if (current && current != previous) UnsavedChanged.Invoke(current, null);
        }
        internal static void DestroyParking(ScriptableObject parking)
        {
            if (!parking) return;
            if (Tabs(parking).Any(w => !(w is JEEWindowGroupParking)))
                throw new InvalidOperationException("Cannot destroy a parking host that still owns member windows.");
            var parent = ViewParent.GetValue(parking) as ScriptableObject;
            if (parent) RemoveChild.Invoke(parent, new object[] { parking });
            UnityEngine.Object.DestroyImmediate(parking);
        }
        internal static void Select(ScriptableObject dock, EditorWindow window)
        {
            if (!dock || !window) return;
            var index = Array.IndexOf(Tabs(dock), window);
            if (index >= 0)
            {
                if (Selected != null) Selected.SetValue(dock, index);
                else window.Focus();
            }
        }
        internal static void Order(ScriptableObject dock, EditorWindow[] order)
        {
            var current = Tabs(dock);
            if (current.SequenceEqual(order)) return;
            if (current.Length != order.Length || order.Distinct().Count() != order.Length || current.Except(order).Any())
                throw new ArgumentException("Tab reordering must preserve every existing window exactly once.");
            var active = Active(dock);
            var panes = (IList)Panes.GetValue(dock);
            panes.Clear();
            foreach (var window in order) panes.Add(window);
            (Contents.GetValue(null) as IDictionary)?.Clear();
            Select(dock, active);
            Repaint.Invoke(dock, null);
        }
        internal static void CloseEmptyDock(ScriptableObject dock)
        {
            if (IsDock(dock) && !IsDestroying(dock) && Tabs(dock).Length == 0) KillIfEmpty.Invoke(dock, null);
        }
    }
}
