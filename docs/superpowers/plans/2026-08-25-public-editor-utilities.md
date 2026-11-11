# Public Editor Utilities Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish a small Editor-only utility assembly that other Unity asmdefs can reference for asset, hierarchy, and IMGUI helpers.

**Architecture:** Create `JustEnoughEditor.Utilities` beneath `Editor/Utilities`, move the existing `JEEEditorStyles` into it, and add one static class each for asset and hierarchy operations. Existing JEE windows remain in their current assembly and consume the new auto-referenced assembly; a dedicated test asmdef proves outside consumers can compile against the public API.

**Tech Stack:** Unity 6 Editor APIs, C#, asmdef, IMGUI, NUnit EditMode tests.

**Spec:** `docs/superpowers/specs/2026-08-25-public-editor-utilities-design.md`

## Global Constraints

- Support Unity 6.0 and later.
- Keep every API Editor-only and in namespace `JustEnoughEditor`.
- Add no dependency, callback wrapper, registration system, interface, factory, or speculative helper.
- Preserve favorite GUIDs, EditorPrefs data, shared asset paths, migration behavior, and colors.
- Preserve the GUID of `JEEEditorStyles.cs.meta` when moving the file.
- Preserve all unrelated working-tree changes, especially the existing Bulk Rename edits.
- Do not commit or push unless explicitly requested.

## File Map

- Create `Assets/JustEnoughEditor/Editor/Utilities/JustEnoughEditor.Utilities.asmdef`: public Editor-only assembly boundary.
- Move `Assets/JustEnoughEditor/Editor/JEEEditorStyles.cs` and `.meta` to `Editor/Utilities/`: existing public IMGUI API.
- Create `Assets/JustEnoughEditor/Editor/Utilities/JEEAssetUtility.cs`: asset queries, opening, and ScriptableObject asset creation.
- Create `Assets/JustEnoughEditor/Editor/Utilities/JEEHierarchyUtility.cs`: hierarchy depth and path operations.
- Create `Assets/JustEnoughEditor/Tests/Editor/Utilities/JustEnoughEditor.Utilities.Tests.asmdef`: external-consumer test boundary.
- Create `Assets/JustEnoughEditor/Tests/Editor/Utilities/JEEAssetUtilityTests.cs`: asset behavior tests.
- Create `Assets/JustEnoughEditor/Tests/Editor/Utilities/JEEHierarchyUtilityTests.cs`: hierarchy behavior tests.
- Create `Assets/JustEnoughEditor/Tests/Editor/Utilities/JEEEditorStylesTests.cs`: public IMGUI reachability tests.
- Modify `FavoriteAssetsWindow.cs`, `FavoriteFoldersWindow.cs`, `BulkRenameWindow.cs`, `HierarchyColorAndIconDrawer.cs`, and `MissingScriptFinderWindow.cs`: replace local implementations with the public utilities.
- Modify `README.md`: document asmdef setup and one example per utility class.

---

### Task 1: Establish the Public Assembly Boundary

**Files:**
- Create: `Assets/JustEnoughEditor/Tests/Editor/BulkRename/PublicEditorUtilitiesSmokeTests.cs`
- Create: `Assets/JustEnoughEditor/Editor/Utilities/JustEnoughEditor.Utilities.asmdef`
- Move: `Assets/JustEnoughEditor/Editor/JEEEditorStyles.cs`
- Move: `Assets/JustEnoughEditor/Editor/JEEEditorStyles.cs.meta`

**Interfaces:**
- Consumes: existing public static class `JustEnoughEditor.JEEEditorStyles`.
- Produces: assembly `JustEnoughEditor.Utilities` containing `JEEEditorStyles` with unchanged members.

- [ ] **Step 1: Write the failing assembly smoke test**

Add this test to the existing test asmdef so it can fail before the new assembly exists:

```csharp
using System;
using System.Linq;
using NUnit.Framework;

namespace JustEnoughEditor.Tests
{
    public class PublicEditorUtilitiesSmokeTests
    {
        [Test]
        public void UtilitiesAssemblyIsAvailableToEditorConsumers()
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .SingleOrDefault(candidate => candidate.GetName().Name == "JustEnoughEditor.Utilities");

            Assert.That(assembly, Is.Not.Null);
            Assert.That(assembly.GetType("JustEnoughEditor.JEEEditorStyles"), Is.Not.Null);
        }
    }
}
```

- [ ] **Step 2: Run the smoke test and verify RED**

Run Unity 6000.3.7 EditMode tests with filter:

```text
JustEnoughEditor.Tests.PublicEditorUtilitiesSmokeTests.UtilitiesAssemblyIsAvailableToEditorConsumers
```

Expected: FAIL because `JustEnoughEditor.Utilities` is absent.

- [ ] **Step 3: Create the utility asmdef**

Create the directory and add:

```json
{
    "name": "JustEnoughEditor.Utilities",
    "rootNamespace": "JustEnoughEditor",
    "references": [],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 4: Move `JEEEditorStyles` without changing content or GUID**

Verify both source paths resolve inside the workspace, create `Editor/Utilities`, then move the `.cs` and `.meta` files together. Re-read the destination `.meta` and confirm its GUID matches the source value recorded before the move.

- [ ] **Step 5: Run the smoke test and verify GREEN**

Expected: PASS, with no compilation errors and the same public `JEEEditorStyles` type loaded from `JustEnoughEditor.Utilities`.

---

### Task 2: Add `JEEAssetUtility`

**Files:**
- Create: `Assets/JustEnoughEditor/Editor/Utilities/JEEAssetUtility.cs`
- Create: `Assets/JustEnoughEditor/Tests/Editor/Utilities/JustEnoughEditor.Utilities.Tests.asmdef`
- Create: `Assets/JustEnoughEditor/Tests/Editor/Utilities/JEEAssetUtilityTests.cs`

**Interfaces:**
- Consumes: `AssetDatabase`, `Selection`, `ScriptableObject`, `System.IO`.
- Produces:
  - `bool JEEAssetUtility.IsAsset(UnityEngine.Object target)`
  - `bool JEEAssetUtility.IsValidAssetPath(string assetPath)`
  - `bool JEEAssetUtility.TryOpenAsset(string assetPath)`
  - `T JEEAssetUtility.LoadOrCreateAsset<T>(string assetPath) where T : ScriptableObject`

- [ ] **Step 1: Create the external-consumer test asmdef**

```json
{
    "name": "JustEnoughEditor.Utilities.Tests",
    "rootNamespace": "JustEnoughEditor.Tests",
    "references": [
        "JustEnoughEditor.Utilities",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": true,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: Write asset tests before the production class**

Use a unique test root such as `Assets/__JEEAssetUtilityTests_<guid>` and save the prior `Selection.activeObject`. Tests must assert these literal outcomes:

```csharp
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
```

The fixture's `SetUp` must create `testRoot` with `Directory.CreateDirectory(testRoot)` followed by `AssetDatabase.Refresh()`. Its `TearDown` must delete only `testRoot` with `AssetDatabase.DeleteAsset(testRoot)`, refresh the AssetDatabase, and restore the saved selection. Define `TestData` and `OtherData` as private nested `ScriptableObject` test types.

- [ ] **Step 3: Run focused tests and verify RED**

Expected: compilation fails only because `JEEAssetUtility` does not exist. Do not change assertions to make this green.

- [ ] **Step 4: Implement the minimal asset utility**

```csharp
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JustEnoughEditor
{
    public static class JEEAssetUtility
    {
        public static bool IsAsset(Object target)
        {
            return target != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(target));
        }

        public static bool IsValidAssetPath(string assetPath)
        {
            return !string.IsNullOrEmpty(assetPath) &&
                   !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath));
        }

        public static bool TryOpenAsset(string assetPath)
        {
            var asset = string.IsNullOrEmpty(assetPath)
                ? null
                : AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (asset == null || !AssetDatabase.OpenAsset(asset)) return false;

            Selection.activeObject = asset;
            return true;
        }

        public static T LoadOrCreateAsset<T>(string assetPath) where T : ScriptableObject
        {
            assetPath = ValidateCreationPath(assetPath);

            var existing = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (existing != null)
            {
                if (existing is T typed) return typed;
                throw new InvalidOperationException($"An asset of type {existing.GetType().FullName} already exists at {assetPath}.");
            }

            var directory = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(directory))
            {
                Directory.CreateDirectory(directory);
                AssetDatabase.Refresh();
            }

            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssets();
            return asset;
        }

        private static string ValidateCreationPath(string assetPath)
        {
            var normalized = assetPath?.Replace('\\', '/');
            var traversesParent = normalized != null &&
                                  (normalized.Contains("/../") ||
                                   normalized.EndsWith("/..", StringComparison.Ordinal) ||
                                   normalized.Contains("/./") ||
                                   normalized.EndsWith("/.", StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(normalized) ||
                Path.IsPathRooted(normalized) ||
                !normalized.StartsWith("Assets/", StringComparison.Ordinal) ||
                !string.Equals(Path.GetExtension(normalized), ".asset", StringComparison.OrdinalIgnoreCase) ||
                traversesParent)
                throw new ArgumentException("Asset path must be a .asset path under Assets/.", nameof(assetPath));

            return normalized;
        }
    }
}
```

- [ ] **Step 5: Run focused asset tests and verify GREEN**

Expected: all asset utility tests pass. If Unity batch mode cannot open a valid test asset, keep the invalid-path side-effect test and record live Editor opening as manual validation; do not fake `AssetDatabase.OpenAsset`.

---

### Task 3: Add `JEEHierarchyUtility`

**Files:**
- Create: `Assets/JustEnoughEditor/Editor/Utilities/JEEHierarchyUtility.cs`
- Create: `Assets/JustEnoughEditor/Tests/Editor/Utilities/JEEHierarchyUtilityTests.cs`
- Create: `Assets/JustEnoughEditor/Tests/Editor/Utilities/JEEEditorStylesTests.cs`

**Interfaces:**
- Consumes: `UnityEngine.Transform`, existing `JEEEditorStyles`.
- Produces:
  - `int JEEHierarchyUtility.GetDepth(Transform transform)`
  - `string JEEHierarchyUtility.GetPath(Transform transform)`

- [ ] **Step 1: Write failing hierarchy and IMGUI accessibility tests**

```csharp
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

[Test]
public void PublicImguiStylesAreReachableAcrossTheAssemblyBoundary()
{
    Assert.That(JEEEditorStyles.Card, Is.Not.Null);
    Assert.That(JEEEditorStyles.HeaderTitle, Is.Not.Null);
    Assert.That(JEEEditorStyles.SearchFieldStyle(), Is.Not.Null);
}
```

- [ ] **Step 2: Run focused hierarchy tests and verify RED**

Expected: compilation fails only because `JEEHierarchyUtility` is absent; `JEEEditorStyles` resolves through the utility asmdef.

- [ ] **Step 3: Implement the minimal hierarchy utility**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace JustEnoughEditor
{
    public static class JEEHierarchyUtility
    {
        public static int GetDepth(Transform transform)
        {
            if (transform == null) throw new ArgumentNullException(nameof(transform));

            var depth = 0;
            while (transform.parent != null)
            {
                depth++;
                transform = transform.parent;
            }

            return depth;
        }

        public static string GetPath(Transform transform)
        {
            if (transform == null) throw new ArgumentNullException(nameof(transform));

            var names = new Stack<string>();
            while (transform != null)
            {
                names.Push(transform.name);
                transform = transform.parent;
            }

            return string.Join("/", names);
        }
    }
}
```

- [ ] **Step 4: Run focused utility tests and verify GREEN**

Expected: asset, hierarchy, and IMGUI accessibility tests all pass from `JustEnoughEditor.Utilities.Tests`.

---

### Task 4: Replace Existing Private Helpers

**Files:**
- Modify: `Assets/JustEnoughEditor/Editor/FavoriteAssetsWindow.cs`
- Modify: `Assets/JustEnoughEditor/Editor/FavoriteFoldersWindow.cs`
- Modify: `Assets/JustEnoughEditor/Editor/BulkRenameWindow.cs`
- Modify: `Assets/JustEnoughEditor/Editor/HierarchyColorAndIconDrawer.cs`
- Modify: `Assets/JustEnoughEditor/Editor/MissingScriptFinderWindow.cs`

**Interfaces:**
- Consumes: public APIs created in Tasks 2 and 3.
- Produces: the same window behavior with redundant local helpers removed.

- [ ] **Step 1: Replace asset helpers at their call sites**

Make only these mechanical substitutions:

```csharp
// FavoriteAssetsWindow
JEEAssetUtility.IsValidAssetPath(path)
JEEAssetUtility.TryOpenAsset(path)
var dataAsset = JEEAssetUtility.LoadOrCreateAsset<FavoriteAssetsData>(SharedDataPath);

// FavoriteFoldersWindow
var dataAsset = JEEAssetUtility.LoadOrCreateAsset<FavoriteFoldersData>(SharedDataPath);

// BulkRenameWindow
JEEAssetUtility.IsAsset(target)
```

Delete `FavoriteAssetsWindow.IsValidAssetPath`, `FavoriteAssetsWindow.OpenItem`, and `BulkRenameWindow.IsAsset`. In both favorite windows, replace only the create-if-missing block inside `SaveData`; retain read-only `LoadAssetAtPath` calls and the existing list-copy/save logic.

- [ ] **Step 2: Replace hierarchy helpers at their call sites**

Replace every `HierarchyColorAndIconDrawer.GetDepth(transform)` call with `JEEHierarchyUtility.GetDepth(transform)` and delete its private `GetDepth`. Replace `MissingScriptFinderWindow.GetHierarchyPath(target)` with `JEEHierarchyUtility.GetPath(target.transform)` and delete the private helper.

- [ ] **Step 3: Run the complete EditMode suite**

Expected: all existing tests plus the new utility tests pass. Confirm the previously added collapsed-scope regression test remains green.

- [ ] **Step 4: Inspect the scoped diff**

Confirm there are no formatting-only rewrites and no changes to the user-owned Bulk Rename logic outside the deleted `IsAsset` helper and its call sites.

---

### Task 5: Document and Verify the Public Contract

**Files:**
- Modify: `README.md`
- Remove after replacement: `Assets/JustEnoughEditor/Tests/Editor/BulkRename/PublicEditorUtilitiesSmokeTests.cs` and its generated `.meta`

**Interfaces:**
- Consumes: final public utility assembly.
- Produces: end-user setup and usage documentation.

- [ ] **Step 1: Add the README public API section**

Document the asmdef reference:

```json
"references": [
  "JustEnoughEditor.Utilities"
]
```

Add these minimal examples and state that they belong in an `Editor` folder or Editor-only asmdef:

```csharp
var data = JEEAssetUtility.LoadOrCreateAsset<MyEditorData>("Assets/Editor/MyEditorData.asset");
Debug.Log(JEEHierarchyUtility.GetPath(Selection.activeTransform));

JEEEditorStyles.BeginCard("My Tool");
EditorGUILayout.LabelField("Ready");
JEEEditorStyles.EndCard();
```

- [ ] **Step 2: Remove the temporary reflection smoke test**

The compile-time `JustEnoughEditor.Utilities.Tests` assembly now proves the same boundary more strongly. Remove only the temporary smoke `.cs` and its matching `.meta`.

- [ ] **Step 3: Run fresh Unity 6000.3.7 verification**

Run focused `JustEnoughEditor.Utilities.Tests` EditMode tests, then the full EditMode suite. Require result XML with zero failures; a Unity process exit code without a result file is not a verdict.

- [ ] **Step 4: Verify Unity 6.0 compatibility in an isolated copy**

Create `Temp/JEE-Unity6000-Validation`, copy only `Assets`, `Packages`, and `ProjectSettings`, and change only the copied `ProjectVersion.txt` to installed editor `6000.0.58f2`. Run the focused utility tests with:

```text
C:\Program Files\Unity\Hub\Editor\6000.0.58f2\Editor\Unity.exe
```

Require a test-result XML with zero failures. Do not open the working project with the older editor. After checking the resolved absolute validation path is inside the workspace `Temp` directory, delete that generated validation copy.

- [ ] **Step 5: Final checks**

Run `git diff --check` on the scoped files, inspect the utility assembly and test asmdef contents, verify the moved `JEEEditorStyles.cs.meta` GUID, and report `git status --short`. Leave all changes uncommitted.
