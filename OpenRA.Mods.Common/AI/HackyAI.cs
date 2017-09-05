#region Copyright & License Information
/*
 * Copyright 2007-2018 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using OpenRA.Activities;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Pathfinder;
using OpenRA.Mods.Common.Traits;
using OpenRA.Scripting;
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.AI
{
	public sealed class HackyAIInfo : IBotInfo, ITraitInfo
	{
		public class UnitCategories
		{
			public readonly HashSet<string> Mcv = new HashSet<string>();
			public readonly HashSet<string> NavalUnits = new HashSet<string>();
			public readonly HashSet<string> Seige = new HashSet<string>();
			public readonly HashSet<string> ExcludeFromSquads = new HashSet<string>();
		}

		public class BuildingCategories
		{
			public readonly HashSet<string> ConstructionYard = new HashSet<string>();
			public readonly HashSet<string> VehiclesFactory = new HashSet<string>();
			public readonly HashSet<string> Refinery = new HashSet<string>();
			public readonly HashSet<string> Power = new HashSet<string>();
			public readonly HashSet<string> Barracks = new HashSet<string>();
			public readonly HashSet<string> Production = new HashSet<string>();
			public readonly HashSet<string> NavalProduction = new HashSet<string>();
			public readonly HashSet<string> Silo = new HashSet<string>();
			public readonly HashSet<string> StaticAntiAir = new HashSet<string>();
			public readonly HashSet<string> Fragile = new HashSet<string>();
			public readonly HashSet<string> Defense = new HashSet<string>();
		}

		[FieldLoader.Require]
		[Desc("Internal id for this bot.")]
		public readonly string Type = null;

		[Desc("Human-readable name this bot uses.")]
		public readonly string Name = "Unnamed Bot";

		[Desc("The Script AI will read and execute")]
		public readonly string LuaScript;

		[Desc("Build units according to Lua script? (false = old hacky AI behavior).")]
		public readonly bool EnableLuaUnitProduction = false;

		[Desc("EXPERIMENTAL")]
		public readonly bool PipeExternalQuery = false;

		[Desc("Minimum number of units AI must have before attacking.")]
		public readonly int SquadSize = 8;

		[Desc("Random number of up to this many units is added to squad size when creating an attack squad.")]
		public readonly int SquadSizeRandomBonus = 30;

		[Desc("Production queues AI uses for buildings.")]
		public readonly HashSet<string> BuildingQueues = new HashSet<string> { "Building" };

		[Desc("Production queues AI uses for defenses.")]
		public readonly HashSet<string> DefenseQueues = new HashSet<string> { "Defense" };

		[Desc("Delay (in ticks) between giving out orders to units.")]
		public readonly int AssignRolesInterval = 20;

		[Desc("Delay (in ticks) between attempting rush attacks.")]
		public readonly int RushInterval = 600;

		[Desc("Delay (in ticks) between updating squads.")]
		public readonly int AttackForceInterval = 30;

		[Desc("Minimum delay (in ticks) between creating squads.")]
		public readonly int MinimumAttackForceDelay = 0;

		[Desc("Minimum portion of pending orders to issue each tick (e.g. 5 issues at least 1/5th of all pending orders). Excess orders remain queued for subsequent ticks.")]
		public readonly int MinOrderQuotientPerTick = 5;

		[Desc("Minimum excess power the AI should try to maintain.")]
		public readonly int MinimumExcessPower = 0;

		[Desc("Increase maintained excess power by this amount for every ExcessPowerIncreaseThreshold of base buildings.")]
		public readonly int ExcessPowerIncrement = 0;

		[Desc("Increase maintained excess power by ExcessPowerIncrement for every N base buildings.")]
		public readonly int ExcessPowerIncreaseThreshold = 1;

		[Desc("The targeted excess power the AI tries to maintain cannot rise above this.")]
		public readonly int MaximumExcessPower = 0;

		[Desc("Additional delay (in ticks) between structure production checks when there is no active production.",
			"StructureProductionRandomBonusDelay is added to this.")]
		public readonly int StructureProductionInactiveDelay = 125;

		[Desc("Additional delay (in ticks) added between structure production checks when actively building things.",
			"Note: The total delay is gamespeed OrderLatency x 4 + this + StructureProductionRandomBonusDelay.")]
		public readonly int StructureProductionActiveDelay = 0;

		[Desc("A random delay (in ticks) of up to this is added to active/inactive production delays.")]
		public readonly int StructureProductionRandomBonusDelay = 10;

		[Desc("Delay (in ticks) until retrying to build structure after the last 3 consecutive attempts failed.")]
		public readonly int StructureProductionResumeDelay = 1500;

		[Desc("After how many failed attempts to place a structure should AI give up and wait",
			"for StructureProductionResumeDelay before retrying.")]
		public readonly int MaximumFailedPlacementAttempts = 3;

		[Desc("How many randomly chosen cells with resources to check when deciding refinery placement.")]
		public readonly int MaxResourceCellsToCheck = 3;

		[Desc("Delay (in ticks) until rechecking for new BaseProviders.")]
		public readonly int CheckForNewBasesDelay = 1500;

		[Desc("Minimum range at which to build defensive structures near a combat hotspot.")]
		public readonly int MinimumDefenseRadius = 5;

		[Desc("Maximum range at which to build defensive structures near a combat hotspot.")]
		public readonly int MaximumDefenseRadius = 20;

		[Desc("Try to build another production building if there is too much cash.")]
		public readonly int NewProductionCashThreshold = 5000;

		[Desc("Only produce units as long as there are less than this amount of units idling inside the base.")]
		public readonly int IdleBaseUnitsMaximum = 12;

		[Desc("Radius in cells around enemy BaseBuilder (Construction Yard) where AI scans for targets to rush.")]
		public readonly int RushAttackScanRadius = 15;

		[Desc("Radius in cells around the base that should be scanned for units to be protected.")]
		public readonly int ProtectUnitScanRadius = 15;

		[Desc("Radius in cells around a factory scanned for rally points by the AI.")]
		public readonly int RallyPointScanRadius = 8;

		[Desc("Minimum distance in cells from center of the base when checking for building placement.")]
		public readonly int MinBaseRadius = 2;

		[Desc("Same as MinBaseRadius but for fragile structures to push them further away.")]
		public readonly int MinFragilePlacementRadius = 8;

		[Desc("Radius in cells around the center of the base to expand.")]
		public readonly int MaxBaseRadius = 20;

		[Desc("Radius in cells that squads should scan for enemies around their position while idle.")]
		public readonly int IdleScanRadius = 10;

		[Desc("Radius in cells that squads should scan for danger around their position to make flee decisions.")]
		public readonly int DangerScanRadius = 10;

		[Desc("Radius in cells that attack squads should scan for enemies around their position when trying to attack.")]
		public readonly int AttackScanRadius = 12;

		[Desc("Radius in cells that protecting squads should scan for enemies around their position.")]
		public readonly int ProtectionScanRadius = 8;

		[Desc("Should deployment of additional MCVs be restricted to MaxBaseRadius if explicit deploy locations are missing or occupied?")]
		public readonly bool RestrictMCVDeploymentFallbackToBase = true;

		[Desc("Radius in cells around each building with ProvideBuildableArea",
			"to check for a 3x3 area of water where naval structures can be built.",
			"Should match maximum adjacency of naval structures.")]
		public readonly int CheckForWaterRadius = 8;

		[Desc("Terrain types which are considered water for base building purposes.")]
		public readonly HashSet<string> WaterTerrainTypes = new HashSet<string> { "Water" };

		[Desc("Avoid enemy actors nearby when searching for a new resource patch. Should be somewhere near the max weapon range.")]
		public readonly WDist HarvesterEnemyAvoidanceRadius = WDist.FromCells(8);

		[Desc("Production queues AI uses for producing units.")]
		public readonly HashSet<string> UnitQueues = new HashSet<string> { "Vehicle", "Infantry", "Plane", "Ship", "Aircraft" };

		[Desc("Should the AI repair its buildings if damaged?")]
		public readonly bool ShouldRepairBuildings = true;

		[Desc("What units to the AI should build.", "What % of the total army must be this type of unit.")]
		public readonly Dictionary<string, float> UnitsToBuild = null;

		[Desc("What units should the AI have a maximum limit to train.")]
		public readonly Dictionary<string, int> UnitLimits = null;

		[Desc("What buildings to the AI should build.", "What % of the total base must be this type of building.")]
		public readonly Dictionary<string, float> BuildingFractions = null;

		[Desc("Tells the AI what unit types fall under the same common name. Supported entries are Mcv and ExcludeFromSquads.")]
		[FieldLoader.LoadUsing("LoadUnitCategories", true)]
		public readonly UnitCategories UnitsCommonNames;

		[Desc("Tells the AI what building types fall under the same common name.",
			"Possible keys are ConstructionYard, Power, Refinery, Silo, Barracks, Production, VehiclesFactory, NavalProduction.")]
		[FieldLoader.LoadUsing("LoadBuildingCategories", true)]
		public readonly BuildingCategories BuildingCommonNames;

		public readonly Dictionary<string, string> CoreDefinitions = null;

		[Desc("What buildings should the AI have a maximum limit to build.")]
		public readonly Dictionary<string, int> BuildingLimits = null;

		// TODO Update OpenRA.Utility/Command.cs#L300 to first handle lists and also read nested ones
		[Desc("Tells the AI how to use its support powers.")]
		[FieldLoader.LoadUsing("LoadDecisions")]
		public readonly List<SupportPowerDecision> PowerDecisions = new List<SupportPowerDecision>();

		[Desc("Actor types that can capture other actors (via `Captures` or `ExternalCaptures`).",
			"Leave this empty to disable capturing.")]
		public HashSet<string> CapturingActorTypes = new HashSet<string>();

		[Desc("Actor types that can be targeted for capturing.",
			"Leave this empty to include all actors.")]
		public HashSet<string> CapturableActorTypes = new HashSet<string>();

		[Desc("Minimum delay (in ticks) between trying to capture with CapturingActorTypes.")]
		public readonly int MinimumCaptureDelay = 375;

		[Desc("Maximum number of options to consider for capturing.",
			"If a value less than 1 is given 1 will be used instead.")]
		public readonly int MaximumCaptureTargetOptions = 10;

		[Desc("Should visibility (Shroud, Fog, Cloak, etc) be considered when searching for capturable targets?")]
		public readonly bool CheckCaptureTargetsForVisibility = true;

		[Desc("Player stances that capturers should attempt to target.")]
		public readonly Stance CapturableStances = Stance.Enemy | Stance.Neutral;

		static object LoadUnitCategories(MiniYaml yaml)
		{
			var categories = yaml.Nodes.First(n => n.Key == "UnitsCommonNames");
			return FieldLoader.Load<UnitCategories>(categories.Value);
		}

		static object LoadBuildingCategories(MiniYaml yaml)
		{
			var categories = yaml.Nodes.First(n => n.Key == "BuildingCommonNames");
			return FieldLoader.Load<BuildingCategories>(categories.Value);
		}

		static object LoadDecisions(MiniYaml yaml)
		{
			var ret = new List<SupportPowerDecision>();
			foreach (var d in yaml.Nodes)
				if (d.Key.Split('@')[0] == "SupportPowerDecision")
					ret.Add(new SupportPowerDecision(d.Value));

			return ret;
		}

		string IBotInfo.Type { get { return Type; } }

		string IBotInfo.Name { get { return Name; } }

		public object Create(ActorInitializer init) { return new HackyAI(this, init); }
	}

	public sealed class HackyAI : ITick, IBot, INotifyDamage
	{
		public MersenneTwister Random { get; private set; }
		public readonly HackyAIInfo Info;

		public CPos GetRandomBaseCenter()
		{
			var randomConstructionYard = World.Actors.Where(a => a.Owner == Player &&
				Info.BuildingCommonNames.ConstructionYard.Contains(a.Info.Name))
				.RandomOrDefault(Random);

			return randomConstructionYard != null ? randomConstructionYard.Location : initialBaseCenter;
		}

		public bool IsEnabled;
		public List<Squad> Squads = new List<Squad>();
		public Dictionary<Actor, Squad> WhichSquad = new Dictionary<Actor, Squad>();
		public Player Player { get; private set; }

		readonly Func<Actor, bool> isEnemyUnit;

		CPos initialBaseCenter;
		PowerManager playerPower;
		PlayerResources playerResource;
		int ticks;

		BitArray resourceTypeIndices;

		AIHarvesterManager harvManager;
		AISupportPowerManager supportPowerManager;

		List<BaseBuilder> builders = new List<BaseBuilder>();

		List<Actor> unitsHangingAroundTheBase = new List<Actor>();

		// Units that the ai already knows about. Any unit not on this list needs to be given a role.
		List<Actor> activeUnits = new List<Actor>();

		public const int FeedbackTime = 30; // ticks; = a bit over 1s. must be >= netlag.

		public readonly World World;
		public Map Map { get { return World.Map; } }
		IBotInfo IBot.Info { get { return Info; } }

		int rushTicks;
		int assignRolesTicks;
		int attackForceTicks;
		int minAttackForceDelayTicks;
		int minCaptureDelayTicks;
		readonly int maximumCaptureTargetOptions;

		readonly Queue<Order> orders = new Queue<Order>();

		readonly HashSet<Actor> luaOccupiedActors = new HashSet<Actor>();
		readonly Dictionary<Player, AIScriptContext> luaContexts = new Dictionary<Player, AIScriptContext>();

		UdpClient client;

		public HackyAI(HackyAIInfo info, ActorInitializer init)
		{
			Info = info;
			World = init.World;

			if (World.Type == WorldType.Editor)
				return;

			isEnemyUnit = unit =>
				IsOwnedByEnemy(unit)
					&& !unit.Info.HasTraitInfo<HuskInfo>()
					&& unit.Info.HasTraitInfo<ITargetableInfo>();

			maximumCaptureTargetOptions = Math.Max(1, Info.MaximumCaptureTargetOptions);

			client = new UdpClient();
			client.Connect("localhost", 9999);
		}

		public void SetLuaOccupied(Actor a, bool occupied)
		{
			if (occupied)
				luaOccupiedActors.Add(a);
			else
				luaOccupiedActors.Remove(a);
		}

		public bool IsLuaOccupied(Actor a)
		{
			return luaOccupiedActors.Contains(a);
		}

		bool UnitCannotBeOrdered(Actor a)
		{
			if (a.Owner != Player || a.IsDead || !a.IsInWorld)
				return true;

			// Don't make aircrafts land and take off like crazy
			var pool = a.TraitOrDefault<AmmoPool>();
			if (pool != null && pool.Info.SelfReloads == false && pool.HasAmmo() == false)
				return true;

			// Actors in luaOccupiedActors are under control of scripted actions and
			// shouldn't be ordered by the default hacky controller.
			return IsLuaOccupied(a);
		}

		public static void BotDebug(string s, params object[] args)
		{
			if (Game.Settings.Debug.BotDebug)
				Game.Debug(s, args);
		}

		// Called by the host's player creation code
		public void Activate(Player p)
		{
			Player = p;
			IsEnabled = true;
			playerPower = p.PlayerActor.Trait<PowerManager>();
			playerResource = p.PlayerActor.Trait<PlayerResources>();

			harvManager = new AIHarvesterManager(this, p);
			supportPowerManager = new AISupportPowerManager(this, p);

			if (luaContexts.ContainsKey(p))
			{
				luaContexts[p].Dispose();
				luaContexts.Remove(p);
			}

			AIScriptContext context = null;
			if (Info.LuaScript != null)
			{
				// when no script given, Hacky AI behavior is still in effect and will continue to work as before.
				string[] scripts = { Info.LuaScript };
				context = new AIScriptContext(World, scripts); // Create context (not yet activated)
				luaContexts.Add(p, context);
			}

			foreach (var building in Info.BuildingQueues)
				builders.Add(new BaseBuilder(this, building, p, playerPower, playerResource, context));
			foreach (var defense in Info.DefenseQueues)
				builders.Add(new BaseBuilder(this, defense, p, playerPower, playerResource, context));

			Random = new MersenneTwister(Game.CosmeticRandom.Next());

			// Avoid all AIs trying to rush in the same tick, randomize their initial rush a little.
			var smallFractionOfRushInterval = Info.RushInterval / 20;
			rushTicks = Random.Next(Info.RushInterval - smallFractionOfRushInterval, Info.RushInterval + smallFractionOfRushInterval);

			// Avoid all AIs reevaluating assignments on the same tick, randomize their initial evaluation delay.
			assignRolesTicks = Random.Next(0, Info.AssignRolesInterval);
			attackForceTicks = Random.Next(0, Info.AttackForceInterval);
			minAttackForceDelayTicks = Random.Next(0, Info.MinimumAttackForceDelay);
			minCaptureDelayTicks = Random.Next(0, Info.MinimumCaptureDelay);

			var tileset = World.Map.Rules.TileSet;
			resourceTypeIndices = new BitArray(tileset.TerrainInfo.Length); // Big enough
			foreach (var t in Map.Rules.Actors["world"].TraitInfos<ResourceTypeInfo>())
				resourceTypeIndices.Set(tileset.GetTerrainIndex(t.TerrainType), true);
		}

		public void QueueOrder(Order order)
		{
			orders.Enqueue(order);
		}

		ActorInfo ChooseRandomUnitToBuild(ProductionQueue queue)
		{
			var buildableThings = queue.BuildableItems();
			if (!buildableThings.Any())
				return null;

			var unit = buildableThings.Random(Random);
			return HasAdequateAirUnitReloadBuildings(unit) ? unit : null;
		}

		ActorInfo ChooseUnitToBuild(ProductionQueue queue)
		{
			var buildableThings = queue.BuildableItems();
			if (!buildableThings.Any())
				return null;

			var myUnits = Player.World
				.ActorsHavingTrait<IPositionable>()
				.Where(a => a.Owner == Player)
				.Select(a => a.Info.Name).ToList();

			foreach (var unit in Info.UnitsToBuild.Shuffle(Random))
				if (buildableThings.Any(b => b.Name == unit.Key))
					if (myUnits.Count(a => a == unit.Key) < unit.Value * myUnits.Count)
						if (HasAdequateAirUnitReloadBuildings(Map.Rules.Actors[unit.Key]))
							return Map.Rules.Actors[unit.Key];

			return null;
		}

		IEnumerable<Actor> GetActorsWithTrait<T>()
		{
			return World.ActorsHavingTrait<T>();
		}

		int CountActorsWithTrait<T>(string actorName, Player owner)
		{
			return GetActorsWithTrait<T>().Count(a => a.Owner == owner && a.Info.Name == actorName);
		}

		int CountBuildingByCommonName(HashSet<string> buildings, Player owner)
		{
			return GetActorsWithTrait<Building>()
				.Count(a => a.Owner == owner && buildings.Contains(a.Info.Name));
		}

		public ActorInfo GetInfoByCommonName(HashSet<string> names, Player owner)
		{
			return Map.Rules.Actors.Where(k => names.Contains(k.Key)).Random(Random).Value;
		}

		public bool HasAdequateFact()
		{
			// Require at least one construction yard, unless we have no vehicles factory (can't build it).
			return CountBuildingByCommonName(Info.BuildingCommonNames.ConstructionYard, Player) > 0 ||
				CountBuildingByCommonName(Info.BuildingCommonNames.VehiclesFactory, Player) == 0;
		}

		public bool HasAdequateProc()
		{
			// Require at least one refinery, unless we can't build it.
			return CountBuildingByCommonName(Info.BuildingCommonNames.Refinery, Player) > 0 ||
				CountBuildingByCommonName(Info.BuildingCommonNames.Power, Player) == 0 ||
				CountBuildingByCommonName(Info.BuildingCommonNames.ConstructionYard, Player) == 0;
		}

		public bool HasMinimumProc()
		{
			// Require at least two refineries, unless we have no power (can't build it)
			// or barracks (higher priority?)
			return CountBuildingByCommonName(Info.BuildingCommonNames.Refinery, Player) >= 2 ||
				CountBuildingByCommonName(Info.BuildingCommonNames.Power, Player) == 0 ||
				CountBuildingByCommonName(Info.BuildingCommonNames.Barracks, Player) == 0;
		}

		// For mods like RA (number of building must match the number of aircraft)
		bool HasAdequateAirUnitReloadBuildings(ActorInfo actorInfo)
		{
			var aircraftInfo = actorInfo.TraitInfoOrDefault<AircraftInfo>();
			if (aircraftInfo == null)
				return true;

			// If the aircraft has at least 1 AmmoPool and defines 1 or more RearmBuildings, check if we have enough of those
			var hasAmmoPool = actorInfo.TraitInfos<AmmoPoolInfo>().Any();
			if (hasAmmoPool && aircraftInfo.RearmBuildings.Count > 0)
			{
				var countOwnAir = CountActorsWithTrait<IPositionable>(actorInfo.Name, Player);
				var bldgs = World.ActorsHavingTrait<Building>().Where(a =>
					a.Owner == Player && aircraftInfo.RearmBuildings.Contains(a.Info.Name));
				int dockCount = 0;
				foreach (var b in bldgs)
					dockCount += b.TraitsImplementing<Dock>().Count();
				if (countOwnAir >= dockCount)
					return false;
			}

			return true;
		}

		// Given base center position and front vector (front vector starts from center and moves in front direction),
		// find a buildable cell in front semi-circle (or semi-annulus)
		// This function is currently used for placing structure in rear area.
		// (if you invert front vector you get rear)
		CPos? FindPosFront(CPos center, CVec front, int minRange, int maxRange,
			string actorType, BuildingInfo bi, bool distanceToBaseIsImportant)
		{
			// zero vector case. we can't define front or rear.
			if (front == CVec.Zero)
				return FindPos(center, center, minRange, maxRange, actorType, bi, distanceToBaseIsImportant);

			var ai = Map.Rules.Actors[actorType];
			var cells = Map.FindTilesInAnnulus(center, minRange, maxRange).Shuffle(Random);
			foreach (var cell in cells)
			{
				var v = cell - center;
				if (!World.CanPlaceBuilding(cell, ai, bi, null))
					continue;

				if (CVec.Dot(front, v) > 0)
					return cell;
			}

			// Front is so full of buildings that we can't place anything.
			return null;
		}

		// Find the buildable cell that is closest to pos and centered around center
		CPos? FindPos(CPos center, CPos target, int minRange, int maxRange,
			string actorType, BuildingInfo bi, bool distanceToBaseIsImportant)
		{
			var ai = Map.Rules.Actors[actorType];
			var cells = Map.FindTilesInAnnulus(center, minRange, maxRange);

			// Sort by distance to target if we have one
			if (center != target)
				cells = cells.OrderBy(c => (c - target).LengthSquared);
			else
				cells = cells.Shuffle(Random);

			foreach (var cell in cells)
			{
				if (!World.CanPlaceBuilding(cell, ai, bi, null))
					continue;

				if (distanceToBaseIsImportant && !bi.IsCloseEnoughToBase(World, Player, ai, cell))
					continue;

				return cell;
			}

			return null;
		}

		CPos defenseCenter;
		public CPos? ChooseBuildLocation(string actorType, bool distanceToBaseIsImportant, BuildingPlacementType type)
		{
			var ai = Map.Rules.Actors[actorType];
			var bi = ai.TraitInfoOrDefault<BuildingInfo>();
			if (bi == null)
				return null;

			var baseCenter = GetRandomBaseCenter();

			switch (type)
			{
				case BuildingPlacementType.Defense:
					{
						// Build near the closest enemy structure
						var closestEnemy = World.ActorsHavingTrait<Building>().Where(a => !a.Disposed && IsOwnedByEnemy(a))
							.ClosestTo(World.Map.CenterOfCell(defenseCenter));

						var targetCell = closestEnemy != null ? closestEnemy.Location : baseCenter;
						return FindPos(defenseCenter, targetCell, Info.MinimumDefenseRadius, Info.MaximumDefenseRadius,
							actorType, bi, distanceToBaseIsImportant);
					}

				case BuildingPlacementType.Fragile:
					{
						// Build far from the closest enemy structure
						var closestEnemy = World.ActorsHavingTrait<Building>().Where(a => !a.Disposed && IsOwnedByEnemy(a))
							.ClosestTo(World.Map.CenterOfCell(defenseCenter));

						CVec direction = CVec.Zero;
						if (closestEnemy != null)
							direction = baseCenter - closestEnemy.Location;

						// MinFragilePlacementRadius introduced to push fragile buildings away from base center.
						// Resilient to nuke.
						var pos = FindPosFront(baseCenter, direction, Info.MinFragilePlacementRadius, Info.MaxBaseRadius,
							actorType, bi, distanceToBaseIsImportant);
						if (pos == null) // rear placement failed but we can still try placing anywhere.
							pos = FindPos(baseCenter, baseCenter, Info.MinBaseRadius,
								distanceToBaseIsImportant ? Info.MaxBaseRadius : Map.Grid.MaximumTileSearchRange,
								actorType, bi, distanceToBaseIsImportant);
						return pos;
					}

				case BuildingPlacementType.Refinery:

					// Try and place the refinery near a resource field
					var nearbyResources = Map.FindTilesInAnnulus(baseCenter, Info.MinBaseRadius, Info.MaxBaseRadius)
						.Where(a => resourceTypeIndices.Get(Map.GetTerrainIndex(a)))
						.Shuffle(Random).Take(Info.MaxResourceCellsToCheck);

					foreach (var r in nearbyResources)
					{
						var found = FindPos(baseCenter, r, Info.MinBaseRadius, Info.MaxBaseRadius,
							actorType, bi, distanceToBaseIsImportant);
						if (found != null)
							return found;
					}

					// Try and find a free spot somewhere else in the base
					return FindPos(baseCenter, baseCenter, Info.MinBaseRadius, Info.MaxBaseRadius,
						actorType, bi, distanceToBaseIsImportant);

				case BuildingPlacementType.Building:
					return FindPos(baseCenter, baseCenter, Info.MinBaseRadius,
						distanceToBaseIsImportant ? Info.MaxBaseRadius : Map.Grid.MaximumTileSearchRange,
						actorType, bi, distanceToBaseIsImportant);
			}

			// Can't find a build location
			return null;
		}

		void ITick.Tick(Actor self)
		{
			if (!IsEnabled)
				return;

			ticks++;

			if (ticks == 1)
			{
				InitializeBase(self, false);

				// Activating here. Some variables aren't available in Lua at tick 0.
				foreach (var kv in luaContexts)
					kv.Value.ActivateAI(Player.Faction.Name, Player.InternalName);
			}
			else
			{
				// Don't tick at ticks 0 and 1.
				if (Info.EnableLuaUnitProduction) // tick each lua AI context only when told so.
					foreach (var kv in luaContexts)
						kv.Value.Tick(self);
			}

			// Fall back to hacky random production when lua production not set.
			if (!Info.EnableLuaUnitProduction && ticks % FeedbackTime == 0)
				ProductionUnits(self);

			AssignRolesToIdleUnits(self);
			SetRallyPointsForNewProductionBuildings(self);
			supportPowerManager.TryToUseSupportPower(self);

			foreach (var b in builders)
				b.Tick();

			var ordersToIssueThisTick = Math.Min((orders.Count + Info.MinOrderQuotientPerTick - 1) / Info.MinOrderQuotientPerTick, orders.Count);
			for (var i = 0; i < ordersToIssueThisTick; i++)
				World.IssueOrder(orders.Dequeue());
		}

		internal Actor FindClosestEnemy(WPos pos)
		{
			return World.Actors.Where(isEnemyUnit).ClosestTo(pos);
		}

		internal Actor FindClosestEnemy(WPos pos, WDist radius)
		{
			return World.FindActorsInCircle(pos, radius).Where(isEnemyUnit).ClosestTo(pos);
		}

		List<Actor> FindEnemyConstructionYards()
		{
			return World.Actors.Where(a => IsOwnedByEnemy(a) && !a.IsDead &&
				Info.BuildingCommonNames.ConstructionYard.Contains(a.Info.Name)).ToList();
		}

		void CleanSquads()
		{
			Squads.RemoveAll(s => !s.IsValid);
			foreach (var s in Squads)
				s.Units.RemoveAll(UnitCannotBeOrdered);
		}

		// Use of this function requires that one squad of this type. Hence it is a piece of shit
		Squad GetSquadOfType(SquadType type)
		{
			return Squads.FirstOrDefault(s => s.Type == type);
		}

		Squad RegisterNewSquad(SquadType type, Actor target = null)
		{
			var ret = new Squad(this, type, target);
			Squads.Add(ret);
			return ret;
		}

		void AssignRolesToIdleUnits(Actor self)
		{
			CleanSquads();

			activeUnits.RemoveAll(UnitCannotBeOrdered);
			unitsHangingAroundTheBase.RemoveAll(UnitCannotBeOrdered);

			if (--rushTicks <= 0)
			{
				rushTicks = Info.RushInterval;
				TryToRushAttack();
			}

			if (--attackForceTicks <= 0)
			{
				attackForceTicks = Info.AttackForceInterval;
				foreach (var s in Squads)
					s.Update();
			}

			if (--assignRolesTicks <= 0)
			{
				assignRolesTicks = Info.AssignRolesInterval;
				harvManager.Tick(activeUnits);
				FindNewUnits(self);
				InitializeBase(self, true);
			}

			if (--minAttackForceDelayTicks <= 0)
			{
				minAttackForceDelayTicks = Info.MinimumAttackForceDelay;
				CreateAttackForce();
			}

			if (--minCaptureDelayTicks <= 0)
			{
				minCaptureDelayTicks = Info.MinimumCaptureDelay;
				QueueCaptureOrders();
			}
		}

		void KillOutOfMapAircrafts()
		{
			var map = World.Map;
			var toKill = World.ActorsHavingTrait<IPositionable>()
				.Where(a => a.Owner == Player && !map.Contains(a.Location));

			foreach (var a in toKill)
			{
				a.CancelActivity();
				a.QueueActivity(new CallFunc(() => a.Kill(a)));
			}
		}

		IEnumerable<Actor> GetVisibleActorsBelongingToPlayer(Player owner)
		{
			foreach (var actor in GetActorsThatCanBeOrderedByPlayer(owner))
				if (actor.CanBeViewedByPlayer(Player))
					yield return actor;
		}

		IEnumerable<Actor> GetActorsThatCanBeOrderedByPlayer(Player owner)
		{
			foreach (var actor in World.Actors)
				if (actor.Owner == owner && !actor.IsDead && actor.IsInWorld)
					yield return actor;
		}

		void QueueCaptureOrders()
		{
			if (!Info.CapturingActorTypes.Any() || Player.WinState != WinState.Undefined)
				return;

			var capturers = unitsHangingAroundTheBase.Where(a => a.IsIdle && Info.CapturingActorTypes.Contains(a.Info.Name)).ToArray();
			if (capturers.Length == 0)
				return;

			var randPlayer = World.Players.Where(p => !p.Spectating
				&& Info.CapturableStances.HasStance(Player.Stances[p])).Random(Random);

			var targetOptions = Info.CheckCaptureTargetsForVisibility
				? GetVisibleActorsBelongingToPlayer(randPlayer)
				: GetActorsThatCanBeOrderedByPlayer(randPlayer);

			var capturableTargetOptions = targetOptions
				.Select(a => new CaptureTarget<CapturableInfo>(a, "CaptureActor"))
				.Where(target =>
				{
					if (target.Info == null)
						return false;

					var capturable = target.Actor.TraitOrDefault<Capturable>();
					if (capturable == null)
						return false;

					return capturers.Any(capturer => capturable.CanBeTargetedBy(capturer, target.Actor.Owner));
				})
				.OrderByDescending(target => target.Actor.GetSellValue())
				.Take(maximumCaptureTargetOptions);

			var externalCapturableTargetOptions = targetOptions
				.Select(a => new CaptureTarget<ExternalCapturableInfo>(a, "ExternalCaptureActor"))
				.Where(target =>
				{
					if (target.Info == null)
						return false;

					var externalCapturable = target.Actor.TraitOrDefault<ExternalCapturable>();
					if (externalCapturable == null)
						return false;

					return capturers.Any(capturer => externalCapturable.CanBeTargetedBy(capturer, target.Actor.Owner));
				})
				.OrderByDescending(target => target.Actor.GetSellValue())
				.Take(maximumCaptureTargetOptions);

			if (Info.CapturableActorTypes.Any())
			{
				capturableTargetOptions = capturableTargetOptions.Where(target => Info.CapturableActorTypes.Contains(target.Actor.Info.Name.ToLowerInvariant()));
				externalCapturableTargetOptions = externalCapturableTargetOptions.Where(target => Info.CapturableActorTypes.Contains(target.Actor.Info.Name.ToLowerInvariant()));
			}

			if (!capturableTargetOptions.Any() && !externalCapturableTargetOptions.Any())
				return;

			var externalCapturers = capturers.Where(a => a.Info.HasTraitInfo<ExternalCapturesInfo>());
			var capturesCapturers = capturers.Except(externalCapturers).Where(a => a.Info.HasTraitInfo<CapturesInfo>());

			foreach (var capturer in capturesCapturers)
				QueueCaptureOrderFor(capturer, GetCapturerTargetClosestToOrDefault(capturer, capturableTargetOptions));

			foreach (var capturer in externalCapturers)
				QueueCaptureOrderFor(capturer, GetCapturerTargetClosestToOrDefault(capturer, externalCapturableTargetOptions));
		}

		void QueueCaptureOrderFor<TTargetType>(Actor capturer, CaptureTarget<TTargetType> target) where TTargetType : class, ITraitInfoInterface
		{
			if (capturer == null)
				return;

			if (target == null)
				return;

			if (target.Actor == null)
				return;

			QueueOrder(new Order(target.OrderString, capturer, Target.FromActor(target.Actor), true));
			BotDebug("AI ({0}): Ordered {1} to capture {2}", Player.ClientIndex, capturer, target.Actor);
			activeUnits.Remove(capturer);
		}

		CaptureTarget<TTargetType> GetCapturerTargetClosestToOrDefault<TTargetType>(Actor capturer, IEnumerable<CaptureTarget<TTargetType>> targets)
			where TTargetType : class, ITraitInfoInterface
		{
			return targets.MinByOrDefault(target => (target.Actor.CenterPosition - capturer.CenterPosition).LengthSquared);
		}

		void FindNewUnits(Actor self)
		{
			var newUnits = self.World.ActorsHavingTrait<IPositionable>()
				.Where(a => a.Owner == Player && !Info.UnitsCommonNames.Mcv.Contains(a.Info.Name) &&
					!Info.UnitsCommonNames.ExcludeFromSquads.Contains(a.Info.Name) && !activeUnits.Contains(a));

			foreach (var a in newUnits)
			{
				unitsHangingAroundTheBase.Add(a);

				if (a.Info.HasTraitInfo<AircraftInfo>() && a.Info.HasTraitInfo<AttackBaseInfo>())
				{
					var air = GetSquadOfType(SquadType.Air);
					if (air == null)
						air = RegisterNewSquad(SquadType.Air);

					air.Units.Add(a);
					WhichSquad[a] = air;
				}
				else if (Info.UnitsCommonNames.NavalUnits.Contains(a.Info.Name))
				{
					var ships = GetSquadOfType(SquadType.Naval);
					if (ships == null)
						ships = RegisterNewSquad(SquadType.Naval);

					ships.Units.Add(a);
				}

				activeUnits.Add(a);
			}
		}

		void CreateAttackForce()
		{
			// Create an attack force when we have enough units around our base.
			// (don't bother leaving any behind for defense)
			var randomizedSquadSize = Info.SquadSize + Random.Next(Info.SquadSizeRandomBonus);

			if (unitsHangingAroundTheBase.Count >= randomizedSquadSize)
			{
				var attackForce = RegisterNewSquad(SquadType.Assault);

				foreach (var a in unitsHangingAroundTheBase)
					if (!a.Info.HasTraitInfo<AircraftInfo>())
					{
						attackForce.Units.Add(a);
						WhichSquad[a] = attackForce;
					}

				unitsHangingAroundTheBase.Clear();
			}
		}

		public bool IsOwnedByEnemy(Actor a)
		{
			return Player.Stances[a.Owner] == Stance.Enemy && a.Owner.InternalName.ToLowerInvariant().StartsWith("multi");
		}

		void TryToRushAttack()
		{
			var allEnemyBaseBuilder = FindEnemyConstructionYards();
			var ownUnits = activeUnits
				.Where(unit => unit.IsIdle && unit.Info.HasTraitInfo<AttackBaseInfo>()
					&& !unit.Info.HasTraitInfo<AircraftInfo>() && !unit.Info.HasTraitInfo<HarvesterInfo>()).ToList();

			if (!allEnemyBaseBuilder.Any() || (ownUnits.Count < Info.SquadSize))
				return;

			foreach (var b in allEnemyBaseBuilder)
			{
				var enemies = World.FindActorsInCircle(b.CenterPosition, WDist.FromCells(Info.RushAttackScanRadius))
					.Where(unit => IsOwnedByEnemy(unit) && unit.Info.HasTraitInfo<AttackBaseInfo>()).ToList();

				if (AttackOrFleeFuzzy.Rush.CanAttack(ownUnits, enemies))
				{
					Actor target = enemies.Any() ? enemies.Random(Random) : b;
					//// TODO: make on target selected Lua call

					var rush = GetSquadOfType(SquadType.Rush);
					if (rush == null)
						rush = RegisterNewSquad(SquadType.Rush, target);

					foreach (var a3 in ownUnits)
					{
						rush.Units.Add(a3);
						WhichSquad[a3] = rush;
					}

					return;
				}
			}
		}

		void ProtectOwn(Actor attacker)
		{
			var protectSq = GetSquadOfType(SquadType.Protection);
			if (protectSq == null)
				protectSq = RegisterNewSquad(SquadType.Protection, attacker);

			if (!protectSq.IsTargetValid)
				protectSq.TargetActor = attacker;

			if (!protectSq.IsValid)
			{
				var ownUnits = World.FindActorsInCircle(World.Map.CenterOfCell(GetRandomBaseCenter()), WDist.FromCells(Info.ProtectUnitScanRadius))
					.Where(unit => unit.Owner == Player && !unit.Info.HasTraitInfo<BuildingInfo>() && !unit.Info.HasTraitInfo<HarvesterInfo>()
						&& unit.Info.HasTraitInfo<AttackBaseInfo>());

				foreach (var a in ownUnits)
				{
					protectSq.Units.Add(a);
					WhichSquad[a] = protectSq;
				}
			}
		}

		bool IsRallyPointValid(CPos x, BuildingInfo info)
		{
			return info != null && World.IsCellBuildable(x, null, info);
		}

		void SetRallyPointsForNewProductionBuildings(Actor self)
		{
			foreach (var rp in self.World.ActorsWithTrait<RallyPoint>())
			{
				if (rp.Actor.Owner == Player &&
					!IsRallyPointValid(rp.Trait.Location, rp.Actor.Info.TraitInfoOrDefault<BuildingInfo>()))
				{
					QueueOrder(new Order("SetRallyPoint", rp.Actor, Target.FromCell(World, ChooseRallyLocationNear(rp.Actor)), false)
					{
						SuppressVisualFeedback = true
					});
				}
			}
		}

		// Won't work for shipyards...
		CPos ChooseRallyLocationNear(Actor producer)
		{
			var possibleRallyPoints = Map.FindTilesInCircle(producer.Location, Info.RallyPointScanRadius)
				.Where(c => IsRallyPointValid(c, producer.Info.TraitInfoOrDefault<BuildingInfo>()));

			if (!possibleRallyPoints.Any())
			{
				BotDebug("Bot Bug: No possible rallypoint near {0}", producer.Location);
				return producer.Location;
			}

			return possibleRallyPoints.Random(Random);
		}

		void InitializeBase(Actor self, bool chooseLocation)
		{
			var mcv = FindAndDeployMcv(self, chooseLocation);

			if (mcv == null)
				return;

			initialBaseCenter = mcv.Location;
			defenseCenter = mcv.Location;
		}

		// Find any MCV and deploy them at a sensible location.
		Actor FindAndDeployMcv(Actor self, bool move)
		{
			var mcv = self.World.Actors.FirstOrDefault(a => a.Owner == Player &&
				Info.UnitsCommonNames.Mcv.Contains(a.Info.Name) && a.IsIdle);

			if (mcv == null)
				return null;

			// Don't try to move and deploy an undeployable actor
			var transformsInfo = mcv.Info.TraitInfoOrDefault<TransformsInfo>();
			if (transformsInfo == null)
				return null;

			// If we lack a base, we need to make sure we don't restrict deployment of the MCV to the base!
			var restrictToBase = Info.RestrictMCVDeploymentFallbackToBase &&
				CountBuildingByCommonName(Info.BuildingCommonNames.ConstructionYard, Player) > 0;

			if (move)
			{
				var desiredLocation = ChooseBuildLocation(transformsInfo.IntoActor, restrictToBase, BuildingPlacementType.Building);
				if (desiredLocation == null)
					return null;

				QueueOrder(new Order("Move", mcv, Target.FromCell(World, desiredLocation.Value), true));
			}

			QueueOrder(new Order("DeployTransform", mcv, true));

			return mcv;
		}

		internal IEnumerable<ProductionQueue> FindQueues(string category)
		{
			return World.ActorsWithTrait<ProductionQueue>()
				.Where(a => a.Actor.Owner == Player && a.Trait.Info.Type == category && a.Trait.Enabled)
				.Select(a => a.Trait);
		}

		void ProductionUnits(Actor self)
		{
			// Stop building until economy is restored
			if (!HasAdequateProc())
				return;

			// No construction yards - Build a new MCV
			if (Info.UnitsCommonNames.Mcv.Any() && !HasAdequateFact() && !self.World.Actors.Any(a => a.Owner == Player &&
				Info.UnitsCommonNames.Mcv.Contains(a.Info.Name)))
				BuildUnit("Vehicle", GetInfoByCommonName(Info.UnitsCommonNames.Mcv, Player).Name);

			foreach (var q in Info.UnitQueues)
			{
				if (Info.PipeExternalQuery)
					QueryPipe(q, unitsHangingAroundTheBase);
				else
					BuildUnit(q, unitsHangingAroundTheBase.Count < Info.IdleBaseUnitsMaximum);
			}
		}

		void BuildUnit(string category, bool buildRandom)
		{
			// Pick a free queue
			var queue = FindQueues(category).FirstOrDefault(q => q.CurrentItem() == null);
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

			if (Info.UnitLimits != null &&
				Info.UnitLimits.ContainsKey(name) &&
				World.Actors.Count(a => a.Owner == Player && a.Info.Name == name) >= Info.UnitLimits[name])
				return;

			QueueOrder(Order.StartProduction(queue.Actor, name, 1));
		}

		void BuildUnit(string category, string name)
		{
			var queue = FindQueues(category).FirstOrDefault(q => q.CurrentItem() == null);
			if (queue == null)
				return;

			if (Map.Rules.Actors[name] != null)
				QueueOrder(Order.StartProduction(queue.Actor, name, 1));
		}

		void INotifyDamage.Damaged(Actor self, AttackInfo e)
		{
			if (!IsEnabled || e.Attacker == null)
				return;

			if (e.Attacker.Owner.Stances[self.Owner] == Stance.Neutral)
				return;

			var rb = self.TraitOrDefault<RepairableBuilding>();

			if (Info.ShouldRepairBuildings && rb != null)
			{
				if (e.DamageState > DamageState.Light && e.PreviousDamageState <= DamageState.Light && !rb.RepairActive)
				{
					BotDebug("Bot noticed damage {0} {1}->{2}, repairing.",
						self, e.PreviousDamageState, e.DamageState);
					QueueOrder(new Order("RepairBuilding", self.Owner.PlayerActor, Target.FromActor(self), false));
				}
			}

			if (e.Attacker.Disposed)
				return;

			if (!e.Attacker.Info.HasTraitInfo<ITargetableInfo>())
				return;

			// Protected priority assets, MCVs, harvesters and buildings
			if ((self.Info.HasTraitInfo<HarvesterInfo>() || self.Info.HasTraitInfo<BuildingInfo>() || self.Info.HasTraitInfo<BaseBuildingInfo>()) &&
				Player.Stances[e.Attacker.Owner] == Stance.Enemy)
			{
				defenseCenter = e.Attacker.Location;
				ProtectOwn(e.Attacker);
			}
			else if (self.TraitOrDefault<Aircraft>() != null)
			{
				if (e.Damage.Value == 0)
					return;

				// HARDCODE: Aurora keep target
				if (self.Info.Name == "mig")
					return;

				// U2 or spawned stuff
				if (self.TraitOrDefault<Selectable>() == null)
					return;

				// Aircraft micro control
				if (WhichSquad.ContainsKey(self))
				{
					WhichSquad[self].Damage(e); // Reflex avoidance
					WhichSquad[self].Update(); // Determine flee or not
				}
			}
		}

		void Send(UdpClient udp, string msg)
		{
			var sendBytes = Encoding.UTF8.GetBytes(msg);
			udp.Send(sendBytes, sendBytes.Length);
		}

		void QueryPipe(string category, IEnumerable<Actor> unit)
		{
			// Pick a free queue
			var queue = FindQueues(category).FirstOrDefault(q => q.CurrentItem() == null);
			if (queue == null)
				return;

			var buildableThings = queue.BuildableItems();
			if (!buildableThings.Any())
				return;

			foreach (var b in buildableThings)
				Send(client, b.Name);
			Send(client, "END");

			// Get incoming result
			IPEndPoint remoteIpEndPoint = new IPEndPoint(IPAddress.Any, 0);
			var recv = client.Receive(ref remoteIpEndPoint);
			string name = Encoding.UTF8.GetString(recv);

			QueueOrder(Order.StartProduction(queue.Actor, name, 1));
		}
	}
}
