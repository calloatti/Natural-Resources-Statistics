using Timberborn.ModManagerScene;
using UnityEngine;

namespace Calloatti.NaturalResourcesStatistics
{
  public class ModStarter : IModStarter
  {
    public void StartMod(IModEnvironment modEnvironment)
    {
      Debug.Log("[NaturalResourcesStatistics] Mod started");
    }
  }
}
