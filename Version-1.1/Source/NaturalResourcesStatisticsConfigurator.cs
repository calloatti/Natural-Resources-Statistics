using Bindito.Core;
using Timberborn.BatchControl;

namespace Calloatti.NaturalResourcesStatistics
{
  [Context("Game")]
  public class NaturalResourcesStatisticsConfigurator : Configurator
  {
    protected override void Configure()
    {
      Bind<NaturalResourcesStatisticsTab>().AsSingleton();
      MultiBind<BatchControlModule>().ToProvider<BatchControlModuleProvider>().AsSingleton();
    }

    private class BatchControlModuleProvider : IProvider<BatchControlModule>
    {
      private readonly NaturalResourcesStatisticsTab _tab;

      public BatchControlModuleProvider(NaturalResourcesStatisticsTab tab)
      {
        _tab = tab;
      }

      public BatchControlModule Get()
      {
        BatchControlModule.Builder builder = new BatchControlModule.Builder();
        builder.AddTab(_tab, 92);
        return builder.Build();
      }
    }
  }
}
