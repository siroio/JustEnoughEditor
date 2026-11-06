# Window Tab Groups Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Group native Unity editor tabs with normal heading tabs, retaining member identity, state, and native activation.

**Architecture:** A serialized heading EditorWindow owns group data. A coordinator manages transitions and lifecycle; a reflection adapter confines Unity-internal docking operations. Establish the hidden-host mechanism with a disposable integration experiment before production implementation.

**Tech Stack:** Unity 6000.3.7f1, C#, IMGUI heading controls, native EditorWindow/DockArea, Unity EditMode tests.

**Spec:** `docs/superpowers/specs/2026-11-06-window-tab-groups-design.md`

## Global Constraints

- Unity validation target: 6000.3.7f1. No runtime patch dependencies or replacement tab renderer.
- Preserve all existing worktree changes. No staging, commits, pushes, or worktrees.
- Never destroy/recreate members for collapse or bypass unsaved-change handling.
- No production feature claims until persistence, native activation, and recovery checks pass.
- Smart Brain writes remain deferred pending its project identifier; the earlier question remains unanswered.
- User's explicit request to proceed through implementation supersedes additional plan-approval gates. Execute inline, then obtain one independent review.

## Files and interfaces

- `Editor/WindowGroups/JEEDockAdapter.cs`: capability checks; native parents, panes, selection, parking ownership, and moves. All reflection lives here.
- `Editor/WindowGroups/JEEWindowGroup.cs`: serialized heading, group ID/name/color/member references, and IMGUI/context menu controls.
- `Editor/WindowGroups/JEEWindowGroups.cs`: `Create(IReadOnlyList<EditorWindow>, string, Color)`, `SetCollapsed(JEEWindowGroup, bool)`, `Ungroup(JEEWindowGroup)`, `RemoveMember(JEEWindowGroup, EditorWindow)`, reconciliation, recovery, native activation.
- `Editor/WindowGroups/JEEWindowGroupSetup.cs`: create/add selection UI and menu items, using the focused dock captured before opening setup.
- `Editor/WindowGroups/JustEnoughEditor.WindowGroups.asmdef`: Editor-only, existing Utilities reference.
- `Tests/Editor/WindowGroups/`: independent test assembly, disposable windows, model/integration tests. Temporary feasibility scripts stay in the task's ignored scratch project until the mechanism is proven.
- README: creation, management, recovery, version limitations.

## Review Focus

- Duplicate Inspector instances must remain distinct through collapse and reload (Task 2).
- Native activation of a hidden member must expose that exact instance (Tasks 1 and 2).
- Heading destruction during layout teardown must not resurrect closed windows (Task 2).
- Partial moves or missing reflection members must leave visible recoverable windows (Task 2).
- A native drag between groups/docks must not silently steal membership or lose tab order (Tasks 2 and 3).

### Task 1: Prove hidden ownership and activation

**Files:** ignored `.superpowers/sdd/2026-11-06-window-tab-groups/` experiment project, logs, and ledger.
**Interfaces:** produces the evidence-backed native ownership strategy used by `JEEDockAdapter`.

- [x] Create a disposable editor project/test harness with the exact installed editor. Identify the connected project before any editor mutation.
- [x] Characterize the failure: removing a member from its dock clears its parent and breaks native `ShowTab` activation.
- [x] Exercise a parking DockArea owned by the visible dock, keeping members in its pane list. Observe `HostView.actualViewChanged` to restore a member on native activation. Verify serialization, host initialization, and cleanup on disposable instances.
- [x] Save and reopen a test layout and restart the disposable editor; verify distinct member state and native activation. If this mechanism is invalid, investigate alternatives within the approved normal-heading/reflection boundary before proceeding.

### Task 2: Implement group lifecycle and adapter

**Files:** adapter, coordinator, serialized heading model, assembly definitions and tests.
**Interfaces:** produces the group operations above; the heading UI consumes them in Task 3.

- [x] Write behavioral tests for create/collapse/expand, two same-type instances with different state, native activation, remove/ungroup, and failed cross-dock transitions. Observe the initial unsupported/missing-feature failure.
- [x] Implement capability detection, ordered membership, valid parking ownership, and selection changes using the strategy proved in Task 1.
- [x] Implement heading close, last-member dissolution, domain-reload repair, layout teardown, missing-member recovery, group movement, and normal member detach/reorder.
- [x] Run the focused EditMode integration suite. Expected: identity, state, pane order, native activation, teardown, and recovery assertions pass without new console errors.

### Task 3: Add normal-tab UI and documentation

**Files:** heading management view, setup UI/menu items, README.
**Interfaces:** consumes Task 2 operations; no reflection in UI files.

- [x] Implement group setup from captured focused dock, explicit member selection, name/color, and validation. Keep mutation out of cancelled setup.
- [x] Add heading icon/name/count/state, member list, exact-instance activation, expand/collapse, add/remove, rename/color, Ungroup, and Restore All Grouped Tabs.
- [x] Exercise creation, management, drag/reorder, multiple groups, duplicate titles, and recovery with representative native/JEE windows. Expected: ordinary Unity tab rendering and documented controls match the approved design.
- [x] Document controls and measured support limits; do not promise untested Unity versions.

### Task 4: Integration, regression, and review

**Files:** test reports and task ledger; only scoped fixes if discovered.

- [x] Verify exact-version compile and relevant existing EditMode tests with current user changes preserved. Report unrelated baseline failures separately.
- [x] Verify real Scene, Game, Inspector, Console, Project, IMGUI, and UI Toolkit windows; Play Mode, maximize/restore, reload, collapsed layout reload, actual restart, and unsaved test-window teardown.
- [x] Request one fresh review of the new files and scoped README changes. Resolve material findings and rerun affected tests.
- [x] Verify absolute-path file contents, scoped diff, asset metadata, final status, and report measured results and remaining limits.

## Completed verification — 2026-11-06

All checks below used the installed Unity **6000.3.7f1**. Disposable live-editor checks ran in the ignored scratch project, not the unrelated project exposed by the connected MCP endpoints. Evidence lives under `.superpowers/sdd/2026-11-06-window-tab-groups/`.

| Check | Measured result | Evidence |
| --- | --- | --- |
| Compile and complete repository EditMode suite | **93/93 passed**, including 36 Window Groups cases and 57 existing cases; no failed or skipped tests | `tests-verified.xml` |
| Group behavior and failure recovery | Distinct instances, ordering, exact native activation, close/ungroup, whole-dock teardown, UI Toolkit, representative native/JEE windows, unsupported-hook recovery, and third-party insertion failure rollback passed | `WindowGroupTests.cs`, `tests-verified.xml` |
| Unsaved member handling | Dirty windows remain in the native container hierarchy and unsaved-window list; moving a collapsed group updates the source and destination lists | `CollapsedGroupMoveTransfersUnsavedClosePromptToDestination`, `tests-verified.xml` |
| Native UI | Actual menu/setup creation, collapse/expand and mouse drag to another floating dock retained all nine members; the drag/render interaction produced no new errors | `preview-drag-safe.log`, computer-use observations |
| Floating detach and maximize/restore | Heading and UI Toolkit member restored with group state intact; all visual lifecycle assertions passed, exit 0 | `visual-drag-safe.log` |
| Script reload and Play Mode | Domain reload, entering Play Mode, and returning to Edit Mode retained the collapsed group and distinct states; exit 0 | `lifecycle-verified.log` |
| Separate-process persistence | Collapsed layout restored nine windows; two Inspectors retained distinct asset targets and locks, Project retained its search, custom windows retained different values, and every member's native Focus exposed the correct instance; save and restore exited 0 | `detailed-save-final.log`, `detailed-restore-complete.log` |
| Independent review | One fresh reviewer found partial-capability recovery and failed native insertion rollback defects; both were fixed and covered by regression tests | `review-red-verified.xml`, `tests-verified.xml` |
| Scope audit | Absolute source reads, non-empty added-code diff, complete asset metadata and `git diff --check` verified; existing unrelated changes preserved; no staging or commits | Final worktree audit |

Implementation details established by live verification:

- Collapsed members use an offscreen child DockArea with a selected idle pane. Unity retains native ownership and serializes the hierarchy; hidden member activation expands the group.
- Native drag callbacks must not activate or move real members reentrantly. A temporary placeholder keeps the source dock alive, with reconciliation deferred until insertion completes.
- Native unmaximize restores serialized copies. The coordinator reconnects the surviving original using Unity's insertion order and group ID.
- Reparenting the hidden DockArea must refresh native unsaved-window bookkeeping in both containers.

Verification boundaries: no other Unity version is claimed tested. Tests inspect Unity's unsaved-window bookkeeping; they do not claim an automated click-through of the save/discard/cancel dialog. Internal docking API compatibility remains version-sensitive. The implementation uses the existing uncommitted `JustEnoughEditor.Utilities` assembly in this checkout.

Harness corrections were kept out of production: startup UI initialization was deferred before setting Project search; search state is read through its property; ordinary layout loading replaced the probe's `NoMainWindowSupport` flag, which excludes floating windows from subsequent saves ([Unity reference source](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.3/Editor/Mono/GUI/WindowLayout.cs)). A batch-mode native main-window assertion passed when rerun in a normal editor process. A scratch shutdown-marker file race occurred after successful UI checks and is not a product interaction error.

Smart Brain storage remains deferred because its project identifier is still unspecified. No additional approval is needed for the completed implementation.

## Follow-up: native tab context menu — 2026-11-06

The user requested creation and addition from right-clicking ordinary tabs such as Scene and Build Profiles. Added `JEEWindowGroupTabMenu` through HostView's optional `populateDefaultMenuItems` event, confined to the reflection adapter. The clicked instance and dock are captured independently of focus. New Group opens the existing setup with that tab selected; Add to Group lists only same-dock headings, retaining duplicate-name identity and the chosen group's collapsed state. Member tabs also offer Manage Group and Remove from Group. Deferred actions revalidate ownership, so moving a tab after opening its menu cannot add it to an unrelated dock.

- `context-menu-red.xml`: all five new behavioral checks failed because the native menus lacked the feature.
- `context-menu-targeted.xml`: all five passed after implementation and waiting for the actual deferred editor callback in the tests.
- `context-menu-verified.xml`: **98/98 passed** (41 Window Groups and 57 existing checks), no failures/skips, Unity 6000.3.7f1.
- Live UI: Scene's ordinary tab context menu displayed Window Groups alongside native Close Tab/Add Tab/Overlays items. Full menu callbacks, setup creation, addition, removal and stale-target behavior were verified by the native-menu integration tests. The computer-use helper dismissed floating-window menus on subsequent input, so a complete mouse-driven create/add sequence is not claimed.
- README and design updated; metadata and final scoped audit passed. Existing ShaderGraphSettings changes observed at the start of this follow-up were preserved. No staging or commits.

## Follow-up: click heading to toggle — 2026-11-06

The user requested direct collapse/expand by clicking the heading tab. The heading now observes mouse down/move/up on its native dock IMGUI container, without consuming events or replacing Unity's drawing. A completed left click schedules the existing collapse/expand operation, including on an already-selected heading. Native drag, right-click, programmatic focus, content clicks, and releases outside the heading do not toggle. Callbacks detach on disable and reattach when the heading moves to another dock.

Live verification exposed native scrolling to the last reinserted member on expansion. The click action now asks Unity's existing deferred tab-visibility mechanism to keep the heading visible; native scroll buttons are excluded from hit testing. [Unity DockArea reference](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.3/Editor/Mono/GUI/DockArea.cs) was checked against the installed editor behavior.

- `heading-click-red.xml`: the original click test failed before implementation; focus/drag exclusions already passed.
- `heading-click-overflow-red.xml`: reproduced the heading disappearing after expansion. Its scroller case initially failed at fixture setup; `heading-click-scroller-red.xml` corrected the overflowing layout and reproduced the actual unwanted toggle.
- `heading-click-overflow-fixed.xml`: all seven native-input checks passed, including small pointer movement, release outside, relocation, overflow, and scroller exclusion.
- `heading-click-final.xml`: **105/105 passed** (48 Window Groups and 57 existing checks), no failures/skips, Unity 6000.3.7f1.
- Live UI with matching production source hashes: a restored nine-member group expanded and collapsed from two successive clicks on the same already-selected heading. The expanded heading stayed visible and all nine member instance IDs remained unchanged. `heading-click-preview-final.log` contained no compile errors, exceptions or assertions. The disposable editor was closed afterward.
- README and design reflect the new interaction. Existing unrelated changes were retained; no staging or commits. Smart Brain writes remain deferred because the project identifier is unspecified.

## Current interaction: compact creation/addition and explicit toggle — 2026-11-06

This section supersedes the previous click-to-toggle follow-up. The user prioritized faster creation/addition, then explicitly rejected additional windows/features and reported accidental single-click toggles. Final scope is limited to native menus: `Window Groups > New Group` immediately groups the clicked tab, and the heading's `Add Tab` submenu adds one eligible same-dock window. A heading click only selects. Collapse/Expand remains explicit in the existing context menu and management buttons. The selected heading stays visible after expansion.

References reviewed: [Chrome tab groups](https://support.google.com/chrome/answer/2391819), [Vivaldi Tab Stacks](https://help.vivaldi.com/desktop/tabs/tab-stacks/), [Vivaldi Windows Panel](https://help.vivaldi.com/desktop/tabs/window-panel/), [Brave tab management](https://brave.com/blog/vertical-tabs/), [VS Code editor groups and Quick Open](https://code.visualstudio.com/docs/editing/getting-started/userinterface), and [Rider editor tabs](https://www.jetbrains.com/help/rider/Managing_Editor_Tabs.html). The adopted idea is operating directly from existing tabs. Search, batch-selection enhancements, continuous-add UI, color palettes, automatic distinct defaults, and new shortcuts were implemented provisionally and then removed per the user's correction. No new product window, setting or source file remains. Native tab mouse callbacks/hit testing were also removed; final production diff against the start of this follow-up is 25 insertions and 96 deletions in five existing files.

Validation: `compact-interaction-red.xml` reproduced both accidental heading collapse and hidden headings after explicit expansion. `compact-final.xml` passes **106/106 tests** (49 Window Groups + 57 existing), no skipped cases, Unity 6000.3.7f1. Coverage includes native menu callbacks, single-click selection without toggling, explicit overflow-safe expansion, exact/stale addition targets, and an existing add-dialog bug where a destroyed target could otherwise create an unintended new group. Metadata and `git diff --check` passed. No staging or commits.

Final live UI validation was stopped by the user's physical Escape key before interaction checks. No further computer-use actions were issued. The disposable preview editor was left open; `compact-preview.log` reached readiness without compile errors. Earlier UI observations of the withdrawn search/palette interface are not evidence for the final design. Smart Brain writes remain deferred because its project identifier is unspecified.

## Project Settings and membership visibility follow-up — 2026-11-06

The user explicitly removed the need for a tab management window, requested Project Settings instead, automatic colors, and clearer membership on the tab strip. Deleted `JEEWindowGroupSetup` and the heading's management controls. A standard provider at `Project/Just Enough Editor/Window Groups` edits current layout groups. Native menus still create/add immediately and link to Settings. Colors are assigned once from unused colors in the dock and remain serialized with the heading; there is no color picker or additional settings store. The unused Utilities assembly reference was removed.

A display-only IMGUI layer draws a thin line and end caps from the heading through consecutive members. It keeps original native titles and icons, clips at tab-strip/scroller boundaries, follows dock moves, and is removed on heading disable/destruction. No mouse handlers, shortcuts or replacement tab renderer were added. Native single-click selection remains unchanged.

Validation on Unity 6000.3.7f1: `project-settings-red.xml` 0/3 reproduced missing provider/menu and identical fixed colors; `project-settings-green.xml` 3/3. `group-indicator-red.xml` 0/3 reproduced missing membership indication. `settings-indicator-feature.xml` 54/54. Final `settings-indicator-final.xml` **111/111 passed** (54 Window Groups + 57 existing), zero failures/skips. Tests open and render the standard settings page, verify stable serialized colors, exercise native panel dispatch of the indicator's repaint callback, check clipped spans, collapsed width, move/cleanup and preserved tab input. Metadata and whitespace audits passed. Production delta against `settings-before`: 194 insertions / 169 deletions including metadata, with the setup window removed and one static provider added. README and the current design describe the resulting interface.

No visible preview or computer-use session was resumed after the prior Escape. Manual visual acceptance of the final line styling is not claimed; native rendering dispatch and behavior were exercised by EditMode tests. Prior unrelated worktree changes remain. No staging/commit/push; Smart Brain writes remain deferred pending a project identifier.

## Follow-up: live visual and interaction verification — 2026-11-06

The user explicitly requested live Unity checks, authorizing a new computer-use session. Verified the current feature in the disposable Unity 6000.3.7f1 editor, with identical production/preview source hashes. The unrelated open Unity project was not modified.

Two issues found through the live UI were corrected:

- Selecting a heading drew an additional color stripe across its entire content area. Removed that stripe so only the actual tab membership span carries the group color.
- Native menus interpreted trailing `#instanceId` labels as keyboard shortcuts and displayed `Shift+number`. Parenthesized identifiers now render as ordinary labels. Menu integration assertions were updated, and the corrected native submenu was inspected live.

Observed with actual mouse input: adjacent blue Play and yellow-green Tools spans distinguish Scene/Game from Inspector/Console; ungrouped tabs have no group line. Single clicks select without toggling. Explicit context-menu collapse/expand updates both visible members and the line extent. Heading Add Tab added the existing Project instance and updated the count/span immediately. An ordinary tab's New Group created a third, automatically purple group without a setup window. Dragging Play out retained its three members and color indicator in the floating dock. Native maximize/restore retained the groups. The standard Project Settings page opened at Window Groups, displayed compact membership lists without a color picker, and its Collapse button updated the tab strip.

Verification boundaries: owned-window keyboard focus prevented completing a physical rename in Project Settings; its nested Add Tab click and attempted redocking/narrowing were not completed by the input driver. These are not claimed as live passes. Native-menu callbacks, settings rendering, clipped indicator bounds, and relocation remain covered by the integration suite. The scratch request-file helper had one sharing-violation exception; no product compile errors, exceptions or assertions were observed during the verified interactions.

Final `visual-ui-verified.xml`: **111/111 passed**, zero failures/skips (54 Window Groups + 57 existing). Source hashes matched, whitespace audit passed, and the disposable Unity editor was closed after verification. Existing worktree changes were preserved; no staging, commits or pushes. Smart Brain storage remains deferred because its project identifier is unspecified.
