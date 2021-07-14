using OpenMod.Extensions.Games.Abstractions.Items;
using OpenMod.Unturned.Items;
using SDG.Unturned;

namespace ConsumableRewards.Items
{
    public class AdminItemState : IItemState
    {
        public AdminItemState(UnturnedItemAsset itemAsset)
        {
            ItemQuality = itemAsset.MaxQuality ?? 100;
            ItemDurability = itemAsset.MaxDurability ?? 100;
            ItemAmount = itemAsset.MaxAmount ?? 1;
            StateData = itemAsset.ItemAsset.getState(EItemOrigin.ADMIN);
        }

        public double ItemQuality { get; }

        public double ItemDurability { get; }

        public double ItemAmount { get; }

        public byte[]? StateData { get; }
    }
}
