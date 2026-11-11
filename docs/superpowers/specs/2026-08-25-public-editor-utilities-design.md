# Public Editor Utilities Design

## Goal

Provide a small, documented, Editor-only API that Unity projects can reference from their own assembly definitions. The first release targets Unity 6.0 and later and extracts only behavior already used by Just Enough Editor.

## Non-goals

- Do not wrap Unity's existing editor callbacks or menu APIs.
- Do not add registration systems, interfaces, factories, or settings abstractions for hypothetical extensions.
- Do not change favorite data paths, GUID storage, EditorPrefs formats, or redesign existing windows.
- Do not expose the hierarchy expansion reflection used internally by the scope guide.

## Assembly Boundary

Add `Assets/JustEnoughEditor/Editor/Utilities/JustEnoughEditor.Utilities.asmdef` with:

- assembly name `JustEnoughEditor.Utilities`;
- Editor-only platform support;
- automatic referencing enabled so existing predefined editor assemblies continue to consume it;
- Unity engine and editor references enabled.

Move `JEEEditorStyles.cs` and its `.meta` file into this directory without changing the asset GUID. Add the two new utility classes beside it. External editor assemblies consume the API by referencing `JustEnoughEditor.Utilities` in their own asmdef.

Keep the `JustEnoughEditor` namespace and existing `JEE` class prefix. This avoids a second naming convention and preserves existing `JEEEditorStyles` call sites.

## Public API

### `JEEAssetUtility`

```csharp
public static class JEEAssetUtility
{
    public static bool IsAsset(UnityEngine.Object target);
    public static bool IsValidAssetPath(string assetPath);
    public static bool TryOpenAsset(string assetPath);
    public static T LoadOrCreateAsset<T>(string assetPath) where T : ScriptableObject;
}
```

- `IsAsset` returns `false` for `null` and otherwise checks for a non-empty AssetDatabase path.
- `IsValidAssetPath` returns `false` for null, empty, or unresolved paths. Valid folders remain valid paths, matching current behavior.
- `TryOpenAsset` loads the object, opens it, and updates `Selection.activeObject` only when opening succeeds. It returns `false` without side effects when the path cannot be resolved or opened.
- `LoadOrCreateAsset<T>` returns an existing asset of `T`, or creates one without overwriting anything.
- Creation accepts only `.asset` paths under `Assets/`. Null, empty, package, absolute, or non-`.asset` paths throw `ArgumentException`.
- An existing asset of another type throws `InvalidOperationException` rather than attempting to overwrite it.
- Missing parent directories are created before the AssetDatabase asset is created.

### `JEEHierarchyUtility`

```csharp
public static class JEEHierarchyUtility
{
    public static int GetDepth(Transform transform);
    public static string GetPath(Transform transform);
}
```

- Both methods throw `ArgumentNullException` for null input.
- `GetDepth` counts parents, so a root Transform has depth zero.
- `GetPath` returns a slash-separated root-to-object path without a scene name.

Unity's native `Transform.IsChildOf` remains the supported ancestry API. No redundant ancestry wrapper is added.

### `JEEEditorStyles`

Move the existing class into `JustEnoughEditor.Utilities` unchanged. Its current public style properties and compound IMGUI helpers become the supported public IMGUI surface:

- header, section, card, toolbar, list, badge, label, button, status, and pill styles;
- `DrawHeader`, `BeginCard`, `EndCard`, `SearchFieldStyle`, `DrawStatusLabel`, `DrawEmptyState`, and `GetIcon`.

No generic drag-and-drop helper is added. The three existing drag-and-drop flows accept different inputs and perform different actions; a shared callback abstraction would be larger and less clear than those flows.

## Existing Call-site Migration

- `FavoriteAssetsWindow` uses `IsValidAssetPath`, `TryOpenAsset`, and `LoadOrCreateAsset<FavoriteAssetsData>`.
- `FavoriteFoldersWindow` uses `LoadOrCreateAsset<FavoriteFoldersData>`.
- `BulkRenameWindow` uses `IsAsset`.
- `HierarchyColorAndIconDrawer` uses `GetDepth`.
- `MissingScriptFinderWindow` uses `GetPath`.
- Remove only the private helpers made redundant by these replacements.

Favorite serialization, migration, list copying, colors, repaint behavior, and user interaction remain in their current owners.

## Documentation

Add a README section showing:

1. how to add `JustEnoughEditor.Utilities` to an Editor asmdef reference list;
2. one asset example;
3. one hierarchy example;
4. one IMGUI card example.

The API is Editor-only. Examples must live under an `Editor` folder or in an Editor-only asmdef.

The listed public members are a compatibility surface. They should not be renamed or removed without a deprecation path.

## Tests and Verification

Add `JustEnoughEditor.Utilities.Tests`, an Editor-only test assembly that references `JustEnoughEditor.Utilities`. This proves the API is consumable across an asmdef boundary rather than merely public inside its own assembly.

EditMode tests cover:

- null and valid asset/path classification;
- successful and unsuccessful asset opening without unintended selection changes;
- ScriptableObject load-or-create behavior, including existing assets, missing directories, invalid paths, and wrong existing types;
- root/child hierarchy depth and full paths;
- null hierarchy inputs;
- public IMGUI styles being reachable from the separate test assembly.

Tests create assets only under a unique temporary `Assets/` directory and delete that exact directory during cleanup.

Verification sequence:

1. add a reflection-based smoke test in the existing test assembly and confirm it fails because the public utility assembly is absent;
2. implement the minimum public assembly and utilities;
3. replace the smoke coverage with compile-time tests in `JustEnoughEditor.Utilities.Tests`, then run the focused utility tests on Unity 6000.3.7;
4. run the complete EditMode suite on Unity 6000.3.7;
5. copy the project to an isolated temporary directory and run the focused utility tests with Unity 6000.0.58, leaving the working project untouched;
6. inspect the scoped diff and final Git status. Do not commit or push unless explicitly requested.
