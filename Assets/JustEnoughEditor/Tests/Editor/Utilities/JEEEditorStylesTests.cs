using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace JustEnoughEditor.Tests
{
    public class JEEEditorStylesTests
    {
        [UnityTest]
        public IEnumerator SearchFieldPreservesInputAndClearButtonClearsIt()
        {
            var window = ScriptableObject.CreateInstance<SearchTestWindow>();
            try
            {
                window.ShowUtility();
                window.position = new Rect(100, 100, 400, 100);
                yield return null;
                window.SendEvent(new Event { type = EventType.Layout });
                window.SendEvent(new Event { type = EventType.Repaint });
                Assert.That(window.Query, Is.EqualTo("Player"));
                Assert.That(window.ClearRect.width, Is.GreaterThan(0));

                window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = window.ClearRect.center });
                window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = window.ClearRect.center });
                Assert.That(window.Query, Is.Empty);
            }
            finally
            {
                window.Close();
            }
        }

        private class SearchTestWindow : EditorWindow
        {
            public string Query = "Player";
            public Rect ClearRect;

            private void OnGUI()
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    Query = JEEEditorStyles.DrawSearchField(Query, new GUIContent("Clear"));
                    if (Event.current.type == EventType.Repaint)
                        ClearRect = GUILayoutUtility.GetLastRect();
                }

                JEEEditorStyles.DrawProjectColorBar(new Rect(0, 30, 64, 64), Color.red);
                JEEEditorStyles.DrawProjectColorBar(new Rect(10, 20, 100, 16), Color.blue);
                JEEEditorStyles.DrawProjectColorBar(new Rect(30, 20, 100, 16), Color.clear);
            }
        }

        [Test]
        public void PublicImguiStylesAreReachableAcrossTheAssemblyBoundary()
        {
            var type = typeof(JEEEditorStyles);
            Assert.That(type.Assembly.GetName().Name, Is.EqualTo("JustEnoughEditor.Utilities"));
            Assert.That(type.GetProperty(nameof(JEEEditorStyles.Card)), Is.Not.Null);
            Assert.That(type.GetProperty(nameof(JEEEditorStyles.HeaderTitle)), Is.Not.Null);
            Assert.That(type.GetMethod(nameof(JEEEditorStyles.SearchFieldStyle)), Is.Not.Null);
        }
    }
}
