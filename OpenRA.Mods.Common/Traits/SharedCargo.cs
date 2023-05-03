#region Copyright & License Information
/*
 * Copyright 2007-2020 The OpenRA Developers (see AUTHORS)
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
using OpenRA.Mods.Common.Orders;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("This actor can transport Passenger actors.")]
	public class SharedCargoInfo : PausableConditionalTraitInfo, Requires<IOccupySpaceInfo>
	{
		[Desc("`SharedPassenger.CargoType`s that can be loaded into this actor.")]
		public readonly HashSet<string> Types = new HashSet<string>();

		[Desc("`SharedCargoManager.Type` thar this actor shares its passengers.")]
		public readonly string ShareType = "tunnel";

		[Desc("Terrain types that this actor is allowed to eject actors onto. Leave empty for all terrain types.")]
		public readonly HashSet<string> UnloadTerrainTypes = new HashSet<string>();

		[VoiceReference]
		[Desc("Voice to play when ordered to unload the passengers.")]
		public readonly string UnloadVoice = "Action";

		[Desc("Radius to search for a load/unload location if the ordered cell is blocked.")]
		public readonly WDist LoadRange = WDist.FromCells(5);

		[Desc("Which direction the passenger will face (relative to the transport) when unloading.")]
		public readonly WAngle PassengerFacing = new WAngle(512);

		[Desc("Delay (in ticks) before continuing after loading a passenger.")]
		public readonly int AfterLoadDelay = 8;

		[Desc("Delay (in ticks) before unloading the first passenger.")]
		public readonly int BeforeUnloadDelay = 8;

		[Desc("Delay (in ticks) before continuing after unloading a passenger.")]
		public readonly int AfterUnloadDelay = 25;

		[Desc("Cursor to display when able to unload the passengers.")]
		public readonly string UnloadCursor = "deploy";

		[Desc("Cursor to display when unable to unload the passengers.")]
		public readonly string UnloadBlockedCursor = "deploy-blocked";

		[GrantedConditionReference]
		[Desc("The condition to grant to self while waiting for cargo to load.")]
		public readonly string LoadingCondition = null;

		[GrantedConditionReference]
		[Desc("The condition to grant to self while passengers are loaded.",
			"Condition can stack with multiple passengers.")]
		public readonly string LoadedCondition = null;

		[ActorReference(dictionaryReference: LintDictionaryReference.Keys)]
		[Desc("Conditions to grant when specified actors are loaded inside the transport.",
			"A dictionary of [actor id]: [condition].")]
		public readonly Dictionary<string, string> PassengerConditions = new Dictionary<string, string>();

		[GrantedConditionReference]
		public IEnumerable<string> LinterPassengerConditions { get { return PassengerConditions.Values; } }

		public override object Create(ActorInitializer init) { return new SharedCargo(init, this); }
	}

	public class SharedCargo : PausableConditionalTrait<SharedCargoInfo>, IIssueOrder, IResolveOrder, IOrderVoice, INotifyCreated,
		INotifyAddedToWorld, ITick, IIssueDeployOrder, INotifyKilled, INotifyActorDisposing, INotifyPassengersDamage
	{
		readonly Actor self;
		public readonly SharedCargoManager Manager;
		readonly Dictionary<string, Stack<int>> passengerTokens = new Dictionary<string, Stack<int>>();
		readonly Lazy<IFacing> facing;
		readonly bool checkTerrainType;

		Aircraft aircraft;
		int loadingToken = Actor.InvalidConditionToken;
		Stack<int> loadedTokens = new Stack<int>();
		bool takeOffAfterLoad;
		bool initialized;

		CPos currentCell;
		public IEnumerable<CPos> CurrentAdjacentCells { get; private set; }

		enum State { Free, Locked }
		State state = State.Free;

		public SharedCargo(ActorInitializer init, SharedCargoInfo info)
			: base(info)
		{
			self = init.Self;
			Manager = self.Owner.PlayerActor.TraitsImplementing<SharedCargoManager>().Where(m => m.Info.Type == Info.ShareType).First();
			checkTerrainType = info.UnloadTerrainTypes.Count > 0;
			facing = Exts.Lazy(self.TraitOrDefault<IFacing>);
		}

		protected override void Created(Actor self)
		{
			aircraft = self.TraitOrDefault<Aircraft>();

			base.Created(self);
		}

		static int GetWeight(Actor a) { return a.Info.TraitInfo<SharedPassengerInfo>().Weight; }

		public IEnumerable<IOrderTargeter> Orders
		{
			get
			{
				if (IsTraitDisabled)
					yield break;

				yield return new DeployOrderTargeter("UnloadShared", 10, () => CanUnload() ? Info.UnloadCursor : Info.UnloadBlockedCursor);
			}
		}

		public Order IssueOrder(Actor self, IOrderTargeter order, in Target target, bool queued)
		{
			if (order.OrderID == "UnloadShared")
				return new Order(order.OrderID, self, queued);

			return null;
		}

		Order IIssueDeployOrder.IssueDeployOrder(Actor self, bool queued)
		{
			return new Order("UnloadShared", self, queued);
		}

		bool IIssueDeployOrder.CanIssueDeployOrder(Actor self, bool queued) { return true; }

		public void ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "UnloadShared")
			{
				if (!order.Queued && !CanUnload())
					return;

				self.QueueActivity(order.Queued, new UnloadSharedCargo(self, Info.LoadRange));
			}
		}

		IEnumerable<CPos> GetAdjacentCells()
		{
			return Util.AdjacentCells(self.World, Target.FromActor(self)).Where(c => self.Location != c);
		}

		public bool CanUnload(BlockedByActor check = BlockedByActor.None)
		{
			if (checkTerrainType)
			{
				var terrainType = self.World.Map.GetTerrainInfo(self.Location).Type;

				if (!Info.UnloadTerrainTypes.Contains(terrainType))
					return false;
			}

			return !Manager.IsEmpty() && (aircraft == null || aircraft.CanLand(self.Location, blockedByMobile: false)) && !IsTraitPaused
				&& CurrentAdjacentCells != null && CurrentAdjacentCells.Any(c => Manager.Passengers.Any(p => !p.IsDead && p.Trait<IPositionable>().CanEnterCell(c, null, check)));
		}

		public bool CanLoad(Actor self, Actor a)
		{
			return (Manager.Reserves.Contains(a) || Manager.HasSpace(GetWeight(a))) && !IsTraitPaused;
		}

		internal bool ReserveSpace(Actor a)
		{
			if (Manager.Reserves.Contains(a))
				return true;

			var w = GetWeight(a);
			if (!Manager.HasSpace(w))
				return false;

			if (loadingToken == Actor.InvalidConditionToken)
				loadingToken = self.GrantCondition(Info.LoadingCondition);

			Manager.Reserves.Add(a);
			Manager.ReservedWeight += w;
			LockForPickup(self);

			return true;
		}

		internal void UnreserveSpace(Actor a)
		{
			if (!Manager.Reserves.Contains(a) || self.IsDead)
				return;

			Manager.ReservedWeight -= GetWeight(a);
			Manager.Reserves.Remove(a);
			ReleaseLock(self);

			if (loadingToken != Actor.InvalidConditionToken)
				loadingToken = self.RevokeCondition(loadingToken);
		}

		// Prepare for transport pickup
		void LockForPickup(Actor self)
		{
			if (state == State.Locked)
				return;

			state = State.Locked;

			self.CancelActivity();

			var air = self.TraitOrDefault<Aircraft>();
			if (air != null && !air.AtLandAltitude)
			{
				takeOffAfterLoad = true;
				self.QueueActivity(new Land(self));
			}

			self.QueueActivity(new WaitFor(() => state != State.Locked, false));
		}

		void ReleaseLock(Actor self)
		{
			if (Manager.ReservedWeight != 0)
				return;

			state = State.Free;

			self.QueueActivity(new Wait(Info.AfterLoadDelay, false));
			if (takeOffAfterLoad)
				self.QueueActivity(new TakeOff(self));

			takeOffAfterLoad = false;
		}

		public string CursorForOrder(Actor self, Order order)
		{
			if (order.OrderString != "UnloadShared")
				return null;

			return CanUnload() ? Info.UnloadCursor : Info.UnloadBlockedCursor;
		}

		public string VoicePhraseForOrder(Actor self, Order order)
		{
			if (order.OrderString != "UnloadShared" || Manager.IsEmpty() || !self.HasVoice(Info.UnloadVoice))
				return null;

			return Info.UnloadVoice;
		}

		public Actor Peek(Actor self) { return Manager.Cargo.Last(); }

		public Actor Unload(Actor self, Actor passenger = null)
		{
			passenger = passenger ?? Manager.Cargo.Last();
			if (!Manager.Cargo.Remove(passenger))
				throw new ArgumentException("Attempted to unload an actor that is not a passenger.");

			Manager.TotalWeight -= GetWeight(passenger);

			SetPassengerFacing(passenger);

			foreach (var npe in self.TraitsImplementing<INotifyPassengerExited>())
				npe.OnPassengerExited(self, passenger);

			foreach (var nec in passenger.TraitsImplementing<INotifyExitedSharedCargo>())
				nec.OnExitedSharedCargo(passenger, self);

			var p = passenger.Trait<SharedPassenger>();
			p.Transport = null;

			if (passengerTokens.TryGetValue(passenger.Info.Name, out var passengerToken) && passengerToken.Any())
				self.RevokeCondition(passengerToken.Pop());

			if (loadedTokens.Any())
				self.RevokeCondition(loadedTokens.Pop());

			return passenger;
		}

		void SetPassengerFacing(Actor passenger)
		{
			if (facing.Value == null)
				return;

			var passengerFacing = passenger.TraitOrDefault<IFacing>();
			if (passengerFacing != null)
				passengerFacing.Facing = facing.Value.Facing + Info.PassengerFacing;
		}

		public void Load(Actor self, Actor a)
		{
			Manager.Cargo.Add(a);
			var w = GetWeight(a);
			Manager.TotalWeight += w;
			if (Manager.Reserves.Contains(a))
			{
				Manager.ReservedWeight -= w;
				Manager.Reserves.Remove(a);
				ReleaseLock(self);

				if (loadingToken != Actor.InvalidConditionToken)
					loadingToken = self.RevokeCondition(loadingToken);
			}

			// If not initialized then this will be notified in the first tick
			if (initialized)
			{
				a.Trait<SharedPassenger>().Transport = self;

				foreach (var nec in a.TraitsImplementing<INotifyEnteredSharedCargo>())
					nec.OnEnteredSharedCargo(a, self);

				foreach (var npe in self.TraitsImplementing<INotifyPassengerEntered>())
					npe.OnPassengerEntered(self, a);
			}

			if (Info.PassengerConditions.TryGetValue(a.Info.Name, out var passengerCondition))
				passengerTokens.GetOrAdd(a.Info.Name).Push(self.GrantCondition(passengerCondition));

			if (!string.IsNullOrEmpty(Info.LoadedCondition))
				loadedTokens.Push(self.GrantCondition(Info.LoadedCondition));
		}

		void INotifyKilled.Killed(Actor self, AttackInfo e)
		{
			if (!self.World.ActorsWithTrait<SharedCargo>().Where(a => a.Trait.Info.ShareType == Info.ShareType && a.Actor.Owner == self.Owner && a.Actor != self).Any())
				Manager.Clear(e);
		}

		void INotifyActorDisposing.Disposing(Actor self)
		{
			if (!self.World.ActorsWithTrait<SharedCargo>().Where(a => a.Trait.Info.ShareType == Info.ShareType && a.Actor.Owner == self.Owner && a.Actor != self).Any())
				Manager.Clear();
		}

		void INotifyAddedToWorld.AddedToWorld(Actor self)
		{
			// Force location update to avoid issues when initial spawn is outside map
			currentCell = self.Location;
			CurrentAdjacentCells = GetAdjacentCells();
		}

		void ITick.Tick(Actor self)
		{
			// Notify initial cargo load
			if (!initialized)
			{
				foreach (var c in Manager.Cargo)
				{
					c.Trait<SharedPassenger>().Transport = self;

					foreach (var npe in self.TraitsImplementing<INotifyPassengerEntered>())
						npe.OnPassengerEntered(self, c);

					foreach (var nec in c.TraitsImplementing<INotifyEnteredSharedCargo>())
						nec.OnEnteredSharedCargo(c, self);
				}

				initialized = true;
			}

			var cell = self.World.Map.CellContaining(self.CenterPosition);
			if (currentCell != cell)
			{
				currentCell = cell;
				CurrentAdjacentCells = GetAdjacentCells();
			}
		}

		int DamageVersus(Actor victim, Dictionary<string, int> versus)
		{
			// If no Versus values are defined, DamageVersus would return 100 anyway, so we might as well do that early.
			if (versus.Count == 0)
				return 100;

			var armor = victim.TraitsImplementing<Armor>()
				.Where(a => !a.IsTraitDisabled && a.Info.Type != null && versus.ContainsKey(a.Info.Type))
				.Select(a => versus[a.Info.Type]);

			return Util.ApplyPercentageModifiers(100, armor);
		}

		void INotifyPassengersDamage.DamagePassengers(int damage, Actor attacker, int amount, Dictionary<string, int> versus, BitSet<DamageType> damageTypes, IEnumerable<int> damageModifiers)
		{
			var passengersToDamage = amount > 0 && amount < Manager.Cargo.ToArray().Count() ? Manager.Cargo.Shuffle(self.World.SharedRandom).Take(amount).ToArray() : Manager.Cargo.ToArray();
			foreach (var passenger in passengersToDamage)
			{
				var d = Util.ApplyPercentageModifiers(damage, damageModifiers.Append(DamageVersus(passenger, versus)));
				passenger.InflictDamage(attacker, new Damage(d, damageTypes));
			}
		}
	}
}
