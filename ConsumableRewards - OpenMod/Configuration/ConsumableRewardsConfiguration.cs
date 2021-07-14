using System;

namespace ConsumableRewards.Configuration
{
    [Serializable]
    public class ConsumableRewardsConfiguration
    {
        public Consumable[] Consumables { get; set; } = { };
    }
}
