using System;
using System.Collections.Generic;
using System.Linq;
using Timberborn.SelectionSystem;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace Calloatti.NaturalResourcesStatistics
{
  public class PlotHighlightService : IUpdatableSingleton, IDisposable
  {
    private readonly AreaHighlightingService _areaHighlightingService;
    private readonly EventBus _eventBus;

    private IReadOnlyCollection<Vector3Int> _currentTiles;
    private readonly Color _fillColor = new Color(1f, 0f, 1f, 1f);

    public bool IsActive => _currentTiles != null && _currentTiles.Count > 0;

    public PlotHighlightService(AreaHighlightingService areaHighlightingService, EventBus eventBus)
    {
      _areaHighlightingService = areaHighlightingService;
      _eventBus = eventBus;
    }

    public void Load()
    {
      _eventBus.Register(this);
    }

    public void SetHighlightedTiles(IEnumerable<Vector3Int> tiles)
    {
      _currentTiles = tiles.ToList();
    }

    public void Clear()
    {
      _currentTiles = null;
      _areaHighlightingService.UnhighlightAll();
    }

    public void UpdateSingleton()
    {
      if (_currentTiles == null || _currentTiles.Count == 0)
        return;

      foreach (var coord in _currentTiles)
      {
        _areaHighlightingService.DrawTile(coord, _fillColor);
      }
      _areaHighlightingService.Highlight();
    }

    public void Dispose()
    {
      _eventBus?.Unregister(this);
    }
  }
}
