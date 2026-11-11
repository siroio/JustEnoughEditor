using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace JustEnoughEditor.Tests
{
    public class JEEAssetUtilityTests
    {
        private string testRoot;
        private UnityEngine.Object savedSelection;

        [SetUp]
        public void SetUp()
        {
            testRoot = $"Assets/__JEEAssetUtilityTests_{Guid.NewGuid():N}";
            savedSelection = Selection.activeObject;
            Directory.CreateDirectory(testRoot);
            AssetDatabase.Refresh();
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(testRoot);
            AssetDatabase.Refresh();
            Selection.activeObject = savedSelection;
        }

        [Test]
        public void NullAndUnknownObjectsAreNotAssets()
        {
            Assert.That(JEEAssetUtility.IsAsset(null), Is.False);
            var transient = ScriptableObject.CreateInstance<TestData>();
            try
            {
                Assert.That(JEEAssetUtility.IsAsset(transient), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(transient);
            }
        }

        [TestCase("FavoriteAssetsWindow")]
        [TestCase("FavoriteFoldersWindow")]
        public void SharedDataDirectoryIsRelativeToTheEditorScript(string scriptName)
        {
            var guids = AssetDatabase.FindAssets(scriptName + " t:Script");
            Assert.That(guids, Is.Not.Empty);
            var scriptPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            var expected = Path.GetDirectoryName(Path.GetDirectoryName(scriptPath)).Replace('\\', '/') + "/Data";
            Assert.That(JEEAssetUtility.GetSharedDataDirectory(scriptName), Is.EqualTo(expected));
        }

        [Test]
        public void MissingScriptUsesTheDefaultSharedDataDirectory()
        {
            Assert.That(JEEAssetUtility.GetSharedDataDirectory("__MissingJEE_" + Guid.NewGuid().ToString("N")),
                Is.EqualTo("Assets/JustEnoughEditor/Data"));
            Assert.Throws<ArgumentException>(() => JEEAssetUtility.GetSharedDataDirectory(""));
        }

        [Test]
        public void CreatedAssetHasAValidPath()
        {
            var asset = JEEAssetUtility.LoadOrCreateAsset<TestData>($"{testRoot}/Data.asset");
            Assert.That(JEEAssetUtility.IsAsset(asset), Is.True);
            Assert.That(JEEAssetUtility.IsValidAssetPath($"{testRoot}/Data.asset"), Is.True);
            Assert.That(JEEAssetUtility.IsValidAssetPath($"{testRoot}/Missing.asset"), Is.False);
        }

        [Test]
        public void LoadOrCreateReturnsTheExistingInstance()
        {
            var first = JEEAssetUtility.LoadOrCreateAsset<TestData>($"{testRoot}/Nested/Data.asset");
            var second = JEEAssetUtility.LoadOrCreateAsset<TestData>($"{testRoot}/Nested/Data.asset");
            Assert.That(second, Is.SameAs(first));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("Packages/com.example/Data.asset")]
        [TestCase("Assets/Data.txt")]
        [TestCase("Assets/../Data.asset")]
        public void LoadOrCreateRejectsInvalidCreationPaths(string path)
        {
            Assert.Throws<ArgumentException>(() => JEEAssetUtility.LoadOrCreateAsset<TestData>(path));
        }

        [Test]
        public void LoadOrCreateRejectsAnExistingDifferentType()
        {
            var path = $"{testRoot}/Data.asset";
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<OtherData>(), path);
            Assert.Throws<InvalidOperationException>(() => JEEAssetUtility.LoadOrCreateAsset<TestData>(path));
        }

        [Test]
        public void MissingAssetDoesNotChangeSelection()
        {
            var previous = ScriptableObject.CreateInstance<TestData>();
            Selection.activeObject = previous;
            Assert.That(JEEAssetUtility.TryOpenAsset($"{testRoot}/Missing.asset"), Is.False);
            Assert.That(Selection.activeObject, Is.SameAs(previous));
            UnityEngine.Object.DestroyImmediate(previous);
        }

        [Test]
        public void ExistingAssetOpensAndBecomesSelected()
        {
            var path = $"{testRoot}/Data.asset";
            var asset = JEEAssetUtility.LoadOrCreateAsset<TestData>(path);
            Assert.That(JEEAssetUtility.TryOpenAsset(path), Is.True);
            Assert.That(Selection.activeObject, Is.SameAs(asset));
        }

        private class TestData : ScriptableObject
        {
        }

        private class OtherData : ScriptableObject
        {
        }
    }
}
