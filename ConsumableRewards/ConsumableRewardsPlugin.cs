using ConsumableRewards.Configuration;
using ConsumableRewards.Items;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenMod.API.Commands;
using OpenMod.API.Permissions;
using OpenMod.API.Plugins;
using OpenMod.Core.Console;
using OpenMod.Core.Helpers;
using OpenMod.Extensions.Games.Abstractions.Items;
using OpenMod.UnityEngine.Extensions;
using OpenMod.Unturned.Items;
using OpenMod.Unturned.Plugins;
using OpenMod.Unturned.Users;
using SDG.Unturned;
using SilK.Unturned.Extras.Configuration;
using SilK.Unturned.Extras.Players;
using SmartFormat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

[assembly: PluginMetadata("ConsumableRewards", DisplayName = "Consumable Rewards")]

namespace ConsumableRewards
{
    public class ConsumableRewardsPlugin : OpenModUnturnedPlugin
    {
        private IConfigurationParser<ConsumableRewardsConfiguration> _configuration = null!;
        private readonly ILogger<ConsumableRewardsPlugin> _logger;
        private readonly IUnturnedUserDirectory _userDirectory;
        private readonly IPermissionChecker _permissionChecker;
        private readonly IPermissionRegistry _permissionRegistry;
        private readonly IItemDirectory _itemDirectory;
        private readonly IItemSpawner _itemSpawner;
        private readonly IPermissionRoleStore _permissionRoleStore;
        private readonly IConsoleActorAccessor _consoleActorAccessor;
        private readonly ICommandExecutor _commandExecutor;
        private readonly IServiceProvider _serviceProvider;

        private readonly Lazy<IConfigurationParser<ConsumableRewardsConfiguration>> _configurationFactory;

        private readonly Random _rng;

        public ConsumableRewardsPlugin(
            Lazy<IConfigurationParser<ConsumableRewardsConfiguration>> configurationFactory,
            ILogger<ConsumableRewardsPlugin> logger,
            IUnturnedUserDirectory userDirectory,
            IPermissionChecker permissionChecker,
            IPermissionRegistry permissionRegistry,
            IItemDirectory itemDirectory,
            IItemSpawner itemSpawner,
            IPermissionRoleStore permissionRoleStore,
            IConsoleActorAccessor consoleActorAccessor,
            ICommandExecutor commandExecutor,
            IServiceProvider serviceProvider) : base(serviceProvider)
        {
            _configurationFactory = configurationFactory;
            _logger = logger;
            _userDirectory = userDirectory;
            _permissionChecker = permissionChecker;
            _permissionRegistry = permissionRegistry;
            _itemDirectory = itemDirectory;
            _itemSpawner = itemSpawner;
            _permissionRoleStore = permissionRoleStore;
            _consoleActorAccessor = consoleActorAccessor;
            _commandExecutor = commandExecutor;
            _serviceProvider = serviceProvider;

            _rng = new Random();
        }

        protected override UniTask OnLoadAsync()
        {
            _configuration = _configurationFactory.Value;
            UseableConsumeable.onConsumePerformed += OnConsumePerformed;

            return UniTask.CompletedTask;
        }

        protected override UniTask OnUnloadAsync()
        {
            UseableConsumeable.onConsumePerformed -= OnConsumePerformed;

            return UniTask.CompletedTask;
        }

        public async Task<ICollection<Consumable>> GetApplicableConsumables(UnturnedUser user,
            UnturnedItemAsset consumedAsset)
        {
            var consumables = new List<Consumable>();

            foreach (var (consumable, index) in _configuration.Instance.Consumables.Select((x, y) => (x, y)))
            {
                var itemCriteriaApplies = !string.IsNullOrEmpty(consumable.Item);
                var patternCriteriaApplies = !string.IsNullOrEmpty(consumable.Pattern);

                if (!itemCriteriaApplies && !patternCriteriaApplies)
                {
                    _logger.LogWarning($"No criteria for configured consumable of index {index}.");
                    continue;
                }

                if (itemCriteriaApplies && patternCriteriaApplies)
                {
                    _logger.LogWarning(
                        $"Both item and pattern criteria apply for consumable with item {consumable.Item} (index {index}). Will use item criteria by default.");
                }

                var applies = false;

                // Match based on criteria
                if (itemCriteriaApplies)
                {
                    var configuredAsset = await consumable.GetItemAsset(_serviceProvider);

                    if (configuredAsset != null)
                    {
                        applies = configuredAsset.ItemAssetId.Equals(consumedAsset.ItemAssetId);

                        _logger.LogDebug(
                            $"Item Criteria Matches: {applies} (Consumed: {consumedAsset.ItemName} ({consumedAsset.ItemAssetId}), Configured: {configuredAsset.ItemName} ({configuredAsset.ItemAssetId}))");
                    }
                    else
                    {
                        _logger.LogWarning($"Could not find consumable item asset '{consumable.Item}'.");
                    }
                }
                else if (patternCriteriaApplies)
                {
                    var pattern = consumable.Pattern!;

                    applies = Regex.IsMatch(consumedAsset.ItemName, pattern);

                    _logger.LogDebug(
                        $"Pattern Criteria Matches: {applies} (Consumed: {consumedAsset.ItemName} ({consumedAsset.ItemAssetId}), Configured Pattern: \"{pattern}\")");
                }

                // Check if user has permission
                var permission = consumable.RequiresPermission;

                if (applies && permission != null)
                {
                    if (_permissionRegistry.FindPermission(this, permission) == null)
                    {
                        _permissionRegistry.RegisterPermission(this, permission,
                            "Allows for the configured consumable reward to be granted.");
                    }

                    applies = await _permissionChecker.CheckPermissionAsync(user, permission) ==
                              PermissionGrantResult.Grant;
                }

                // Check chance
                if (applies && consumable.Chance != null)
                {
                    var random = _rng.NextDouble() * 100;

                    applies = random < consumable.Chance;
                }

                if (!applies)
                {
                    continue;
                }

                consumables.Add(consumable);
            }

            return consumables;
        }

        private void OnConsumePerformed(Player instigatingPlayer, ItemConsumeableAsset consumableAsset)
        {
            UniTask.RunOnThreadPool(async () =>
            {

                var user = _userDirectory.GetUser(instigatingPlayer);
                var asset = new UnturnedItemAsset(consumableAsset);

                await OnConsumePerformedAsync(user, asset);
            }).Forget();
        }

        private async Task OnConsumePerformedAsync(UnturnedUser user, UnturnedItemAsset consumableItemAsset)
        {
            var consumables = await GetApplicableConsumables(user, consumableItemAsset);
            
            foreach (var consumable in consumables)
            {
                _logger.LogDebug(
                    $"Consumable configuration has been triggered (Item: {consumable.Item ?? "null"}, Pattern: {consumable.Pattern ?? "null"}).");

                var rewardsGiven = 0;

                _logger.LogDebug($"Enumerating over {consumable.Rewards.Length} rewards.");

                foreach (var reward in consumable.Rewards)
                {
                    // Check reward limit
                    if (consumable.RewardLimit != null && rewardsGiven >= consumable.RewardLimit.Value)
                    {
                        break;
                    }

                    // Check chance
                    if (reward.Chance != null)
                    {
                        var random = _rng.NextDouble() * 100;

                        var grantReward = random < reward.Chance;

                        _logger.LogDebug($"Checking reward chance: {reward.Chance}% (generated {random:0.00}) | Grant Reward: {grantReward}");

                        if (!grantReward)
                        {
                            continue;
                        }
                    }

                    _logger.LogDebug("Granting reward.");

                    // Give reward
                    rewardsGiven++;

                    if (reward.GiveItem != null)
                    {
                        var rewardItemAsset = await _itemDirectory.FindByIdAsync(reward.GiveItem)
                                              ?? await _itemDirectory.FindByNameAsync(reward.GiveItem);

                        if (rewardItemAsset is UnturnedItemAsset unturnedItemAsset)
                        {
                            await _itemSpawner.GiveItemAsync(user.Player.Inventory, unturnedItemAsset,
                                new AdminItemState(unturnedItemAsset));
                        }
                    }

                    if (reward.GiveExperience != null)
                    {
                        await UniTask.SwitchToMainThread();

                        user.Player.Player.skills.askAward(reward.GiveExperience.Value);
                    }

                    if (reward.ChangeReputation != null)
                    {
                        await UniTask.SwitchToMainThread();

                        user.Player.Player.skills.askRep(reward.ChangeReputation.Value);
                    }

                    if (reward.GiveRole != null)
                    {
                        await UniTask.SwitchToThreadPool();

                        await _permissionRoleStore.AddRoleToActorAsync(user, reward.GiveRole);
                    }

                    if (reward.ShowEffect != null)
                    {
                        await UniTask.SwitchToMainThread();

                        if (reward.EffectVisibleToOthers)
                        {
                            EffectManager.sendEffectReliable(reward.ShowEffect.Value, EffectManager.MEDIUM,
                                user.Player.Transform.Position.ToUnityVector());
                        }
                        else
                        {
                            EffectManager.sendEffectReliable(reward.ShowEffect.Value, user.GetTransportConnection(),
                                user.Player.Transform.Position.ToUnityVector());
                        }
                    }

                    if (reward.SendMessage != null)
                    {
                        await user.PrintMessageAsync(Smart.Format(reward.SendMessage, new {User = user}));
                    }

                    if (reward.RunCommand != null)
                    {
                        var consoleActor = _consoleActorAccessor.Actor;

                        var commandText = Smart.Format(reward.RunCommand, new {User = user});

                        var commandArguments = ArgumentsParser.ParseArguments(commandText);

                        if (commandArguments.Length > 0)
                        {
                            await UniTask.SwitchToThreadPool();

                            await _commandExecutor.ExecuteAsync(consoleActor, commandArguments, string.Empty);
                        }
                    }
                }
            }
        }
    }
}
