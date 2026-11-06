# Window Tab Groups Design

Date: 2026-11-06
Status: Approved by the user on 2026-11-06; implementation and verification authorized.

## Intent and agreed approach

Add browser-like grouping to Unity's existing editor tab bars. The user confirmed that the target includes Scene, Game, Inspector, Console, and third-party editor windows, and selected a normal Unity tab as each group's heading.

The goal is to reduce tab clutter without recreating windows or losing their editing state. This is a new Editor-only feature of Just Enough Editor. Grouping GameObjects, grouping OS applications, and replacing the entire Unity layout are outside this feature.

## Approaches

1. **Normal heading tab, selected by the user.** An ordinary EditorWindow supplies the group name, color icon, count, and serialized group data. Members remain real native editor windows. Unity continues drawing its tab bars. A non-interactive color line marks each group's span. Dock manipulation still requires internal APIs, so this choice does not imply public-API-only compatibility.
2. **Replace or intercept native tab-bar drawing, not selected.** This could reproduce browser color bands more closely, but expands the version-sensitive integration substantially. Native tab input and drawing remain unchanged.
3. **Separate group switcher window, ruled out by the requested interaction.** It would not group tabs directly in their existing tab bars.

## Proposed user experience

The latest user corrections on 2026-11-06 supersede the original setup/management window and single-click toggle proposals.

- An ordinary tab's `Window Groups > New Group` immediately creates a heading named after that tab. The color is assigned automatically, avoiding existing group colors in that dock, and stored with the layout. There is no setup window or color picker.
- A group has a persistent ID, a name, a color, an ordered set of member instances, and an expanded/collapsed state. One window instance belongs to at most one group. Multiple instances of the same window type remain distinct.
- Expanded order is: heading tab, that group's member tabs in their saved order, then the next group or ungrouped tabs. Ungrouped tabs retain their relative order.
- The heading uses a generated color icon plus its name and member count. Expanded/collapsed state is also conveyed by text or an icon; color is not the only cue.
- Selecting the heading only shows its name, count, state and usage hint. Single-click toggling caused accidental operations and is removed. Expand/Collapse stays explicit in the heading context menu and Project Settings. Native tab input remains unchanged.
- `Project Settings > Just Enough Editor > Window Groups` provides rename, membership, Expand/Collapse and Ungroup. The heading and member context menus open this standard page through `Settings…`. No dedicated management window remains. Ordinary tab context menus provide creation, same-dock addition and removal through Unity's existing menu population event.
- A thin color line with end caps spans the heading and its consecutive member tabs, making membership visible without changing original titles or icons. It shrinks to the heading when collapsed and clips to the visible tab strip outside scroll buttons. The overlay ignores input and does not patch native drawing or mouse handlers; it reattaches on dock moves and is removed on group destruction/reload.
- Collapsing leaves the heading visible and hides the group's member tabs. If a member was selected, the heading becomes selected. The existing member instances and their state must survive. Expanding restores their tab order; it does not construct replacement windows.
- Explicitly expanding a selected heading keeps it visible even when member tabs overflow the dock, using Unity's existing deferred visibility mechanism.
- Project Settings lists members even when collapsed. Opening an existing member through Unity's normal menu expands its group and focuses that instance.
- A heading's `Add Tab` submenu and Project Settings' `Add Tab…` menu list eligible ungrouped windows in the same dock. Duplicate types/titles are distinguished by a session-local instance identifier. The model never uses title or type as the primary identity.
- Search, palettes, continuous-add UI, bulk setup dialogs and new shortcuts are excluded. The only settings entry point is Unity's existing Project Settings; it edits the serialized live heading data without another settings store.
- Removing a member makes it a normal visible tab adjacent to the group. Removing the final member dissolves the group. Ungroup restores every surviving member as a normal tab, in order, and removes only the heading.
- Closing a heading through its standard close action has the same non-destructive result as Ungroup. This release does not offer a command to close every member.
- Expanded members can be dragged out using Unity's normal tab dragging; leaving the group's dock removes that instance from the group. Reordering members within the dock updates their stored order. Membership is explicit: dragging an ungrouped tab near a heading does not silently add it.
- Moving a heading to another dock moves its group members with it, preserving order and collapse state. If the destination or transition is unsupported, restore the original valid arrangement and show a concise explanation instead of leaving hidden members behind.
- Groups are local to a single dock at a time; nested groups and groups spanning multiple docks are excluded. Main-window docks and ordinary floating docks are in scope. Popup, dropdown, modal, utility-only, and internal host windows are excluded.

## Architecture and boundaries

Keep the feature in a new Editor-only `WindowGroups` folder and assembly, with its own tests. The group heading owns the minimal serialized model; a small coordinator reconciles live dock changes; one adapter owns every reflection call into Unity's docking implementation. Follow the existing `JustEnoughEditor` namespace. Use the existing editor styles where appropriate, without modifying their public API.

The adapter checks the actual required members and signatures before enabling grouping. It handles tab insertion/removal, selection, group movement, and restoration in one place. Do not spread raw reflection through UI code. Do not introduce Harmony, method detours, a replacement renderer, or other runtime patch dependencies under this design.

Update operations validate their full inputs before changing a dock. Preserve the previous valid order and selection so a partially failed mutation can restore a usable visible arrangement. Do not close or destroy member windows to implement collapse. Do not render another EditorWindow by invoking its OnGUI method or moving its visual tree into a wrapper; Scene, Game, IMGUI, and UI Toolkit windows retain their native hosts and lifecycle.

Avoid continuous full-editor repainting. Lifecycle events and a bounded reconciliation pass may discover moves and closes, but redraw only changed headings and docks. Exact polling cadence and helper-class boundaries are left to the implementer and should follow actual editor measurements.

## State, persistence, and recovery

Use Unity-serialized object references to identify live members and an ordinary serializable string ID for each group. Store the heading's name, color, order, and intended collapse state with its layout data. Instance IDs are useful for live diagnostics and disambiguating labels, but are not persistent identities. Do not substitute window types, names, JSON reconstruction, or freshly created instances for lost references.

Data is personal to the editor layout/project, not a shared asset under Assets. Any auxiliary recovery metadata belongs under UserSettings and must not contain fabricated substitutes for window state. Prefer Unity's own layout serialization over a parallel serialization format for member contents.

Collapsing is the main technical risk. Unity's public API does not expose tab visibility. `DockArea.RemoveTab` clears `m_Parent`, and `WindowLayout.SaveWindowLayout` explicitly skips windows without a parent. Simply removing windows from the visible pane list is not a completed persistence design. `EditorWindow.ShowTab` also resolves the window by index in its parent's pane list, so hidden windows must not break normal menu/shortcut activation.

The implementer must first establish a valid ownership/serialization strategy for hidden instances on the actual target editor. The intended contract is:

- A collapsed member retains a valid owner and remains available for serialization and normal activation.
- Assembly reload, entering/exiting Play Mode, and layout save/load preserve group membership and each member's Unity-serialized state.
- Restarting Unity restores the groups and intended collapse state. Any non-serialized transient state has the same guarantees as an ordinary Unity editor window, not stronger guarantees invented by JEE.
- Opening a collapsed member using Unity's standard menu or shortcut exposes the existing instance and expands its group as necessary; it must not create a duplicate or focus an invisible view.
- Maximizing/restoring a member does not orphan its group or overwrite its original dock membership.
- Closing an entire dock, quitting, or loading another layout must follow Unity's normal unsaved-change handling. A heading's individual Ungroup/Close behavior must not resurrect windows during whole-layout teardown.
- Missing member references are removed with a visible explanation; a live member must never be silently replaced or destroyed. If reflection compatibility is lost, restore visible native tabs wherever recovery is possible and disable grouping operations.
- A recovery command, `JEE > Window Groups > Restore All Grouped Tabs`, restores surviving live members and removes headings. It does not reset the user's overall layout or close unrelated windows.

## First implementation checkpoint

After design approval, implementation begins with a bounded compatibility experiment on disposable test windows and a separate temporary layout. Preserve the user's current layout and unsaved work. Test membership identity, collapse/expand, layout serialization, and native activation before building the complete UI.

The exact valid hidden-window ownership mechanism is deliberately left to that experiment; it is not yet proven by the source review. If it cannot meet the persistence and activation contracts without runtime patching or recreating windows, report the evidence and revise the design. Do not silently ship a manager-only UI, remove persistence, or add a tab-bar patch under the selected approach.

## Validation and completion criteria

Initial validation target is this project's Unity **6000.3.7f1**. Other Unity versions are not claimed supported solely because reflection members can be found. Record the actual editor used for every integration check.

1. Automated model tests cover unique membership by instance, duplicate window types, stable order, removal, group movement, and dissolution of the final member. Test behavior rather than private helper shapes.
2. Editor integration checks create disposable IMGUI and UI Toolkit windows with distinct serialized state. Collapse, expand, reload, and restore must preserve both object identity within a session and serialized state across reloads.
3. Representative real windows include Scene, Game, two distinct Inspector instances, Console, Project, and an existing JEE window. Test selection, menus/shortcuts, reorder, member detach, heading movement, maximize/restore, and group dissolution.
4. Verify collapsed layout save/load and an actual editor restart. Inspectors retain their individual lock/target state, and Project windows retain their individual browsing state to the extent native Unity restores it. A simulated serialization test alone is not a restart test.
5. Exercise unsaved-change handling and whole-dock/layout shutdown using a disposable test editor window. Do not discard the user's actual unsaved work for validation.
6. Verify capability failure/recovery without losing member windows, repeated collapse/expand without duplication, and no new console errors. Verify normal editor use with grouping disabled or no groups present.
7. Run the repository's relevant existing EditMode checks and review the final scoped diff. Preserve all pre-existing worktree changes; do not stage, commit, push, or create a worktree as part of this feature unless separately requested.
8. Document the actual creation, collapse, expansion, removal, and recovery operations in README. Clearly separate automated results, live-editor results, restart results, and anything still unverified.

The feature is complete only when these contracts hold on the target editor. A design document, a successful compile, or a mock tab bar is not feature completion.

## Scope and open implementation decisions

This is a substantial editor integration, not a small styling change. Native tab drawing, standard per-window editing, and unrelated JEE features remain under their existing implementations.

The implementer may choose concrete class names, small helper boundaries, test infrastructure, icons, color palette, and event/reconciliation strategy. The hidden-window ownership experiment must settle its mechanism with evidence. User-facing changes to the selected approach or removal of any persistence/activation contract require a revised proposal.

Smart Brain storage is currently deferred because its project identifier has not been supplied or found in repository configuration. Local design work can continue; no project ID is inferred from the folder name.

## Evidence

- Local `ProjectSettings/ProjectVersion.txt`: Unity 6000.3.7f1.
- Existing JEE tools derive from EditorWindow; there is no existing group controller to extend.
- Unity 6000.3 reference source: [DockArea](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.3/Editor/Mono/GUI/DockArea.cs), [EditorWindow](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.3/Editor/Mono/EditorWindow.cs), [WindowLayout](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.3/Editor/Mono/GUI/WindowLayout.cs).
- The branch source supports the API/lifecycle analysis; it does not replace live verification against the exact installed patch version.
