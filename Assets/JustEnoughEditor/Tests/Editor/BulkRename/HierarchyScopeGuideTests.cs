using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace JustEnoughEditor.Tests
{
    public class HierarchyScopeGuideTests
    {
        [Test]
        public void CollapsedChildIsLastVisibleItemInSelectedScope()
        {
            var root = new GameObject("Selected Root");
            var collapsedChild = new GameObject("Collapsed Child");
            var hiddenGrandchild = new GameObject("Hidden Grandchild");
            collapsedChild.transform.SetParent(root.transform);
            hiddenGrandchild.transform.SetParent(collapsedChild.transform);

            try
            {
                var windowType = typeof(EditorWindow).Assembly.GetType("UnityEditor.SceneHierarchyWindow");
                Assert.That(windowType, Is.Not.Null);

                var window = EditorWindow.GetWindow(windowType);
                window.Focus();

                var setExpanded = windowType.GetMethod(
                    "SetExpanded",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(int), typeof(bool) },
                    null);
                Assert.That(setExpanded, Is.Not.Null);
                setExpanded.Invoke(window, new object[] { root.GetInstanceID(), true });
                setExpanded.Invoke(window, new object[] { collapsedChild.GetInstanceID(), false });

                var drawerType = Type.GetType(
                    "JustEnoughEditor.HierarchyColorAndIconDrawer, Assembly-CSharp-Editor");
                Assert.That(drawerType, Is.Not.Null);

                drawerType.GetMethod("Initialize", BindingFlags.Static | BindingFlags.NonPublic)
                    ?.Invoke(null, null);
                var isLastVisible = drawerType.GetMethod(
                    "IsLastVisibleInSelectedScope",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(isLastVisible, Is.Not.Null);

                var result = (bool)isLastVisible.Invoke(
                    null,
                    new object[] { collapsedChild.transform, root.transform });

                Assert.That(result, Is.True,
                    "A collapsed child's hidden descendants must not extend the scope guide.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
