Include ..\AGENTS.md

# Natural Resources Statistics — Mod-Specific Agent Instructions

## Identity
- **Assembly:** `naturalresourcesstatistics`
- **Namespace:** `Calloatti.NaturalResourcesStatistics`
- **ModId:** `Calloatti.NaturalResourcesStatistics`
- **Framework:** Bindito DI
- **Min Game Version:** 1.1.2.4

## What This Mod Does
Adds a **Natural Resources Statistics** tab to the batch control panel (the tabbed panel opened via hotkey with tabs for Population, Housing, Workplaces, etc.). Shows all natural resources (trees, bushes, crops) grouped by species with health/planting breakdown. Full map view — no district filtering. Filter and sort dropdowns in the header.

## Source Architecture (`Version-1.1/Source/`)

| File | Role |
|---|---|
| `ModStarter.cs` | Entry point — `IModStarter`, logs startup |
| `NaturalResourcesStatisticsConfigurator.cs` | Bindito DI — binds tab as singleton, multi-binds `BatchControlModule` at order 7 |
| `NaturalResourcesStatisticsTab.cs` | `BatchControlTab` subclass — queries entities, groups by species, filter dropdown, row creation with vanilla templates, area highlighting |

## Asset Bundles (`Version-1.1/AssetBundles/`)

| Path | Role |
|---|---|
| `Resources/UI/Views/NaturalResourcesStatistics/NaturalResourcesStatisticsHeader.uxml` | Filter/sort dropdown header above the tab |
| `Resources/Sprites/BatchControl/NaturalResourcesStatistics.png` | Tab icon — `ico-planting` from game ripped assets |

Row templates use vanilla `Game/BatchControl/BatchControlRow` and `Game/BatchControl/BuildingBatchControlRowItem` — no custom row UXML needed.

Built via `unitybuild.ps1` → `NativeModBuilderBatch.Build` → produces `naturalresourcesstatistics_win` and `naturalresourcesstatistics_mac` bundles.

## Localization (`Version-1.1/Localizations/`)

| Key | Text |
|---|---|
| `Calloatti.NRS.Tab` | Natural Resources Statistics |
| `Calloatti.NRS.Header` | Species |
| `Calloatti.NRS.Filter` | Filter: |
| `Calloatti.NRS.Filter.All` | All |
| `Calloatti.NRS.Filter.Planted` | Planted |
| `Calloatti.NRS.Filter.Wild` | Wild |
| `Calloatti.NRS.Column.Healthy` | Healthy |
| `Calloatti.NRS.Column.Dying` | Dying |
| `Calloatti.NRS.Column.Dead` | Dead |
| `Calloatti.NRS.Column.Mature` | Mature |
| `Calloatti.NRS.Sort` | Sort: |
| `Calloatti.NRS.Sort.NameAsc` | Name A-Z |
| `Calloatti.NRS.Sort.NameDesc` | Name Z-A |
| `Calloatti.NRS.Sort.CountDesc` | Count (Most) |
| `Calloatti.NRS.Sort.CountAsc` | Count (Least) |

## Key APIs

### Entity Querying
- `entities` parameter in `GetRowGroups()` — all entities from `EntityComponentRegistry`, passed by the batch control framework
- `entity.GetComponent<NaturalResource>()` — marker component on all natural resources
- `entity.GetComponent<TemplateSpec>().TemplateName` — species identifier (e.g. `"MapleTree.Folktails"`) used for grouping

### Health State Detection
- `entity.GetComponent<LivingNaturalResource>().IsDead` — persisted, true when resource is dead
- `entity.GetComponent<DyingNaturalResource>().IsDying` — runtime only, true when dying from water/contamination/aridity
- Priority: check `IsDead` first, then `IsDying`, else healthy (mutually exclusive)

### Growth State
- `entity.GetComponent<Growable>().IsGrown` — true when growth progress reaches 100%

### Planted vs Wild Detection
- `entity.GetComponent<BlockObject>().Coordinates` — tile coordinates
- `ITerrainService.CellIsField(coordinates)` — true if tile has field texture (player-planted), false for wild-spawned
- The game does NOT persist a "planted" flag. Field tile is the only reliable indicator.
- `NaturalResourceFactory.PlantNew()` fires transient `NaturalResourcePlantedEvent` (not persisted)
- `NaturalResourceFactory.SpawnNew()` (reproduction) does NOT set field tile

### Display Data
- `entity.GetComponent<LabeledEntity>().DisplayName` — species display name
- `entity.GetComponent<LabeledEntity>().Image` — species icon sprite

### Dropdown System
- `DropdownItemsSetter` — inject via constructor, call `SetItems(dropdown, provider)` to populate
- `IExtendedDropdownProvider` — implement for dropdown items with format/icon/classes
- `Dropdown.ValueChanged` — DO NOT USE. Ambiguity from publicizer (CS0229). The provider's `SetValue()` handles state updates instead.
- `VisualElementLoader` — use `new` keyword to hide inherited member (CS0108 warning)

### Batch Control Tab Registration
- `BatchControlModule.Builder.AddTab(tab, order)` — order 7 puts it after Migration (order 7) and before GoodStatistics (order 90)
- `IgnoreDistrictSelection => true` — hides district dropdown, passes `null` for district filtering (shows all entities)
- `IsDirty = true` — marks tab for refresh on next `UpdateContent()` call

### Row Highlighting (Visual)
- **Row highlight:** `rowRoot.EnableInClassList("batch-control-box__row--highlighted", true/false)` on the vanilla `BatchControlRow` root
- **Area highlight:** `_areaHighlightingService.AddForHighlight(blockObject)` per entity, then `_areaHighlightingService.Highlight()` — persists across frames via `RollingHighlighter`
- **DO NOT use** `_areaHighlightingService.DrawTile()` — immediate-mode, only 1 frame
- **Clear on map click:** `[OnEvent] OnSelectableObjectUnselected(SelectableObjectUnselectedEvent)` calls `ClearHighlight()` (removes row class + `UnhighlightAll()`)
- **Click sound:** `_uiSoundController.PlayClickSound()` in the `ClickEvent` callback — `--click-sound` CSS via `customStyle` only works on the exact click target element, not children

### Filter Behavior
- Filter applies to ALL columns (healthy, dying, dead, mature, total), not just the total
- Filter affects area highlight (only highlights planted/wild entities matching the selection)
- Filter is recomputed at render time via `GetFilteredCounts()` — `SpeciesData` stores only display data, not counts

## Batch Control Architecture

### Tab Lifecycle
1. `BatchControlBoxTabController.UpdateEntities()` calls `_entityRegistry.Entities` — populates entity list
2. Tab's `GetContent(entities)` is called once when tab is shown → calls `GetRowGroups(entities)`
3. `GetRowGroups()` yields `BatchControlRowGroup` instances, each containing `BatchControlRow` items
4. `UpdateContent()` handles virtual scrolling (only updates visible rows)
5. `UpdateRowsVisibility()` handles district filtering (skipped when `IgnoreDistrictSelection=true`)

### Row Group Pattern
```csharp
var headerRow = CreateHeader();  // built in code with vanilla BatchControlHeaderRow template
var rowGroup = _rowGroupFactory.CreateUnsorted(headerRow);
rowGroup.AddRow(new BatchControlRow(rowRoot, (EntityComponent)null, iconItem, nameLabel, ...));
yield return rowGroup;
```

### Row Item Pattern (vanilla templates)
```csharp
// Row template — vanilla
VisualElement rowRoot = _visualElementLoader.LoadVisualElement("Game/BatchControl/BatchControlRow");

// Icon item — vanilla BuildingBatchControlRowItem, hide unused wrappers
VisualElement iconElement = _visualElementLoader.LoadVisualElement("Game/BatchControl/BuildingBatchControlRowItem");
iconElement.Q<VisualElement>("ConstructionWrapper").ToggleDisplayStyle(false);
iconElement.Q<VisualElement>("PausableWrapper").ToggleDisplayStyle(false);
iconElement.Q<VisualElement>("DistanceWrapper").ToggleDisplayStyle(false);
iconElement.Q<Image>("Image").sprite = item.Icon;

// Labels — simple Labels wrapped in SimpleRowItem
Label label = new Label(text);
label.style.unityTextAlign = TextAnchor.MiddleRight;
label.style.width = 60;
return new SimpleRowItem(label);

// Assembly
BatchControlRow row = new BatchControlRow(rowRoot, (EntityComponent)null, iconItem, nameLabel, healthyLabel, dyingLabel, deadLabel, matureLabel, countLabel);
```

### Header Row Pattern (built in code)
```csharp
var headerElement = _visualElementLoader.LoadVisualElement("Game/BatchControl/BatchControlHeaderRow");
headerElement.Clear();
headerElement.style.justifyContent = Justify.FlexStart;
headerElement.style.height = 42;

// 38px icon spacer + nameLabel (flexGrow=1) + numeric labels (width=60, marginRight=5)
```

## Publicizer Notes
- `Dropdown.ValueChanged` has CS0229 ambiguity from publicizer — the `Unsafe` strategy creates both event and backing field. Fix: do NOT subscribe to this event; use the provider's `SetValue()` callback instead.
- `VisualElementLoader` hides inherited member — use `new` keyword on field declaration.

## Version Folders
- `Version-1.1` — targets game 1.1.2.4

## Build & Deploy

**Requirements:** .NET SDK, Timberborn at `C:\Program Files (x86)\Steam\steamapps\common\timberborn_public\Timberborn_Data\Managed`, Unity 6000.5.5f1 for asset bundles

**Commands:**
```powershell
# C# build + deploy
dotnet build "Natural Resources Statistics.csproj" -c Release

# Asset bundle build (requires Unity)
.\unitybuild.ps1

# Quick rebuild
.\rebuild.cmd
```

**Deploy target:** `%USERPROFILE%\Documents\Timberborn\Mods\Natural Resources Statistics\Version-1.1\`

## Pitfalls

- **EntityComponentRegistry.GetAll\<T\>() requires IRegisteredComponent:** `NaturalResource` does NOT implement `IRegisteredComponent`. Use the `entities` parameter passed to `GetRowGroups()` instead.
- **Dropdown.ValueChanged ambiguity:** Publicizer creates CS0229. Do not subscribe to this event. The `IExtendedDropdownProvider.SetValue()` callback handles state changes.
- **VisualElementLoader hides inherited member:** `BatchControlTab` has a publicized `VisualElementLoader` field. Use `new` keyword on your field declaration to suppress CS0108.
- **Planted vs wild not persisted:** The game has no per-entity flag. `CellIsField(coordinates)` is the only reliable runtime check. Works because `PlantingService.SetPlantingCoordinates()` calls `TerrainService.SetField()`, while `SpawnValidationService.IsSuitableTerrain()` rejects field tiles for wild spawns.
- **DyingNaturalResource.IsDying is runtime only:** Not persisted. After save/load, dying state is recomputed from providers (AridNaturalResource, WateredNaturalResource, etc.).
- **Growable.IsGrown not persisted directly:** Growth progress is saved and re-applied via `FastForwardProgress()`. `IsGrown` is derived from `_timeTrigger.Finished`.
- **Tab icon sprite:** Must be at `Sprites/BatchControl/NaturalResourcesStatistics` in the asset bundle (loaded via `Resources.Load<Sprite>(path)`). The `ico-planting.png` from ripped assets is used.
- **`--click-sound` only works on exact click target:** `UISoundInitializer.PlayUISound` checks `currentTarget == target`. If the click target is a child element (e.g., Image inside Button), the parent's `--click-sound` won't fire. Use `UISoundController.PlayClickSound()` in the callback instead.
- **AreaHighlightingService.DrawTile() is immediate-mode:** Uses `Graphics.DrawMesh()` — only visible for 1 frame. Use `AddForHighlight(blockObject)` + `Highlight()` instead, which uses `RollingHighlighter` for persistence.
- **BatchControlTab.Load() registers [OnEvent] via reflection:** Do NOT call `_eventBus.Register(this)` manually — the base class handles it. `[OnEvent]` methods are auto-discovered.

## Hard Rule
DO NOT EVER TOUCH THE DEPLOY FOLDER.

BUILD DOES EVERYTHING, NEVER EVER MESS WITH THE DEPLOY PROCESS.
