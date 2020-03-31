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

using System.Collections.Generic;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Orders;
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("This actor can enter SharedCargo actors.")]
	public class SharedPassengerInfo : ITraitInfo, IObservesVariablesInfo
	{
		public readonly string CargoType = null;
		public readonly PipType PipType = PipType.Green;
		public readonly int Weight = 1;

		[GrantedConditionReference]
		[Desc("The condition to grant to when this actor is loaded inside any transport.")]
		public readonly string CargoCondition = null;

		[Desc("Conditions to grant when this actor is loaded inside specified transport.",
			"A dictionary of [actor id]: [condition].")]
		public readonly Dictionary<string, string> CargoConditions = new Dictionary<string, string>();

		[GrantedConditionReference]
		public IEnumerable<string> LinterCargoConditions { get { return CargoConditions.Values; } }

		[VoiceReference]
		public readonly string Voice = "Action";

		[ConsumedConditionReference]
		[Desc("Boolean expression defining the condition under which the regular (non-force) enter cursor is disabled.")]
		public readonly BooleanExpression RequireForceMoveCondition = null;

		public object Create(ActorInitializer init) { return new SharedPassenger(this); }
	}

	public class SharedPassenger : INotifyCreated, IIssueOrder, IResolveOrder, IOrderVoice, INotifyRemovedFromWorld, INotifyEnteredSharedCargo, INotifyExitedSharedCargo, IObservesVariables
	{
		public readonly SharedPassengerInfo Info;
		public Actor Transport;
		bool requireForceMove;

		ConditionManager conditionManager;
		int anyCargoToken = ConditionManager.InvalidConditionToken;
		int specificCargoToken = ConditionManager.InvalidConditionToken;

		public SharedPassenger(SharedPassengerInfo info)
		{
			Info = info;
		}

		public SharedCargo ReservedCargo { get; private set; }

		IEnumerable<IOrderTargeter> IIssueOrder.Orders
		{
			get
			{
				yield return new EnterAlliedActorTargeter<SharedCargoInfo>("EnterTransport", 5, IsCorrectCargoType, CanEnter);
			}
		}

		void INotifyCreated.Created(Actor self)
		{
			conditionManager = self.TraitOrDefault<ConditionManager>();
		}

		public Order IssueOrder(Actor self, IOrderTargeter order, Target target, bool queued)
		{
			if (order.OrderID == "EnterSharedTransport")
				return new Order(order.OrderID, self, target, queued);

			return null;
		}

		bool IsCorrectCargoType(Actor target, TargetModifiers modifiers)
		{
			if (requireForceMove && !modifiers.HasModifier(TargetModifiers.ForceMove))
				return false;

			return IsCorrectCargoType(target);
		}

		bool IsCorrectCargoType(Actor target)
		{
			var c = target.TraitOrDefault<SharedCargo>();
			return c != null && c.Info.Types.Contains(Info.CargoType) && !c.IsTraitDisabled;
		}

		bool CanEnter(SharedCargo cargo)
		{
			return cargo != null && cargo.Manager.HasSpace(Info.Weight) && !cargo.IsTraitPaused;
		}

		bool CanEnter(Actor target)
		{
			return CanEnter(target.TraitOrDefault<SharedCargo>());
		}

		public string VoicePhraseForOrder(Actor self, Order order)
		{
			if (order.OrderString != "EnterSharedTransport")
				return null;

			return Info.Voice;
		}

		void INotifyEnteredSharedCargo.OnEnteredSharedCargo(Actor self, Actor cargo)
		{
			string specificCargoCondition;
			if (conditionManager != null)
			{
				if (anyCargoToken == ConditionManager.InvalidConditionToken && !string.IsNullOrEmpty(Info.CargoCondition))
					anyCargoToken = conditionManager.GrantCondition(self, Info.CargoCondition);

				if (specificCargoToken == ConditionManager.InvalidConditionToken && Info.CargoConditions.TryGetValue(cargo.Info.Name, out specificCargoCondition))
					specificCargoToken = conditionManager.GrantCondition(self, specificCargoCondition);
			}
		}

		void INotifyExitedSharedCargo.OnExitedSharedCargo(Actor self, Actor cargo)
		{
			if (anyCargoToken != ConditionManager.InvalidConditionToken)
				anyCargoToken = conditionManager.RevokeCondition(self, anyCargoToken);

			if (specificCargoToken != ConditionManager.InvalidConditionToken)
				specificCargoToken = conditionManager.RevokeCondition(self, specificCargoToken);
		}

		public void ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString != "EnterSharedTransport")
				return;

			// Enter orders are only valid for own/allied actors,
			// which are guaranteed to never be frozen.
			if (order.Target.Type != TargetType.Actor)
				return;

			var targetActor = order.Target.Actor;
			if (!CanEnter(targetActor))
				return;

			if (!IsCorrectCargoType(targetActor))
				return;

			if (!order.Queued)
				self.CancelActivity();

			self.QueueActivity(new RideSharedTransport(self, order.Target));
			self.ShowTargetLines();
		}

		public bool Reserve(Actor self, SharedCargo cargo)
		{
			Unreserve(self);
			if (!cargo.ReserveSpace(self))
				return false;
			ReservedCargo = cargo;
			return true;
		}

		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self) { Unreserve(self); }

		public void Unreserve(Actor self)
		{
			if (ReservedCargo == null)
				return;
			ReservedCargo.UnreserveSpace(self);
			ReservedCargo = null;
		}

		IEnumerable<VariableObserver> IObservesVariables.GetVariableObservers()
		{
			if (Info.RequireForceMoveCondition != null)
				yield return new VariableObserver(RequireForceMoveConditionChanged, Info.RequireForceMoveCondition.Variables);
		}

		void RequireForceMoveConditionChanged(Actor self, IReadOnlyDictionary<string, int> conditions)
		{
			requireForceMove = Info.RequireForceMoveCondition.Evaluate(conditions);
		}
	}
}
