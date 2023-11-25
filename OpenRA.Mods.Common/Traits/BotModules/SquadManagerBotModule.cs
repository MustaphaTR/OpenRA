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

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits.BotModules.Squads;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Manages AI squads.")]
	public class SquadManagerBotModuleInfo : ConditionalTraitInfo
	{
		[ActorReference]
		[Desc("Actor types that are valid for naval squads.")]
		public readonly HashSet<string> NavalUnitsTypes = new();

		[ActorReference]
		[Desc("Actor types that are excluded from ground attacks.")]
		public readonly HashSet<string> AirUnitsTypes = new();

		[ActorReference]
		[Desc("Actor types that should generally be excluded from attack squads.")]
		public readonly HashSet<string> ExcludeFromSquadsTypes = new();

		[ActorReference]
		[Desc("Actor types that are randomly sent around the base after their production.")]
		public readonly HashSet<string> DozerTypes = new();

		[ActorReference]
		[Desc("Actor types that are considered construction yards (base builders).")]
		public readonly HashSet<string> ConstructionYardTypes = new();

		[ActorReference]
		[Desc("Enemy building types around which to scan for targets for naval squads.")]
		public readonly HashSet<string> NavalProductionTypes = new();

		[ActorReference]
		[Desc("Own actor types that are prioritized when defending.")]
		public readonly HashSet<string> ProtectionTypes = new();

		[ActorReference]
		[Desc("Units that form a guerrilla squad.")]
		public readonly HashSet<string> GuerrillaTypes = new();

		[Desc("Target types are used for identifying aircraft.")]
		public readonly BitSet<TargetableType> AircraftTargetType = new("Air", "AirborneActor");

		[Desc("Minimum number of units AI must have before attacking.")]
		public readonly int SquadSize = 8;

		[Desc("Random number of up to this many units is added to squad size when creating an attack squad.")]
		public readonly int SquadSizeRandomBonus = 30;

		[Desc("Possibility of units in GuerrillaTypes to join Guerrilla.")]
		public readonly int JoinGuerrilla = 50;

		[Desc("Max number of units AI has in guerrilla squad")]
		public readonly int MaxGuerrillaSize = 10;

		[Desc("Delay (in ticks) between updating squads.")]
		public readonly int AttackForceInterval = 75;

		[Desc("Minimum delay (in ticks) between creating squads.")]
		public readonly int MinimumAttackForceDelay = 0;

		[Desc("Radius in cells around the base that should be scanned for units to be protected.")]
		public readonly int ProtectUnitScanRadius = 15;

		[Desc("Minimum radius in cells around base center to send dozer after building it.")]
		public readonly int MinDozerSendingRadius = 4;

		[Desc("Maximum radius in cells around base center to send dozer after building it.")]
		public readonly int MaxDozerSendingRadius = 16;

		[Desc("Maximum distance in cells from center of the base when checking for MCV deployment location.",
			"Only applies if RestrictMCVDeploymentFallbackToBase is enabled and there's at least one construction yard.")]
		public readonly int MaxBaseRadius = 20;

		[Desc("Radius in cells that squads should scan for enemies around their position while idle.")]
		public readonly int IdleScanRadius = 10;

		[Desc("Radius in cells that squads should scan for danger around their position to make flee decisions.")]
		public readonly int DangerScanRadius = 10;

		[Desc("Radius in cells that attack squads should scan for enemies around their position when trying to attack.")]
		public readonly int AttackScanRadius = 12;

		[Desc("Radius in cells that protecting squads should scan for enemies around their position.")]
		public readonly int ProtectionScanRadius = 8;

		[Desc("Enemy target types to never target.")]
		public readonly BitSet<TargetableType> IgnoredEnemyTargetTypes = default;

		[Desc("Locomotor used by pathfinding leader for squads")]
		public readonly HashSet<string> SuggestedGroundLeaderLocomotor = new();

		[Desc("Locomotor used by pathfinding leader for squads")]
		public readonly HashSet<string> SuggestedNavyLeaderLocomotor = new();

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			base.RulesetLoaded(rules, ai);

			if (DangerScanRadius <= 0)
				throw new YamlException("DangerScanRadius must be greater than zero.");
		}

		public override object Create(ActorInitializer init) { return new SquadManagerBotModule(init.Self, this); }
	}

	public class SquadManagerBotModule : ConditionalTrait<SquadManagerBotModuleInfo>, IBotEnabled, IBotTick, IBotRespondToAttack, IBotPositionsUpdated, IGameSaveTraitData
	{
		public CPos GetRandomBaseCenter()
		{
			var randomConstructionYard = World.Actors.Where(a => a.Owner == Player &&
				Info.ConstructionYardTypes.Contains(a.Info.Name))
				.RandomOrDefault(World.LocalRandom);

			return randomConstructionYard?.Location ?? initialBaseCenter;
		}

		public readonly World World;
		public readonly Player Player;
		public readonly int RepeatedAltertTicks = 15;

		public readonly Predicate<Actor> UnitCannotBeOrdered;
		readonly List<UnitWposWrapper> unitsHangingAroundTheBase = new();

		// Units that the bot already knows about. Any unit not on this list needs to be given a role.
		readonly List<Actor> activeUnits = new();

		public List<Squad> Squads = new();

		IBot bot;
		IBotPositionsUpdated[] notifyPositionsUpdated;
		IBotNotifyIdleBaseUnits[] notifyIdleBaseUnits;

		CPos initialBaseCenter;
		Actor airStrikeTarget;

		int assignRolesTicks;

		// int attackForceTicks;
		int protectionForceTicks;
		int guerrillaForceTicks;
		int airForceTicks;
		int navyForceTicks;
		int groundForceTicks;

		int minAttackForceDelayTicks;

		int alertedTicks;

		public SquadManagerBotModule(Actor self, SquadManagerBotModuleInfo info)
			: base(info)
		{
			World = self.World;
			Player = self.Owner;
			alertedTicks = 0;

			UnitCannotBeOrdered = a => a == null || a.Owner != Player || a.IsDead || !a.IsInWorld || a.CurrentActivity is Enter;
		}

		// Use for proactive targeting.
		public bool IsPreferredEnemyUnit(Actor a)
		{
			if (a == null || a.IsDead || Player.RelationshipWith(a.Owner) != PlayerRelationship.Enemy || a.Info.HasTraitInfo<HuskInfo>())
				return false;

			var targetTypes = a.GetEnabledTargetTypes();
			return !targetTypes.IsEmpty && !targetTypes.Overlaps(Info.IgnoredEnemyTargetTypes);
		}

		public bool IsNotHiddenUnit(Actor a)
		{
			var hasModifier = false;
			var visModifiers = a.TraitsImplementing<IVisibilityModifier>();
			foreach (var v in visModifiers)
			{
				if (v.IsVisible(a, Player))
					return true;

				hasModifier = true;
			}

			return !hasModifier;
		}

		public bool IsNotUnseenUnit(Actor a)
		{
			var isUnseen = false;
			var visModifiers = a.TraitsImplementing<IDefaultVisibility>();
			foreach (var v in visModifiers)
			{
				if (v.IsVisible(a, Player))
					return true;

				isUnseen = true;
			}

			return !isUnseen;
		}

		protected override void Created(Actor self)
		{
			notifyPositionsUpdated = self.Owner.PlayerActor.TraitsImplementing<IBotPositionsUpdated>().ToArray();
			notifyIdleBaseUnits = self.Owner.PlayerActor.TraitsImplementing<IBotNotifyIdleBaseUnits>().ToArray();
		}

		protected override void TraitEnabled(Actor self)
		{
			var attackForceTicks = World.LocalRandom.Next(0, Info.AttackForceInterval);

			protectionForceTicks = attackForceTicks;
			guerrillaForceTicks = attackForceTicks + 1;
			airForceTicks = attackForceTicks + 2;
			navyForceTicks = attackForceTicks + 3;
			groundForceTicks = attackForceTicks + 4;
			assignRolesTicks = attackForceTicks + 5;

			minAttackForceDelayTicks = World.LocalRandom.Next(0, Info.MinimumAttackForceDelay);
		}

		void IBotEnabled.BotEnabled(IBot bot)
		{
			this.bot = bot;
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (!IsPreferredEnemyUnit(airStrikeTarget) || !IsNotHiddenUnit(airStrikeTarget))
				airStrikeTarget = null;

			AssignRolesToIdleUnits(bot);
			if (alertedTicks > 0)
				alertedTicks--;
		}

		internal Actor FindClosestEnemy(Actor sourceActor, WDist radius)
		{
			return World.FindActorsInCircle(sourceActor.CenterPosition, radius).Where(a => IsPreferredEnemyUnit(a) && IsNotHiddenUnit(a) && IsNotUnseenUnit(a)).ClosestToWithPathFrom(sourceActor);
		}

		internal Actor FindClosestEnemy(Actor sourceActor)
		{
			var findVisible = false;
			var bestDist = long.MaxValue;
			Actor bestTarget = null;
			foreach (var a in World.Actors.Where(a => IsPreferredEnemyUnit(a)))
			{
				var dist = (a.CenterPosition - sourceActor.CenterPosition).LengthSquared;

				if (findVisible)
				{
					if (IsNotHiddenUnit(a) && dist < bestDist)
					{
						bestTarget = a;
						bestDist = dist;
					}
				}
				else
				{
					if (IsNotHiddenUnit(a))
					{
						findVisible = true;
						bestTarget = a;
						bestDist = dist;
					}
					else if (dist < bestDist)
					{
						bestTarget = a;
						bestDist = dist;
					}
				}
			}

			return bestTarget;
		}

		void CleanSquads()
		{
			Squads.RemoveAll(s => !s.IsValid);
		}

		// HACK: Use of this function requires that there is one squad of this type.
		Squad GetSquadOfType(SquadType type)
		{
			return Squads.Find(s => s.Type == type);
		}

		IEnumerable<Squad> GetSquadsOfType(SquadType type)
		{
			return Squads.Where(s => s.Type == type);
		}

		Squad RegisterNewSquad(IBot bot, SquadType type, Actor target = null)
		{
			var ret = new Squad(bot, this, type, target);
			Squads.Add(ret);
			return ret;
		}

		public void DismissSquad(Squad squad)
		{
			unitsHangingAroundTheBase.AddRange(squad.Units);

			squad.Units.Clear();
		}

		void AssignRolesToIdleUnits(IBot bot)
		{
			CleanSquads();

			// Ticks squads
			if (--protectionForceTicks <= 0)
			{
				protectionForceTicks = Info.AttackForceInterval;
				foreach (var s in GetSquadsOfType(SquadType.Protection))
				{
					s.Units.RemoveAll(u => UnitCannotBeOrdered(u.Actor));
					s.Update();
				}
			}

			if (--guerrillaForceTicks <= 0)
			{
				guerrillaForceTicks = Info.AttackForceInterval;
				foreach (var s in GetSquadsOfType(SquadType.Assault))
				{
					s.Units.RemoveAll(u => UnitCannotBeOrdered(u.Actor));
					s.Update();
				}
			}

			if (--airForceTicks <= 0)
			{
				airForceTicks = Info.AttackForceInterval;
				foreach (var s in GetSquadsOfType(SquadType.Air))
				{
					s.Units.RemoveAll(u => UnitCannotBeOrdered(u.Actor));
					s.Update();
				}
			}

			if (--navyForceTicks <= 0)
			{
				navyForceTicks = Info.AttackForceInterval;
				foreach (var s in GetSquadsOfType(SquadType.Naval))
				{
					s.Units.RemoveAll(u => UnitCannotBeOrdered(u.Actor));
					s.Update();
				}
			}

			if (--groundForceTicks <= 0)
			{
				groundForceTicks = Info.AttackForceInterval;
				foreach (var s in GetSquadsOfType(SquadType.Rush))
				{
					s.Units.RemoveAll(u => UnitCannotBeOrdered(u.Actor));
					s.Update();
				}
			}

			if (--assignRolesTicks <= 0)
			{
				assignRolesTicks = Info.AttackForceInterval;
				unitsHangingAroundTheBase.RemoveAll(u => UnitCannotBeOrdered(u.Actor));
				activeUnits.RemoveAll(UnitCannotBeOrdered);
				FindNewUnits(bot);
			}

			if (--minAttackForceDelayTicks <= 0)
			{
				minAttackForceDelayTicks = Info.MinimumAttackForceDelay;
				unitsHangingAroundTheBase.RemoveAll(u => UnitCannotBeOrdered(u.Actor));
				CreateAttackForce(bot);
			}
		}

		void FindNewUnits(IBot bot)
		{
			var newUnits = World.ActorsHavingTrait<IPositionable>()
				.Where(a => a.Owner == Player &&
					!Info.ExcludeFromSquadsTypes.Contains(a.Info.Name) &&
					!activeUnits.Contains(a));

			var guerrillaForce = GetSquadOfType(SquadType.Assault);
			var guerrillaUpdate = guerrillaForce == null || (guerrillaForce.Units.Count <= Info.MaxGuerrillaSize && (World.LocalRandom.Next(100) >= Info.JoinGuerrilla));

			foreach (var a in newUnits)
			{
				var baseCenter = GetRandomBaseCenter();
				var mobile = a.TraitOrDefault<Mobile>();
				if (Info.DozerTypes.Contains(a.Info.Name) && mobile != null)
				{
					var dozerTargetPos = World.Map.FindTilesInAnnulus(baseCenter, Info.MinDozerSendingRadius, Info.MaxDozerSendingRadius)
						.Where(c => mobile.CanEnterCell(c)).Random(World.LocalRandom);

					AIUtils.BotDebug($"AI: {a.Owner} has chosen {dozerTargetPos} to move its Dozer ({a})");
					bot.QueueOrder(new Order("Move", a, Target.FromCell(World, dozerTargetPos), true));
				}
				else if (Info.GuerrillaTypes.Contains(a.Info.Name) && guerrillaUpdate)
				{
					guerrillaForce ??= RegisterNewSquad(bot, SquadType.Assault);

					guerrillaForce.Units.Add(new UnitWposWrapper(a));
				}
				else if (Info.AirUnitsTypes.Contains(a.Info.Name))
				{
					var air = GetSquadOfType(SquadType.Air);
					air ??= RegisterNewSquad(bot, SquadType.Air);

					air.Units.Add(new UnitWposWrapper(a));
				}
				else if (Info.NavalUnitsTypes.Contains(a.Info.Name))
				{
					var ships = GetSquadOfType(SquadType.Naval);
					ships ??= RegisterNewSquad(bot, SquadType.Naval);

					ships.Units.Add(new UnitWposWrapper(a));
				}
				else
					unitsHangingAroundTheBase.Add(new UnitWposWrapper(a));

				activeUnits.Add(a);
			}

			// Notifying here rather than inside the loop, should be fine and saves a bunch of notification calls
			foreach (var n in notifyIdleBaseUnits)
				n.UpdatedIdleBaseUnits(unitsHangingAroundTheBase);

			var protectSq = GetSquadOfType(SquadType.Protection);
			if (protectSq != null)
			{
				protectSq.Units = unitsHangingAroundTheBase;
				return;
			}

			protectSq = RegisterNewSquad(bot, SquadType.Protection, null);
			protectSq.Units = unitsHangingAroundTheBase;
		}

		void CreateAttackForce(IBot bot)
		{
			// Create an attack force when we have enough units around our base.
			// (don't bother leaving any behind for defense)
			var randomizedSquadSize = Info.SquadSize + World.LocalRandom.Next(Info.SquadSizeRandomBonus);

			if (unitsHangingAroundTheBase.Count >= randomizedSquadSize)
			{
				var attackForce = RegisterNewSquad(bot, SquadType.Rush);

				attackForce.Units.AddRange(unitsHangingAroundTheBase);

				unitsHangingAroundTheBase.Clear();
				foreach (var n in notifyIdleBaseUnits)
					n.UpdatedIdleBaseUnits(unitsHangingAroundTheBase);
			}
		}

		void ProtectOwn(Actor attacker)
		{
			foreach (var s in Squads.Where(s => s.IsValid))
			{
				if (s.Type != SquadType.Protection && (s.CenterPosition - attacker.CenterPosition).LengthSquared > WDist.FromCells(Info.ProtectUnitScanRadius).LengthSquared)
					continue;

				s.TargetActor = attacker;
			}
		}

		void IBotPositionsUpdated.UpdatedBaseCenter(CPos newLocation)
		{
			initialBaseCenter = newLocation;
		}

		void IBotPositionsUpdated.UpdatedDefenseCenter(CPos newLocation) { }

		void IBotRespondToAttack.RespondToAttack(IBot bot, Actor self, AttackInfo e)
		{
			if (alertedTicks > 0 || !IsPreferredEnemyUnit(e.Attacker))
				return;

			alertedTicks = RepeatedAltertTicks;

			if (Info.ProtectionTypes.Contains(self.Info.Name))
			{
				foreach (var n in notifyPositionsUpdated)
					n.UpdatedDefenseCenter(e.Attacker.Location);

				ProtectOwn(e.Attacker);
			}
		}

		public void SetAirStrikeTarget(Actor target)
		{
			airStrikeTarget = target;
		}

		public Actor PopAirStrikeTarget()
		{
			var target = airStrikeTarget;
			airStrikeTarget = null;
			return target;
		}

		List<MiniYamlNode> IGameSaveTraitData.IssueTraitData(Actor self)
		{
			if (IsTraitDisabled)
				return null;

			return new List<MiniYamlNode>()
			{
				new("Squads", "", Squads.ConvertAll(s => new MiniYamlNode("Squad", s.Serialize()))),
				new("InitialBaseCenter", FieldSaver.FormatValue(initialBaseCenter)),
				new("UnitsHangingAroundTheBase", FieldSaver.FormatValue(unitsHangingAroundTheBase
					.Where(u => !UnitCannotBeOrdered(u.Actor))
					.Select(u => u.Actor.ActorID)
					.ToArray())),
				new("ActiveUnits", FieldSaver.FormatValue(activeUnits
					.Where(a => !UnitCannotBeOrdered(a))
					.Select(a => a.ActorID)
					.ToArray())),
				new("AssignRolesTicks", FieldSaver.FormatValue(assignRolesTicks)),
				new("protectionForceTicks", FieldSaver.FormatValue(protectionForceTicks)),
				new("guerrillaForceTicks", FieldSaver.FormatValue(guerrillaForceTicks)),
				new("airForceTicks", FieldSaver.FormatValue(airForceTicks)),
				new("navyForceTicks", FieldSaver.FormatValue(navyForceTicks)),
				new("groundForceTicks", FieldSaver.FormatValue(groundForceTicks)),
				new("MinAttackForceDelayTicks", FieldSaver.FormatValue(minAttackForceDelayTicks)),
			};
		}

		void IGameSaveTraitData.ResolveTraitData(Actor self, MiniYaml data)
		{
			if (self.World.IsReplay)
				return;

			var nodes = data.ToDictionary();

			if (nodes.TryGetValue("InitialBaseCenter", out var initialBaseCenterNode))
				initialBaseCenter = FieldLoader.GetValue<CPos>("InitialBaseCenter", initialBaseCenterNode.Value);

			if (nodes.TryGetValue("UnitsHangingAroundTheBase", out var unitsHangingAroundTheBaseNode))
			{
				unitsHangingAroundTheBase.Clear();

				foreach (var a in FieldLoader.GetValue<uint[]>("UnitsHangingAroundTheBase", unitsHangingAroundTheBaseNode.Value)
					.Select(a => self.World.GetActorById(a)).Where(a => a != null))
				{
					unitsHangingAroundTheBase.Add(new UnitWposWrapper(a));
				}
			}

			if (nodes.TryGetValue("ActiveUnits", out var activeUnitsNode))
			{
				activeUnits.Clear();
				activeUnits.AddRange(FieldLoader.GetValue<uint[]>("ActiveUnits", activeUnitsNode.Value)
					.Select(a => self.World.GetActorById(a)).Where(a => a != null));
			}

			if (nodes.TryGetValue("AssignRolesTicks", out var assignRolesTicksNode))
				assignRolesTicks = FieldLoader.GetValue<int>("AssignRolesTicks", assignRolesTicksNode.Value);

			if (nodes.TryGetValue("protectionForceTicks", out var protectionForceTicksNode))
				protectionForceTicks = FieldLoader.GetValue<int>("protectionForceTicks", protectionForceTicksNode.Value);

			if (nodes.TryGetValue("guerrillaForceTicks", out var guerrillaForceTicksNode))
				guerrillaForceTicks = FieldLoader.GetValue<int>("guerrillaForceTicks", guerrillaForceTicksNode.Value);

			if (nodes.TryGetValue("airForceTicks", out var airForceTicksNode))
				airForceTicks = FieldLoader.GetValue<int>("airForceTicks", airForceTicksNode.Value);

			if (nodes.TryGetValue("navyForceTicks", out var navyForceTicksNode))
				navyForceTicks = FieldLoader.GetValue<int>("navyForceTicks", navyForceTicksNode.Value);

			if (nodes.TryGetValue("groundForceTicks", out var groundForceTicksNode))
				groundForceTicks = FieldLoader.GetValue<int>("groundForceTicks", groundForceTicksNode.Value);

			if (nodes.TryGetValue("MinAttackForceDelayTicks", out var minAttackForceDelayTicksNode))
				minAttackForceDelayTicks = FieldLoader.GetValue<int>("MinAttackForceDelayTicks", minAttackForceDelayTicksNode.Value);

			if (nodes.TryGetValue("Squads", out var squadsNode))
			{
				Squads.Clear();
				foreach (var n in squadsNode.Nodes)
					Squads.Add(Squad.Deserialize(bot, this, n.Value));
			}
		}
	}
}
