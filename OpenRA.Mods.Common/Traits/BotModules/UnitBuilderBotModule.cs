#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Controls AI unit production.")]
	public class UnitBuilderBotModuleInfo : ConditionalTraitInfo
	{
		// TODO: Investigate whether this might the (or at least one) reason why bots occasionally get into a state of doing nothing.
		// Reason: If this is less than SquadSize, the bot might get stuck between not producing more units due to this,
		// but also not creating squads since there aren't enough idle units.
		[Desc("Only produce units as long as there are less than this amount of units idling inside the base.")]
		public readonly int IdleBaseUnitsMaximum = 12;

		[Desc("What units can the AI not build if there are no supplies on the map.")]
		public readonly HashSet<string> SupplyCollectorTypes = new();

		[Desc("Production queues AI uses for producing units.")]
		public readonly string[] UnitQueues = null;

		[Desc("What units to the AI should build.", "What relative share of the total army must be this type of unit.")]
		public readonly Dictionary<string, int> UnitsToBuild = null;

		[Desc("What units should the AI have a maximum limit to train.")]
		public readonly Dictionary<string, int> UnitLimits = null;

		[Desc("When should the AI start train specific units.")]
		public readonly Dictionary<string, int> UnitDelays = null;

		[Desc("Limit of queue instances to build from at the same time.")]
		public readonly Dictionary<string, int> QueueLimits = null;

		[Desc("Only queue construction of a new unit when above this requirement.")]
		public readonly int ProductionMinCashRequirement = 501;

		public override object Create(ActorInitializer init) { return new UnitBuilderBotModule(init.Self, this); }
	}

	public class UnitBuilderBotModule : ConditionalTrait<UnitBuilderBotModuleInfo>, IBotTick, IBotNotifyIdleBaseUnits, IBotRequestUnitProduction, IGameSaveTraitData
	{
		/// <summary>
		/// Feedback time in ticks.
		/// </summary>
		public const int FeedbackTime = 30;

		readonly World world;
		readonly Player player;

		readonly List<string> queuedBuildRequests = new();

		IBotRequestPauseUnitProduction[] requestPause;
		int idleUnitCount;
		int currentQueueIndex = 0;
		PlayerResources playerResources;

		int ticks;

		public UnitBuilderBotModule(Actor self, UnitBuilderBotModuleInfo info)
			: base(info)
		{
			world = self.World;
			player = self.Owner;
		}

		protected override void Created(Actor self)
		{
			requestPause = self.Owner.PlayerActor.TraitsImplementing<IBotRequestPauseUnitProduction>().ToArray();
			playerResources = self.Owner.PlayerActor.Trait<PlayerResources>();
		}

		void IBotNotifyIdleBaseUnits.UpdatedIdleBaseUnits(List<UnitWposWrapper> idleUnits)
		{
			idleUnitCount = idleUnits.Count;
		}

		void IBotTick.BotTick(IBot bot)
		{
			// PERF: We shouldn't be queueing new units when we're low on cash
			if (playerResources.Cash < Info.ProductionMinCashRequirement || requestPause.Any(rp => rp.PauseUnitProduction))
				return;

			ticks++;

			if (ticks % FeedbackTime == 0)
			{
				var buildRequest = queuedBuildRequests.FirstOrDefault();
				if (buildRequest != null)
				{
					BuildUnit(bot, buildRequest);
					queuedBuildRequests.Remove(buildRequest);
				}

				for (var i = 0; i < Info.UnitQueues.Length; i++)
				{
					if (++currentQueueIndex >= Info.UnitQueues.Length)
						currentQueueIndex = 0;

					if (AIUtils.FindQueues(player, Info.UnitQueues[currentQueueIndex]).Any())
					{
						// PERF: We tick only one type of valid queue at a time
						// if AI gets enough cash, it can fill all of its queues with enough ticks
						BuildUnit(bot, Info.UnitQueues[currentQueueIndex], idleUnitCount < Info.IdleBaseUnitsMaximum);
						break;
					}
				}
			}
		}

		void IBotRequestUnitProduction.RequestUnitProduction(IBot bot, string requestedActor)
		{
			queuedBuildRequests.Add(requestedActor);
		}

		int IBotRequestUnitProduction.RequestedProductionCount(IBot bot, string requestedActor)
		{
			return queuedBuildRequests.Count(r => r == requestedActor);
		}

		public ProductionQueue FindQueue(Player player, string category)
		{
			var queues = AIUtils.FindQueues(player, category);

			var usedQueues = queues.Where(q => q.AllQueued().Any());
			if (Info.QueueLimits != null &&
				Info.QueueLimits.ContainsKey(category) &&
				usedQueues.Count() >= Info.QueueLimits[category])
				return null;

			var freeQueues = queues.Where(q => !q.AllQueued().Any());
			if (!freeQueues.Any())
				return null;

			return freeQueues.RandomOrDefault(world.LocalRandom);
		}

		void BuildUnit(IBot bot, string category, bool buildRandom)
		{
			// Pick a free queue
			var queue = FindQueue(player, category);
			if (queue == null)
				return;

			var unit = buildRandom ?
				ChooseRandomUnitToBuild(queue) :
				ChooseUnitToBuild(queue);

			if (unit == null)
				return;

			var name = unit.Name;

			if (Info.UnitsToBuild != null && !Info.UnitsToBuild.ContainsKey(name))
				return;

			if (Info.SupplyCollectorTypes.Contains(name) && !world.ActorsWithTrait<ISupplyDock>().Any(d => !d.Trait.IsEmpty()))
				return;

			if (Info.UnitDelays != null &&
				Info.UnitDelays.TryGetValue(name, out var delay) &&
				delay > world.WorldTick)
				return;

			if (Info.UnitLimits != null &&
				Info.UnitLimits.TryGetValue(name, out var limit) &&
				world.Actors.Count(a => a.Owner == player && a.Info.Name == name) >= limit)
				return;

			bot.QueueOrder(Order.StartProductionAI(queue.Actor, name, 1));
		}

		// In cases where we want to build a specific unit but don't know the queue name (because there's more than one possibility)
		void BuildUnit(IBot bot, string name)
		{
			var actorInfo = world.Map.Rules.Actors[name];
			if (actorInfo == null)
				return;

			var buildableInfo = actorInfo.TraitInfoOrDefault<BuildableInfo>();
			if (buildableInfo == null)
				return;

			ProductionQueue queue = null;
			foreach (var pq in buildableInfo.Queue)
			{
				queue = FindQueue(player, pq);
				if (queue != null)
					break;
			}

			if (queue != null)
			{
				bot.QueueOrder(Order.StartProductionAI(queue.Actor, name, 1));
				AIUtils.BotDebug("{0} decided to build {1} (external request)", queue.Actor.Owner, name);
			}
		}

		ActorInfo ChooseRandomUnitToBuild(ProductionQueue queue)
		{
			var buildableThings = queue.BuildableItems();
			if (!buildableThings.Any())
				return null;

			var unit = buildableThings.Random(world.LocalRandom);
			return unit;
		}

		ActorInfo ChooseUnitToBuild(ProductionQueue queue)
		{
			var buildableThings = queue.BuildableItems();
			if (!buildableThings.Any())
				return null;

			var myUnits = player.World
				.ActorsHavingTrait<IPositionable>()
				.Where(a => a.Owner == player)
				.Select(a => a.Info.Name).ToList();

			foreach (var unit in Info.UnitsToBuild.Shuffle(world.LocalRandom))
				if (buildableThings.Any(b => b.Name == unit.Key))
					if (myUnits.Count(a => a == unit.Key) * 100 < unit.Value * myUnits.Count)
						return world.Map.Rules.Actors[unit.Key];

			return null;
		}

		List<MiniYamlNode> IGameSaveTraitData.IssueTraitData(Actor self)
		{
			if (IsTraitDisabled)
				return null;

			return new List<MiniYamlNode>()
			{
				new MiniYamlNode("QueuedBuildRequests", FieldSaver.FormatValue(queuedBuildRequests.ToArray())),
				new MiniYamlNode("IdleUnitCount", FieldSaver.FormatValue(idleUnitCount))
			};
		}

		void IGameSaveTraitData.ResolveTraitData(Actor self, List<MiniYamlNode> data)
		{
			if (self.World.IsReplay)
				return;

			var queuedBuildRequestsNode = data.FirstOrDefault(n => n.Key == "QueuedBuildRequests");
			if (queuedBuildRequestsNode != null)
			{
				queuedBuildRequests.Clear();
				queuedBuildRequests.AddRange(FieldLoader.GetValue<string[]>("QueuedBuildRequests", queuedBuildRequestsNode.Value.Value));
			}

			var idleUnitCountNode = data.FirstOrDefault(n => n.Key == "IdleUnitCount");
			if (idleUnitCountNode != null)
				idleUnitCount = FieldLoader.GetValue<int>("IdleUnitCount", idleUnitCountNode.Value.Value);
		}
	}
}
