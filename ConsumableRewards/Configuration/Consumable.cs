using Microsoft.Extensions.DependencyInjection;
using OpenMod.Extensions.Games.Abstractions.Items;
using OpenMod.Unturned.Items;
using System;
using System.Threading.Tasks;

namespace ConsumableRewards.Configuration
{
    [Serializable]
    public class Consumable
    {
        public string? Item { get; set; }

        public string? Pattern { get; set; }

        public string? RequiresPermission { get; set; }

        public float? Chance { get; set; }

        public int? RewardLimit { get; set; }

        public Reward[] Rewards { get; set; } = { };

        private UnturnedItemAsset? _itemAsset;

        public async Task<UnturnedItemAsset?> GetItemAsset(IServiceProvider serviceProvider)
        {
            if (Item == null)
            {
                return null;
            }

            var itemDirectory = serviceProvider.GetRequiredService<IItemDirectory>();

            _itemAsset ??= await itemDirectory.FindByIdAsync(Item) as UnturnedItemAsset;
            _itemAsset ??= await itemDirectory.FindByNameAsync(Item, false) as UnturnedItemAsset;

            return _itemAsset;
        }
    }
}
