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
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Grants conditions when this actor produces specific actors, which are removed when a different one is produced.")]
	public class GrantMutualConditionsOnProductionInfo : TraitInfo
	{
		[FieldLoader.Require]
		[Desc("The condition to grant per actor produced",
			"A dictionary of [actor id]: [condition].")]
		public readonly Dictionary<string, string> Conditions = null;

		[GrantedConditionReference]
		[Desc("If set, start with this condition granted.")]
		public readonly string InitialCondition = null;

		[Desc("How long condition is applies for. Use -1 for infinite.")]
		public readonly int Duration = -1;

		[Desc("Show a selection bar while condition is applied if it has a duration.")]
		public readonly bool ShowSelectionBar = true;
		public readonly Color SelectionBarColor = Color.Magenta;

		[GrantedConditionReference]
		public IEnumerable<string> LinterConditions { get { return Conditions.Values; } }

		public override object Create(ActorInitializer init) { return new GrantMutualConditionsOnProduction(init.Self, this); }
	}

	public class GrantMutualConditionsOnProduction : INotifyProduction, ITick, ISync, ISelectionBar
	{
		readonly GrantMutualConditionsOnProductionInfo info;

		int token = Actor.InvalidConditionToken;

		[VerifySync]
		int ticks;

		public GrantMutualConditionsOnProduction(Actor self, GrantMutualConditionsOnProductionInfo info)
		{
			this.info = info;
			ticks = info.Duration;

			if (info.InitialCondition != null)
				token = self.GrantCondition(info.InitialCondition);
		}

		void INotifyProduction.UnitProduced(Actor self, Actor other, CPos exit)
		{
			if (info.Conditions.Count > 0 && !info.Conditions.Select(a => a.Key.ToLowerInvariant()).Contains(other.Info.Name))
				return;

			if (token != Actor.InvalidConditionToken)
				token = self.RevokeCondition(token);
			token = self.GrantCondition(info.Conditions[other.Info.Name]);

			ticks = info.Duration;
		}

		void ITick.Tick(Actor self)
		{
			if (info.Duration >= 0 && token != Actor.InvalidConditionToken && --ticks < 0)
				token = self.RevokeCondition(token);
		}

		float ISelectionBar.GetValue()
		{
			if (!info.ShowSelectionBar || info.Duration < 0 || token == Actor.InvalidConditionToken)
				return 0;

			return (float)ticks / info.Duration;
		}

		Color ISelectionBar.GetColor() { return info.SelectionBarColor; }
		bool ISelectionBar.DisplayWhenEmpty => false;
	}
}
