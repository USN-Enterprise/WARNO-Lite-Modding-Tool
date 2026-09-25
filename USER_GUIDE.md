# WARNO Lite Modding Tool User Guide

For version **1.9.15**, Windows x64. Available fields and features depend on the selected Mod's data and structure.

[Home](README.md#english) · [中文教程](使用教程.md) · [Release notes](RELEASE_NOTES.md#english)

## Contents

1. [Preparation and basic concepts](#prepare)
2. [Install, open or create a Mod](#open-mod)
3. [Workspace, search and editing modes](#workspace)
4. [Your first edit: change a unit's price](#first-edit)
5. [Drafts, applying changes and restoring backups](#drafts)
6. [Units, batch editing and creation](#units)
7. [Weapons and ammunition](#weapons-ammo)
8. [Divisions, emblems and descriptive text](#divisions)
9. [Army General and Strategic Packs](#strategic)
10. [Game rules, experience and terrain](#rules)
11. [Professional tools and reference inspection](#professional)
12. [Creation, generation, development launch and upload](#mod-tools)
13. [Settings, troubleshooting and feedback](#help)
14. [Current limits](#limits)

<a id="prepare"></a>

## 1. Preparation and basic concepts

You need the fully extracted editor release and an editable WARNO Mod source folder. Original names, official images, original division text and official creation/generation workflows also require a local WARNO installation. The editor itself does not require Python, the .NET SDK or unpacking software, and can run outside the game directory.

Distinguish these three folders:

| Folder | How to identify it | When to select it |
| --- | --- | --- |
| Editor folder | Contains `WarnoLiteModdingTool.exe` and its companion files | Starting the editor |
| WARNO game folder | The game installation, containing `Data/PC` | Setting the source for original names, images and text |
| Target Mod root | Contains that Mod's `GameData`; a complete official project also has generation scripts and other files | Opening and editing the Mod |

For a path such as `MyMod/GameData/...`, open `MyMod`, not a folder inside `GameData`. Compiled output without editable source files cannot replace the Mod source directory.

Editing has three separate stages:

| Stage | What it does | Does the game already use the change? |
| --- | --- | --- |
| Save drafts | Records pending edits without changing formal source files | No |
| Preview and apply | Validates, backs up and writes the current Mod's source files | Generation is still needed |
| Generate / compile | Runs the official WARNO workflow on the source files | After success, enable the Mod in the game and check it |

For a first exercise, consider a dedicated test Mod. Transaction backups preserve the affected files before application; they do not replace long-term backups of your whole project.

<a id="open-mod"></a>

## 2. Install, open or create a Mod

### 2.1 Download and launch

1. Visit the [project releases page](https://github.com/USN-Enterprise/WARNO-Lite-Modding-Tool/releases/latest) and choose the Windows x64 portable ZIP. End users do not need the Source code download.
2. Extract the entire ZIP to a local folder. Do not run inside the archive or copy only the EXE.
3. Run `WarnoLiteModdingTool.exe`. Only one instance is allowed in the same Windows login session. If it reports that it is already running, return to the existing window.
4. Before upgrading, close the old version normally. Extract the new version into a fresh directory. Drafts and transaction backups are stored inside the target Mod, not the editor installation.

This guide describes 1.9.15. The download page may offer a later release; consult its release notes as well.

### 2.2 Open an existing Mod

1. Use automatic Mod discovery and select the desired project. The same action is available in Mod Tools.
2. If discovery finds nothing, use the action to open a Mod folder and select the root containing `GameData`.
3. Wait for loading, then check the current project path and object list. Make sure you have not opened a different copy with the same name.
4. If names appear as internal identifiers, configure the game folder under [original game names](#help). Available ordinary parameters can generally still be edited.

Features are enabled according to the target Mod's files. A project containing only ammunition or rule files can use those features without unrelated modules.

### 2.3 Create a Mod through the editor

1. Choose **Create New Mod** from the welcome page or Mod Tools.
2. Select WARNO's **parent `Mods` directory**. It must contain the official creation script and `ModData/base.zip`. This is a different folder choice from opening an existing Mod.
3. Enter a name of 1–32 ASCII letters or digits, such as `MyFirstMod`. It must not duplicate an existing Mod name.
4. Check the destination, confirm creation and wait for the official process to finish. The editor then attempts to open the new project.
5. If creation succeeds but opening fails, keep the output and retry by opening the Mod folder manually. For missing-script or missing-base-package errors, check the selected directory first.

<a id="workspace"></a>

## 3. Workspace, search and editing modes

Select a module on the left, select an object in its list, then edit its fields in the inspector. The top bar provides **Draft overview**, **Generate / compile Mod** and Problems. Settings and the professional-mode switch are at the lower left.

- **Search:** Start with a display name; part of an internal identifier can also work. Candidates come from the current Mod.
- **Filters:** Narrow the list by faction, country, category, production tab, role or draft status where available. Unit filters can match all or any conditions.
- **Browsing versus checking:** Clicking a row browses the object. A checkbox adds it to a batch selection; these are separate actions.
- **Select all filtered:** Includes the entire filtered result, not just the visible rows. Changing filters does not clear previous checkboxes. Check the selected count and hidden selections before batching.
- **Layout:** Drag dividers to resize panels; lists and the module sidebar can scroll. Detachable panels are covered under [Settings](#help).

This guide calls the everyday editing view **basic mode**. The current Settings option may read **Standard mode** (`普通模式` in Chinese); these refer to the same mode. **Professional mode** adds complex formulas, indices, source details and some specialist fields. Switching modes does not discard saved drafts.

Original parameter names beside fields help identify the underlying data. Read the reason for a disabled or read-only field; professional mode does not bypass missing data or structural conflicts.

<a id="first-edit"></a>

## 4. Your first edit: change a unit's price

This exercise changes one existing unit's deployment price. Use the value shown in your project; no particular country or unit model is required.

1. Open the target Mod and select **Units**. Use **Clear selection** to make sure you are not editing a checked batch.
2. Search for a unit you intend to test, click its name and verify the object in the inspector.
3. Expand the cost/deployment section and locate the command-points field, identified by `Resource_CommandPoints`. This is deployment price; the nearby Army General price is a different field.
4. Note the original value, then enter a different valid integer. For example, if the original value is `100`, try `90`. These are example values, not a claim about any vanilla unit.
5. Wait for the field's draft to save and check its status. Resolve input errors or save failures first; seeing a new number in the input alone does not establish a successful save.
6. Open **Draft overview**, find the price change and verify its object and before/after values. If other drafts exist, **Apply Selected** can limit this application. **Preview and apply all** includes all project drafts.
7. Wait for the preview, inspect the changes and related effects, then confirm. Check the completion result and backup information.
8. Use **Generate / compile Mod**, read the confirmation and run it. Wait for completion and check the output for success. For failures, see [troubleshooting](#help).
9. Enable the corresponding Mod in the game, select a division or deck that actually contains this unit, and check the displayed price. If the unit is missing, first verify the Mod and division/deck being used.

Before application, use the field's undo action or delete the draft. After application, either edit the price back and apply again, or use [backup restoration](#drafts). Regenerate after changing formal files to check the result in the game.

<a id="drafts"></a>

## 5. Drafts, applying changes and restoring backups

### 5.1 Which edits need a save button?

Ordinary single-field edits generally save drafts automatically. Unit/ammunition batches, creation wizards, division names and emblems, division text and Strategic Packs have an explicit **Add to drafts** action. The unit picture and Traits and abilities windows use **Save draft**. Complete the action in its window, then confirm the result in Draft overview.

Drafts are stored in `<Mod>/.warno-editor/draft-v1.json`. Before switching projects or closing normally, the editor waits for edits participating in its automatic-save workflow. Save failures retain the project and input. For pages with manual save buttons, save explicitly; closing the window is not equivalent to adding a draft.

### 5.2 Inspect and apply

1. Open **Draft overview** and find changes by summary, module, status or source file.
2. Select an entry to inspect its original/target values, object and scope. Expand batch groups to inspect their children.
3. Choose **Apply Selected** or **Preview and apply all**. Partial application still has to satisfy dependencies; object creation and required references cannot be separated arbitrarily.
4. Wait for candidate construction and validation, then read the final preview. Professional mode also offers before/after source text organized by file.
5. Confirm to write formal files. External file changes, changed references or failed candidate checks block the affected submission. Follow the diagnostic and preview again after resolving it.

**Delete Selected** and clearing all drafts cancel unapplied changes; they do not undo already written files. If submission succeeds but the subsequent refresh fails, reopen the same Mod and inspect the result rather than applying the change again immediately.

### 5.3 Restore applied changes

Each application saves relevant originals and a transaction manifest under `<Mod>/.warno-editor/backups/<backup-id>/`, and writes a record under that Mod's `logs/` folder.

1. In **Draft overview**, expand **Transaction backups and restore** and locate the relevant application.
2. Choose **Restore** and read the list of affected files.
3. **Restoration works at file level**, returning files to their state before that application. Later changes in the same files may also be overwritten. It is not a one-field undo action.
4. After confirmation, the editor first backs up the current state, then restores. Check the completion or failure message.
5. Inspect the reloaded data and remaining drafts. Regenerate if the restored data should take effect in the game.

Creation backups are also used to verify the origin of newly created units. Keep them while you still need operations such as deleting those units. These backups restore relevant files; they do not promise recovery of the whole game installation or every external manual edit.

<a id="units"></a>

## 6. Units, batch editing and creation

### 6.1 Stats, names and experience types

Select a unit and edit supported fields in the relevant groups: costs, deployment, survivability, vision, movement and other recognized parameters. Text inputs, dropdowns and tag selectors have different purposes. Unknown existing values are preserved.

- A display name is different from an internal variable name. Units sharing a name token can be renamed independently. Creating or renaming requires the local name dictionaries to be loaded for collision checks.
- ECM is displayed as a positive percentage; do not copy the sign from raw NDF values. Basic vision inputs scale related vision values using their original ratios. Check the combined preview.
- Forward deployment accepts a nonnegative distance or an existing preset from the current Mod. A reference preset does not automatically add every related ability to a unit.
- The experience-type selector chooses an available route from this Mod. Expand the level-effect details to inspect it. Changing a unit's route is different from editing a shared route's numbers; the latter is under [Game rules](#rules).

### 6.2 Traits and abilities

1. Open **Traits and abilities**.
2. In basic mode, choose among the 15 supported groups of related traits and abilities. Read the current Mod's descriptions and role distinctions; the same icon may have different ability variants.
3. Professional mode lets you search parseable existing abilities in the current Mod and add, replace, remove or clear them. Display tags can be edited separately.
4. Save the draft in the window, inspect the preview and apply.

A display tag alone does not grant the underlying ability. This feature primarily manages unit ability references and related display tags, not arbitrary shared ability-strength values. Unknown or ambiguous structures restrict writes.

### 6.3 Batch-edit units

1. Check the target units. At least two checked units switch the existing right inspector to batch editing; you can also open the batch-edit controls manually.
2. Select the checked-unit or current-filtered-results scope and verify the count.
3. Set common fields together. Different original values appear as mixed, with an empty input awaiting a target. Empty does not mean zero.
4. For formulas, select a field and operation. Basic mode offers fixed values and percentage changes. Professional mode adds multiplication, fixed addition/subtraction, rounding and limits.
5. Preview targets, results and related effects, then choose **Add to drafts**. A 10% increase multiplies the original value by `1.1`; it does not add `10` to every object. Integer results also follow the selected rounding rule.
6. Write the result through the [common application workflow](#drafts). Clear checkboxes and close manual batch editing to return to the single-object inspector.

### 6.4 Create a unit from a template

Choose **New Unit** and follow the five pages:

1. **Template:** Select an existing unit from the current Mod as the source of fields, model, animations and slots.
2. **Basic settings:** Enter a display name and adjust the offered fields and abilities. The suggested variable name increments from the template; professional mode allows editing it.
3. **Weapon configuration:** Select existing Ammo for the fixed slots or reset to the template. Changed Weapons are isolated automatically. Choose the independent-weapon option if you need the whole weapon configuration copied.
4. **Available divisions:** Check the required divisions and configure the offered card and transport settings. Verify the selected row before adjusting transport.
5. **Creation draft:** Read the summary and choose **Add to drafts**. Formal data has not been written yet.

Use **Edit Creation Settings** to revise a pending unit. Applying writes the identity, name, registrations and selected division rules together. The new unit retains the template's appearance; replacing Ammo does not automatically adapt models or firing animations.

### 6.5 Variable names, registration and deletion

- **Rename unit variable** in professional mode changes the internal identifier and updates verifiable references. It does not mean changing the display name; GUIDs, name tokens and registration numbers have separate roles.
- **Check unit registration** can generate a targeted repair draft when the issue is identifiable. Read the diagnostic and proposed changes; there is no need to run it repeatedly on every unit without a reason.
- **Delete created unit / undo deletion** can cancel an unapplied creation. Deleting an applied unit requires verification of its creation backup and identity in the current project.
- The deletion preview lists references. Safe division-rule and transport-candidate cleanup is linked to deletion; other users may need a replacement or removal of their reference first. Shared Weapon/Ammo resources remain.
- Undo pending deletion before applying, or restore through file-level backups afterward. This is not a general-purpose command to delete arbitrary vanilla units.

### 6.6 Unit portraits

1. Select a unit and open **Change unit picture**. Choose an existing picture in the Mod or import a custom image.
2. In the separate image editor, crop, zoom for inspection or adjust circular-region transparency. Check the result against light and dark preview backgrounds.
3. Confirm the image edit, then choose **Save draft** in the unit picture window. Reopening it shows the pending result.
4. Preview and apply, then generate the Mod. Custom pictures use independent PNG files and texture keys, redirecting only the selected unit and keeping the original image.

PNG/JPEG inputs must not exceed 8 MB or 4096 pixels on either edge. The encoded output PNG in a draft also has a size limit; reduce the image if validation rejects it. This changes the card portrait, not the 3D model, camouflage or animations.

<a id="weapons-ammo"></a>

## 7. Weapons and ammunition

### 7.1 Choose the right entry point

| Goal | Entry point and scope |
| --- | --- |
| Improve one unit's weapon only | Use Weapons with current-Unit scope; necessary references are isolated |
| Change weapon parameters for several units | Use Weapons with selected-Unit scope and check every target |
| Change an ammunition type for all its users | Edit shared Ammo in Ammunition and inspect its users |
| Adjust several ammunition types at once | Check them in Ammunition and batch edit; all users of each Ammo are affected |

Country, type or other filters on the ammunition list select Ammo objects. **They do not restrict shared ammunition effects to a filtered set of units.**

### 7.2 Weapons: mounts and local changes

1. Search for and select a Unit, then select the Weapon/mount to edit.
2. Confirm the scope first: current Unit by default, or selected Units. Professional mode also offers all references.
3. Edit recognized stock/salvo values, `NbWeapons`, hiding, turret angles, or select an existing Ammo replacement.
4. Adjust supported parameters of the mounted Ammo. Local changes clone the minimum necessary `Ammo → Weapon → Unit` reference chain.
5. Inspect shared effects, common ammo-box or turret relationships in the draft preview before applying.

The interface shows ammo-box indices, salvos, projectiles per salvo and calculated total ammunition where possible; these are different quantities. EffectTag, animation keys and other presentation references are for inspection. Arbitrary slot addition/deletion and a whole-Weapon replacement picker are not available.

### 7.3 Ammunition: shared objects and batches

For a single record, search and select Ammo, then edit supported names, ranges, damage, suppression, splash, accuracy, dispersion, firing, ballistics, supply and behavior fields. Expand the references view to inspect the Ammo → Weapon → Unit chain. Both parameter changes and renaming affect every user.

For batch editing:

1. Check several Ammo records or open batch editing, then select checked ammunition or current filtered results. Browsing another row does not change the checkboxes.
2. Check the hidden-selection count. Common fields can be set together; mixed-value inputs remain empty until you enter a target.
3. Use fixed values, percentages or professional formulas. Formulas operate on each record's effective current value. Specify integer rounding; decimals retain fractions by default.
4. Preview every result and the searchable user list, then choose **Add to drafts**.
5. Preview and apply in Draft overview. Resolve any conflict with local edits to the same field or related Ammo replacement drafts.

Unused Ammo and projects containing only ammunition files are supported. The old standalone weapon-batch window has been removed. Existing historical batches can still be applied or removed in the draft center; do not follow old instructions that ask you to open that window.

<a id="divisions"></a>

## 8. Divisions, emblems and descriptive text

### 8.1 Unit pools and costs

1. Open **Divisions**, search and select a division. Filters include country, faction, type and draft status where available.
2. In **Unit pool**, use **Add Unit** to select a unit. Search by display name or internal identifier, then filter by country, faction, production tab or role.
3. Select its rule row and adjust maximum cards, units per card, deployment-veterancy settings and transport candidates. Transport supports multiple selections; finish with the action that applies the selection. This confirms the picker, not a formal file write.
4. In division settings/costs, adjust type, tags, featured units and cost curves. Basic mode provides ten separate values per category; extra trailing entries are handled in professional mode.
5. Check the division's combined changes in Draft overview and apply.

New transport candidates must have a uniquely recognized transport module. Existing compatibility values whose transport capability cannot be confirmed are preserved and marked. Removing a unit-pool rule is blocked if a read-only default deck still references the unit.

### 8.2 Create or rename a division

Choose **Create tactical division**, select a template, name and emblem, then add the draft. Reopen the same entry point to continue a pending division. After applying creation, edit its unit pool and costs through the regular division page.

Creation automatically produces independent rule, cost and default-deck data. **This does not provide a default-deck editor.** Existing default decks remain read-only. Use **Name and emblem** for an existing division's identity.

### 8.3 Emblem images and fixed templates

1. In the creation or **Name and emblem** window, use **Choose existing emblem** to search the thumbnail gallery, or **Import or edit image** to import PNG/JPEG.
2. The image editor supports zooming for inspection, rectangular cropping, transparency inside/outside a circle, undo and reset. Confirm the image and return to division settings.
3. For a numbered emblem, use **Generate from template**. Ten fixed templates cover Guards, East German, airborne, black shield, four Polish geometric designs and two Czech central symbols. Numbers use 0–3 digits; Czech designs offer four colors. The Polish parachute-and-anchor design is not included.
4. Choose **Add to drafts** in division settings, then preview, apply and generate.

Templates include reconstructed artwork and are not guaranteed pixel-identical to vanilla assets. Custom PNGs, texture declarations and target division references are committed together, preserving originals and other users. Unit portraits use their own entry point and do not use emblem templates.

### 8.4 Gameplay descriptions and history

1. Select an existing division and open **Division text**. For a new division, apply creation before editing its text.
2. If originals are unavailable, choose the game directory and extract/refresh original division text. Wait for completion; extraction can be cancelled.
3. Switch the Chinese/English original-reference language and expand **Original reference and token**. Switching reference language does not overwrite your input. Use the action to adopt the current reference text if you want to copy it into the editor.
4. Edit gameplay and history paragraphs, then choose **Add to drafts**. The page's revert-input action discards input not yet saved on that page.
5. Preview, apply and generate. Custom text receives an independent reference, preserving other divisions' original text.

Custom text is the default across languages. It is not translated automatically and does not inherit the old token's other-language translations. Featured units are edited in division settings/costs, separately from descriptive text.

<a id="strategic"></a>

## 9. Army General and Strategic Packs

### 9.1 Army General: existing formations

1. Open **Army General**, search for and select an existing strategic battalion.
2. Expand the battalion/regiment → company → platoon/group → unit tree and select the level you intend to change.
3. Use the level's available add, delete, move, reorder, rename or HQ actions. For unit entries, change unit, quantity, veterancy and transport.
4. Under pawn properties, edit recognized action points, recovery, movement, roles, zones of control, support ranges, strategic influence, names or existing icon references.
5. Review the concrete formation differences in the draft, then apply. The editor recalculates required PackIndex values and isolates shared definitions for the selected target.

This edits the contents of existing battalions. It does not create a whole strategic battalion or edit campaign maps, events or stories. Unrecognized fields are not written by guesswork.

### 9.2 Strategic Packs (SP): shared combinations

A Pack represents a combination of unit, transport, veterancy and related data referenced by strategic formations. The standalone SP page edits the shared Pack itself, affecting every formation that references it. This differs from Army General changes isolated to the current formation.

1. Open **Strategic Packs (SP)**, search by name, unit or transport, and select an entry. In narrow windows, use the detail/list actions to switch views.
2. Set unit, transport or no transport, veterancy and Pack name. Generating a readable name only proposes a name; it still needs saving.
3. Check usage locations and the impact summary. Select a usage to jump to Army General, or double-click the reference row.
4. Choose **Add to drafts**, inspect shared effects and apply through the common workflow. Pack renaming updates verifiable references and checks collisions.
5. When switching away from unsaved Pack edits, choose to save the draft and switch, discard and switch, or cancel.

Formation quantities and indices remain in Army General. If formation data is incomplete, usage counts cover recognized files only and do not establish that every reference has been found.

<a id="rules"></a>

## 10. Game rules, experience and terrain

Open **Game Rules** and use search and expandable categories to find settings. Descriptions, original parameters and editability depend on the current Mod. Shared rule changes may affect eligible units on both sides.

### 10.1 General rules and air layouts

General rules are organized into match, economy, AI, combat behavior, logistics, Army General and air categories. Read a field's explanation, change its value, confirm the draft and inspect related changes in preview. Settings such as player slots may resize associated tables; check more than the single number.

Air layouts provide grid presets and card scaling:

1. Choose the desired row/column arrangement; capacity is rows × columns.
2. Set the card multiplier, wait for the automatic draft save, then inspect the related layout preview and apply.
3. Scaling uses the card dimensions in the current formal files. Repeated previews do not compound. After application and reload, the new dimensions become the `1×` baseline, so another multiplier acts on those new dimensions.
4. Takeoff interval, firing during evacuation and evacuation altitude are professional-mode settings. Check the actual layout and behavior in the game after generation.

### 10.2 Experience-route levels and effects

1. Expand experience/veterancy, then a route and level.
2. Inspect the route's users and confirm that you intend to change a shared route rather than merely select a different route for one unit.
3. Edit existing thresholds and supported effect values, then wait for the automatic draft save.
4. Preview all affected references and related files before applying.

Routes can have different level structures. Missing, unknown or multiply shared effects not supported for editing remain read-only. The tool does not automatically create missing levels, create unit-specific routes or update in-game tooltip text.

### 10.3 Terrain rules

Basic mode offers damage-reduction percentages for recognized existing infantry damage combinations. Professional mode also edits existing damage multipliers, concealment, visibility, movement blocking, aviation and flammability settings.

1. Find the terrain/combination and read its description and affected scope.
2. Change an editable field. Enter percentages and multipliers according to the field's meaning; they are not interchangeable.
3. Check before/after values in drafts, then apply and generate.

Changes affect eligible units on both sides in the current Mod. Terrain height is read-only. Terrain definitions and damage combinations cannot be added or removed, and missing combinations are not filled with zero. Projects containing only terrain-rule files can use the relevant functionality.

<a id="professional"></a>

## 11. Professional tools and reference inspection

Enable **Professional mode** for additional raw values, field paths, file locations and source information, and access to the reference workbench.

1. Search for objects and fields in the object index or workbench.
2. Follow references to see who uses an object. Default object order is source file, line number and then character position; click column headers to change sorting.
3. Edit supported controlled numerical fields and inspect drafts and paginated before/after source text.
4. Apply through the common workflow. Source previews, identities, reference indices and unsupported fields may be read-only; this is not an unrestricted text editor.

English mode translates the editor's interface. User-created names, internal identifiers, external tool output and unmapped diagnostics may remain in their original language.

<a id="mod-tools"></a>

## 12. Creation, generation, development launch and upload

| Action | Entry point | Purpose and prerequisites |
| --- | --- | --- |
| Create a Mod | Welcome page / Mod Tools | Runs official creation scripts; select the parent Mods directory, as in the [creation steps](#open-mod) |
| Generate / compile | Top bar | Processes the current Mod's formal source files; unapplied drafts are excluded |
| Launch development mode | Mod Tools | Runs the available official development workflow for the current project |
| Upload Mod | Mod Tools | Runs the project's official upload workflow; read the confirmation and full output |

Each action independently detects its required scripts and environment. An unavailable action does not make ordinary parameter editing unavailable. The editor does not need an external Python installation; explicitly invoked official BAT files use the game's bundled tools.

Before generation, confirm the project path and apply the drafts you want included. Afterward, inspect the exit result and output. Upload may also invoke an official backup; a backup message alone is not proof that upload completed. Editor transaction backups and official workflow backups serve different purposes.

Game updates may change official scripts or file structures; follow the workflow provided by the installed game. This guide has not run creation, generation, the game or upload on your behalf, and successful file writing is not proof of correct in-game effects.

<a id="help"></a>

## 13. Settings, troubleshooting and feedback

### 13.1 Original names, images and division text

Open **Settings → General → Original game names**, choose the WARNO folder containing `Data/PC`, and refresh names if needed. Refresh runs after closing Settings and reloads the project; existing drafts are saved first.

The current Mod's CSV names take priority. Without dictionaries, internal identifiers and ordinary parameter editing remain available, but creating/renaming requires dictionary loading and collision checks. The name cache is `%LOCALAPPDATA%/WarnoLiteModdingTool/names/cache-v1.json`; official images are cached under `%LOCALAPPDATA%/WarnoLiteModdingTool/images/v1/`. These are separate from Mod drafts and are not substitutes for them.

Unit images prioritize custom PNGs in the current Mod; other official images come from the local game. Missing images generally do not prevent numerical editing. Extract/refresh division text separately on its own page.

### 13.2 Appearance, panels and loading cache

- Language choices are system default, Simplified Chinese and English. The default editing mode is under General.
- Appearance offers seven themes and a custom background. A fresh installation without a valid theme uses light blue. Background opacity and layout are adjustable.
- Detachable panels are disabled by default under General. Enable them and choose large panels or small expandable groups, then double-click a supported heading to detach it. Closing the floating window returns the panel. The emblem editor is already a separate window.
- Caching the last opened Mod is enabled by default. Disable or clear it under General; the setting affects the next open. Source changes, application updates or cache damage trigger reloading. The name cache is separate.
- Upgrading to 1.9.15 rebuilds the old cache once. Caching speeds up browsing/loading; formal previews and commits still recheck files.

### 13.3 Troubleshooting

| Symptom | What to check |
| --- | --- |
| Mod not found | Manually select the root containing GameData; confirm it is not compiled output only |
| Module or action unavailable | Read its reason/tooltip and Problems; check the relevant source files or official scripts instead of borrowing unrelated files from another Mod |
| Internal names only, or renaming blocked | Select the WARNO folder and wait for names to load; the Mod's custom CSV names still take priority |
| Missing images or division text | Check the game folder, target reference and extraction status; refresh division text from its page |
| Entered value is absent from drafts | Check validation/save status; batches, SP, division text and dialog actions require Add to drafts or Save draft |
| Clicking one row still shows batch editing | Check for multiple checked objects; clear checkboxes and close manual batch editing |
| Other units changed too | Check for shared Ammo/SP/rule editing or all-reference scope; use previews and backups to resolve it |
| Preview reports a conflict | Inspect changed files, references or related drafts, resolve the issue and preview again |
| Commit succeeded but refresh failed | Reopen the same Mod and verify the result before applying again |
| Draft save failed | Keep the input, check folder permissions, disk space and error details, then retry |
| Generation failed | Read the specific error in the full official output and check paths/source data; this is a different stage from saving drafts |
| No change in the game | Confirm drafts were applied, generation succeeded, the correct Mod is enabled and the correct object/scenario is being checked |

### 13.4 Report a problem

Open Problems from the top bar. Mod Problems covers project data, parsing, drafts/transactions and official workflows. Tool Problems covers application exceptions. Copy the full diagnostic with its error ID or open the log directory. Ignoring a problem affects its display, not application validation.

For reports to **QQ group 1013181135**, include the tool version, module, failing step, error ID/full diagnostic and reproduction steps. State the selected scope for shared edits, and include official output for generation issues. Screenshots help locate UI problems but do not replace diagnostic text.

<a id="limits"></a>

## 14. Current limits

- The editor targets structurally compatible Mods; it cannot safely write every unknown NDF structure. Missing, ambiguous or conflicting structures preserve original text and restrict the affected feature.
- Units and divisions can be created from templates. Blank unit creation, cross-Mod unit import, arbitrary existing-unit deletion and whole-division deletion are not supported.
- There is no separate default-deck editing page. Automatic registration during template creation does not mean unrestricted default-deck editing.
- Arbitrary weapon-slot changes and model, animation or camouflage authoring are not provided. Image editing covers unit portraits and division-emblem PNGs.
- Army General edits existing battalion contents and pawn properties, not whole-battalion creation or campaign maps, events and stories.
- Unit-specific experience-route creation, higher level limits, automatic translation of custom text and automatic tooltip updates are not provided. Terrain height is read-only; terrain definitions and combinations cannot be added or removed.
- Cross-Mod batch presets are not saved. Drafts, backups and affected objects belong to their projects; another Mod is not a fallback source for missing data.
- Releases target self-contained Windows x64. Minimum Windows versions and real cross-monitor DPI behavior still need further validation. Generation/in-game checks remain incomplete for custom images, air layouts, weapon count and some newer rules; see the relevant [release notes](RELEASE_NOTES.md#english) for version-specific evidence.

[Back to contents](#contents) · [Home](README.md#english) · [中文教程](使用教程.md)
