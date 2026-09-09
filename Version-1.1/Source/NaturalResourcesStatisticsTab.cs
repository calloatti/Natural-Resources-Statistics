using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Timberborn.BatchControl;
using Timberborn.BlockSystem;
using Timberborn.CoreUI;
using Timberborn.DropdownSystem;
using Timberborn.EntitySystem;
using Timberborn.Growing;
using Timberborn.Localization;
using Timberborn.NaturalResources;
using Timberborn.NaturalResourcesLifecycle;
using Timberborn.Planting;
using Timberborn.SelectionSystem;
using Timberborn.SingletonSystem;
using Timberborn.TerrainSystem;
using Timberborn.TemplateSystem;
using Timberborn.TooltipSystem;
using Timberborn.UISound;
using Timberborn.Yielding;
using UnityEngine;
using UnityEngine.UIElements;

namespace Calloatti.NaturalResourcesStatistics
{
  public class NaturalResourcesStatisticsTab : BatchControlTab
  {
    private new readonly VisualElementLoader _visualElementLoader;
    private readonly BatchControlRowGroupFactory _rowGroupFactory;
    private readonly ITerrainService _terrainService;
    private readonly ILoc _loc;
    private readonly DropdownItemsSetter _dropdownItemsSetter;
    private readonly FilterDropdownProvider _filterProvider;
    private readonly AreaHighlightingService _areaHighlightingService;
    private readonly UISoundController _uiSoundController;
    private readonly PlantingService _plantingService;
    private readonly TemplateNameMapper _templateNameMapper;
    private readonly PlotHighlightService _plotHighlightService;

    private readonly Dictionary<string, VisualElement> _rowRoots = new Dictionary<string, VisualElement>();
    private string _selectedTemplateName;

    public override string TabNameLocKey => "Calloatti.NRS.Tab";
    public override string TabImage => "NaturalResourcesStatistics";
    public override string BindingKey => "Calloatti.NRS.KeyBind";
    public override bool IgnoreDistrictSelection => true;

    public NaturalResourcesStatisticsTab(
        VisualElementLoader visualElementLoader,
        BatchControlDistrict batchControlDistrict,
        EventBus eventBus,
        BatchControlRowGroupFactory rowGroupFactory,
        ITerrainService terrainService,
        ILoc loc,
        ITooltipRegistrar tooltipRegistrar,
        AreaHighlightingService areaHighlightingService,
        UISoundController uiSoundController,
        PlantingService plantingService,
        TemplateNameMapper templateNameMapper,
        PlotHighlightService plotHighlightService)
        : base(visualElementLoader, batchControlDistrict, eventBus)
    {
      _visualElementLoader = visualElementLoader;
      _rowGroupFactory = rowGroupFactory;
      _terrainService = terrainService;
      _loc = loc;
      _dropdownItemsSetter = new DropdownItemsSetter(visualElementLoader, tooltipRegistrar, loc);
      _filterProvider = new FilterDropdownProvider(this, loc);
      _areaHighlightingService = areaHighlightingService;
      _uiSoundController = uiSoundController;
      _plantingService = plantingService;
      _templateNameMapper = templateNameMapper;
      _plotHighlightService = plotHighlightService;
    }

    [OnEvent]
    public void OnSelectableObjectUnselected(SelectableObjectUnselectedEvent evt)
    {
      ClearHighlight();
    }

    [OnEvent]
    public void OnBatchControlBoxHidden(BatchControlBoxHiddenEvent evt)
    {
      ClearHighlight();
    }

    public override void Hide()
    {
      ClearHighlight();
    }

    private void ClearHighlight()
    {
      if (_selectedTemplateName != null && _rowRoots.TryGetValue(_selectedTemplateName, out var root))
      {
        root.EnableInClassList("batch-control-box__row--highlighted", false);
      }
      _selectedTemplateName = null;
      _areaHighlightingService.UnhighlightAll();
      _plotHighlightService.Clear();
    }

    public override IEnumerable<BatchControlRowGroup> GetRowGroups(IEnumerable<EntityComponent> entities)
    {
      _rowRoots.Clear();

      string filter = _filterProvider.GetValue();

      if (filter == "Plots")
      {
        foreach (var group in GetPlotRowGroups())
          yield return group;
        yield break;
      }

      var naturalResources = entities
          .Where(e => e.GetComponent<NaturalResource>() != null)
          .Distinct()
          .ToList();

      var speciesGroups = naturalResources
          .GroupBy(e => e.GetComponent<TemplateSpec>().TemplateName)
          .ToList();

      var headerRow = CreateHeader();
      var rowGroup = _rowGroupFactory.CreateUnsorted(headerRow);

      var rowItems = new List<SpeciesData>();

      foreach (var group in speciesGroups)
      {
        string templateName = group.Key;
        string displayName = "";
        Sprite icon = null;

        foreach (var entity in group)
        {
          if (string.IsNullOrEmpty(displayName))
          {
            var labeledEntity = entity.GetComponent<LabeledEntity>();
            if (labeledEntity != null)
            {
              displayName = labeledEntity.DisplayName;
              icon = labeledEntity.Image;
            }
          }
        }

        rowItems.Add(new SpeciesData(templateName, displayName, icon, group.ToList()));
      }

      rowItems = ApplySort(rowItems);

      foreach (var item in rowItems)
      {
        GetFilteredCounts(item, out int total, out int healthy, out int dying, out int dead, out int mature);
        if (total == 0) continue;

        VisualElement rowRoot = _visualElementLoader.LoadVisualElement("Game/BatchControl/BatchControlRow");

        IBatchControlRowItem iconItem = CreateSpeciesIcon(item, rowRoot);
        IBatchControlRowItem nameLabel = CreateSpeciesName(item.DisplayName);
        IBatchControlRowItem healthyLabel = CreateLabel(healthy.ToString(), TextAnchor.MiddleRight, 60);
        IBatchControlRowItem dyingLabel = CreateLabel(dying.ToString(), TextAnchor.MiddleRight, 60);
        IBatchControlRowItem deadLabel = CreateLabel(dead.ToString(), TextAnchor.MiddleRight, 60);
        IBatchControlRowItem matureLabel = CreateLabel(mature.ToString(), TextAnchor.MiddleRight, 60);
        IBatchControlRowItem countLabel = CreateLabel(total.ToString(), TextAnchor.MiddleRight, 60, FontStyle.Bold);

        BatchControlRow row = new BatchControlRow(rowRoot, (EntityComponent)null, iconItem, nameLabel, healthyLabel, dyingLabel, deadLabel, matureLabel, countLabel);
        rowGroup.AddRow(row);

        _rowRoots[item.TemplateName] = rowRoot;

        if (item.TemplateName == _selectedTemplateName)
        {
          rowRoot.EnableInClassList("batch-control-box__row--highlighted", true);
        }
      }

      yield return rowGroup;
    }

    private IEnumerable<BatchControlRowGroup> GetPlotRowGroups()
    {
      var headerRow = CreatePlotsHeader();
      var rowGroup = _rowGroupFactory.CreateUnsorted(headerRow);

      var plotItems = new List<PlotSpeciesData>();

      foreach (var coord in _plantingService.PlantingCoordinates)
      {
        string templateName = _plantingService.GetResourceAt(coord);
        if (string.IsNullOrEmpty(templateName)) continue;

        var existing = plotItems.FirstOrDefault(p => p.TemplateName == templateName);
        if (existing != null)
        {
          existing.Coordinates.Add(coord);
        }
        else
        {
          string displayName = "";
          Sprite icon = null;

          if (_templateNameMapper.TryGetTemplate(templateName, out var templateSpec))
          {
            var labeledSpec = templateSpec.Blueprint.GetSpec<LabeledEntitySpec>();
            if (labeledSpec != null)
            {
              displayName = _loc.T(labeledSpec.DisplayNameLocKey);
              icon = labeledSpec.Icon.Asset;
            }
          }

          if (string.IsNullOrEmpty(displayName)) displayName = templateName;

          plotItems.Add(new PlotSpeciesData(templateName, displayName, icon, new List<Vector3Int> { coord }));
        }
      }

      plotItems = ApplySort(plotItems);

      foreach (var item in plotItems)
      {
        VisualElement rowRoot = _visualElementLoader.LoadVisualElement("Game/BatchControl/BatchControlRow");

        IBatchControlRowItem iconItem = CreatePlotsIcon(item, rowRoot);
        IBatchControlRowItem nameLabel = CreateSpeciesName(item.DisplayName);
        IBatchControlRowItem countLabel = CreateLabel(item.Coordinates.Count.ToString(), TextAnchor.MiddleRight, 60, FontStyle.Bold);

        BatchControlRow row = new BatchControlRow(rowRoot, (EntityComponent)null, iconItem, nameLabel, countLabel);
        rowGroup.AddRow(row);

        _rowRoots[item.TemplateName] = rowRoot;

        if (item.TemplateName == _selectedTemplateName)
        {
          rowRoot.EnableInClassList("batch-control-box__row--highlighted", true);
        }
      }

      yield return rowGroup;
    }

    private IBatchControlRowItem CreateSpeciesIcon(SpeciesData item, VisualElement rowRoot)
    {
      VisualElement visualElement = _visualElementLoader.LoadVisualElement("Game/BatchControl/BuildingBatchControlRowItem");
      visualElement.Q<VisualElement>("ConstructionWrapper").ToggleDisplayStyle(false);
      visualElement.Q<VisualElement>("PausableWrapper").ToggleDisplayStyle(false);
      visualElement.Q<VisualElement>("DistanceWrapper").ToggleDisplayStyle(false);

      Image image = visualElement.Q<Image>("Image");
      image.sprite = item.Icon;

      visualElement.Q<Button>("Select").RegisterCallback<ClickEvent>(evt =>
      {
        _uiSoundController.PlayClickSound();

        if (_selectedTemplateName != null && _rowRoots.TryGetValue(_selectedTemplateName, out var prevRoot))
        {
          prevRoot.EnableInClassList("batch-control-box__row--highlighted", false);
        }

        rowRoot.EnableInClassList("batch-control-box__row--highlighted", true);
        _selectedTemplateName = item.TemplateName;

        _plotHighlightService.Clear();
        _areaHighlightingService.UnhighlightAll();
        string filter = _filterProvider.GetValue();
        foreach (var entity in item.Entities)
        {
          var blockObject = entity.GetComponent<BlockObject>();
          if (blockObject == null) continue;

          bool isPlanted = _terrainService.CellIsField(blockObject.Coordinates);
          if (filter == "Planted" && !isPlanted) continue;
          if (filter == "Wild" && isPlanted) continue;

          _areaHighlightingService.AddForHighlight(blockObject);
        }
        _areaHighlightingService.Highlight();
      });

      return new SimpleRowItem(visualElement);
    }

    private IBatchControlRowItem CreatePlotsIcon(PlotSpeciesData item, VisualElement rowRoot)
    {
      VisualElement visualElement = _visualElementLoader.LoadVisualElement("Game/BatchControl/BuildingBatchControlRowItem");
      visualElement.Q<VisualElement>("ConstructionWrapper").ToggleDisplayStyle(false);
      visualElement.Q<VisualElement>("PausableWrapper").ToggleDisplayStyle(false);
      visualElement.Q<VisualElement>("DistanceWrapper").ToggleDisplayStyle(false);

      Image image = visualElement.Q<Image>("Image");
      image.sprite = item.Icon;

      visualElement.Q<Button>("Select").RegisterCallback<ClickEvent>(evt =>
      {
        _uiSoundController.PlayClickSound();

        if (_selectedTemplateName != null && _rowRoots.TryGetValue(_selectedTemplateName, out var prevRoot))
        {
          prevRoot.EnableInClassList("batch-control-box__row--highlighted", false);
        }

        rowRoot.EnableInClassList("batch-control-box__row--highlighted", true);
        _selectedTemplateName = item.TemplateName;

        _areaHighlightingService.UnhighlightAll();
        _plotHighlightService.SetHighlightedTiles(item.Coordinates);
      });

      return new SimpleRowItem(visualElement);
    }

    private IBatchControlRowItem CreateLabel(string text, TextAnchor alignment, float width, FontStyle fontStyle = FontStyle.Normal)
    {
      Label label = new Label(text);
      label.style.unityTextAlign = alignment;
      label.style.width = width;
      label.style.minWidth = width;
      label.style.maxWidth = width;
      label.style.marginLeft = 5;
      label.style.marginRight = 5;
      label.style.unityFontStyleAndWeight = fontStyle;
      return new SimpleRowItem(label);
    }

    private IBatchControlRowItem CreateSpeciesName(string text)
    {
      Label label = new Label(text);
      label.style.unityTextAlign = TextAnchor.MiddleLeft;
      label.style.flexGrow = 1;
      label.style.marginLeft = 4;
      return new SimpleRowItem(label);
    }

    private void GetFilteredCounts(SpeciesData item, out int total, out int healthy, out int dying, out int dead, out int mature)
    {
      string filter = _filterProvider.GetValue();
      total = 0;
      healthy = 0;
      dying = 0;
      dead = 0;
      mature = 0;

      foreach (var entity in item.Entities)
      {
        var blockObject = entity.GetComponent<BlockObject>();
        if (blockObject == null) continue;

        bool isPlanted = _terrainService.CellIsField(blockObject.Coordinates);
        if (filter == "Planted" && !isPlanted) continue;
        if (filter == "Wild" && isPlanted) continue;

        total++;

        var livingComponent = entity.GetComponent<LivingNaturalResource>();
        var dyingComponent = entity.GetComponent<DyingNaturalResource>();

        if (livingComponent != null && livingComponent.IsDead)
        {
          dead++;
        }
        else if (dyingComponent != null && dyingComponent.IsDying)
        {
          dying++;
        }
        else
        {
          healthy++;
        }

        var growable = entity.GetComponent<Growable>();
        if (growable != null && growable.IsGrown)
        {
          mature++;
        }
      }
    }

    private List<SpeciesData> ApplySort(List<SpeciesData> items)
    {
      return items.OrderBy(x => x.DisplayName).ToList();
    }

    private List<PlotSpeciesData> ApplySort(List<PlotSpeciesData> items)
    {
      return items.OrderBy(x => x.DisplayName).ToList();
    }

    private BatchControlRow CreateHeader()
    {
      var headerElement = _visualElementLoader.LoadVisualElement("Game/BatchControl/BatchControlHeaderRow");
      headerElement.Clear();
      headerElement.style.justifyContent = Justify.FlexStart;
      headerElement.style.height = 42;

      var iconSpacer = new VisualElement();
      iconSpacer.style.width = 38;
      iconSpacer.style.minWidth = 38;
      iconSpacer.style.maxWidth = 38;
      iconSpacer.style.marginLeft = 1;
      iconSpacer.style.marginRight = 1;

      var nameLabel = new Label(_loc.T("Calloatti.NRS.Header"));
      nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
      nameLabel.style.flexGrow = 1;
      nameLabel.style.marginLeft = 4;
      nameLabel.style.fontSize = 14;

      var healthyLabel = CreateHeaderLabel(_loc.T("Calloatti.NRS.Column.Healthy"));
      var dyingLabel = CreateHeaderLabel(_loc.T("Calloatti.NRS.Column.Dying"));
      var deadLabel = CreateHeaderLabel(_loc.T("Calloatti.NRS.Column.Dead"));
      var matureLabel = CreateHeaderLabel(_loc.T("Calloatti.NRS.Column.Mature"));
      var countLabel = CreateHeaderLabel(_loc.T("Calloatti.NRS.Filter.All"));

      headerElement.Add(iconSpacer);
      headerElement.Add(nameLabel);
      headerElement.Add(healthyLabel);
      headerElement.Add(dyingLabel);
      headerElement.Add(deadLabel);
      headerElement.Add(matureLabel);
      headerElement.Add(countLabel);

      return new BatchControlRow(headerElement);
    }

    private BatchControlRow CreatePlotsHeader()
    {
      var headerElement = _visualElementLoader.LoadVisualElement("Game/BatchControl/BatchControlHeaderRow");
      headerElement.Clear();
      headerElement.style.justifyContent = Justify.FlexStart;
      headerElement.style.height = 42;

      var iconSpacer = new VisualElement();
      iconSpacer.style.width = 38;
      iconSpacer.style.minWidth = 38;
      iconSpacer.style.maxWidth = 38;
      iconSpacer.style.marginLeft = 1;
      iconSpacer.style.marginRight = 1;

      var nameLabel = new Label(_loc.T("Calloatti.NRS.Header"));
      nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
      nameLabel.style.flexGrow = 1;
      nameLabel.style.marginLeft = 4;
      nameLabel.style.fontSize = 14;

      var countLabel = CreateHeaderLabel(_loc.T("Calloatti.NRS.Column.Plots"));

      headerElement.Add(iconSpacer);
      headerElement.Add(nameLabel);
      headerElement.Add(countLabel);

      return new BatchControlRow(headerElement);
    }

    private Label CreateHeaderLabel(string text)
    {
      var label = new Label(text);
      label.style.unityTextAlign = TextAnchor.MiddleRight;
      label.style.width = 60;
      label.style.minWidth = 60;
      label.style.maxWidth = 60;
      label.style.marginLeft = 5;
      label.style.marginRight = 5;
      label.style.fontSize = 14;
      return label;
    }

    public override VisualElement GetHeader()
    {
      var header = _visualElementLoader.LoadVisualElement("NaturalResourcesStatistics/NaturalResourcesStatisticsHeader");
      header.Q<Label>("FilterLabel").text = _loc.T("Calloatti.NRS.Filter");

      var filterDropdown = header.Q<Dropdown>("FilterDropdown");
      _dropdownItemsSetter.SetItems(filterDropdown, _filterProvider);

      return header;
    }

    public void SetFilter(string filter)
    {
      _plotHighlightService.Clear();
      IsDirty = true;
    }

    private class SimpleRowItem : IBatchControlRowItem
    {
      public VisualElement Root { get; }
      public SimpleRowItem(VisualElement root) { Root = root; }
    }

    private class FilterDropdownProvider : IExtendedDropdownProvider
    {
      private readonly NaturalResourcesStatisticsTab _tab;
      private readonly ILoc _loc;
      private string _selectedValue = "All";

      private static readonly string[] FilterKeys = { "All", "Planted", "Wild", "Plots" };

      public FilterDropdownProvider(NaturalResourcesStatisticsTab tab, ILoc loc)
      {
        _tab = tab;
        _loc = loc;
      }

      public IReadOnlyList<string> Items => FilterKeys;

      public string GetValue() => _selectedValue;

      public void SetValue(string value)
      {
        _selectedValue = value;
        _tab.SetFilter(value);
      }

      public string FormatDisplayText(string value, bool selected)
      {
        return _loc.T($"Calloatti.NRS.Filter.{value}");
      }

      public Sprite GetIcon(string value) => null;

      public ImmutableArray<string> GetItemClasses(string value) => ImmutableArray<string>.Empty;
    }

    private class SpeciesData
    {
      public string TemplateName { get; }
      public string DisplayName { get; }
      public Sprite Icon { get; }
      public List<EntityComponent> Entities { get; }

      public SpeciesData(string templateName, string displayName, Sprite icon, List<EntityComponent> entities)
      {
        TemplateName = templateName;
        DisplayName = displayName;
        Icon = icon;
        Entities = entities;
      }
    }

    private class PlotSpeciesData
    {
      public string TemplateName { get; }
      public string DisplayName { get; }
      public Sprite Icon { get; }
      public List<Vector3Int> Coordinates { get; }

      public PlotSpeciesData(string templateName, string displayName, Sprite icon, List<Vector3Int> coordinates)
      {
        TemplateName = templateName;
        DisplayName = displayName;
        Icon = icon;
        Coordinates = coordinates;
      }
    }
  }
}
