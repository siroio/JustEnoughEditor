using System;
using NUnit.Framework;
using UnityEngine;

namespace JustEnoughEditor.Tests
{
    public class JEEHierarchyUtilityTests
    {
        [Test]
        public void RootAndChildHaveExpectedDepthAndPath()
        {
            var root = new GameObject("Root");
            var child = new GameObject("Child");
            var leaf = new GameObject("Leaf");
            try
            {
                child.transform.SetParent(root.transform);
                leaf.transform.SetParent(child.transform);
                Assert.That(JEEHierarchyUtility.GetDepth(root.transform), Is.EqualTo(0));
                Assert.That(JEEHierarchyUtility.GetDepth(leaf.transform), Is.EqualTo(2));
                Assert.That(JEEHierarchyUtility.GetPath(leaf.transform), Is.EqualTo("Root/Child/Leaf"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void NullHierarchyInputsThrow()
        {
            Assert.Throws<ArgumentNullException>(() => JEEHierarchyUtility.GetDepth(null));
            Assert.Throws<ArgumentNullException>(() => JEEHierarchyUtility.GetPath(null));
        }
    }
}
