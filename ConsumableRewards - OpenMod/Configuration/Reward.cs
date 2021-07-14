using System;

namespace ConsumableRewards.Configuration
{
    [Serializable]
    public class Reward
    {
        public string? GiveItem { get; set; }

        public uint? GiveExperience { get; set; }

        public int? ChangeReputation { get; set; }

        public string? GiveRole { get; set; }

        public ushort? ShowEffect { get; set; }

        public bool EffectVisibleToOthers { get; set; }

        public string? SendMessage { get; set; }

        public string? RunCommand { get; set; }



        public double? Chance { get; set; }
    }
}
