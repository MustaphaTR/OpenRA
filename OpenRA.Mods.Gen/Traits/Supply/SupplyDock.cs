#region Copyright & License Information
/*
 * Copyright 2007-2019 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Yupgi_alert.Traits
{
	public class SupplyDockInfo : ITraitInfo
	{
		public readonly HashSet<string> SupplyTypes = new HashSet<string> { "supply" };

		[Desc("How much supplies this actor carries.")]
		public readonly int Capacity = 30000;

		[FieldLoader.Require]
		[Desc("Where can the supply collectors can collect the supplies from.")]
		public readonly CVec[] CollectionOffsets = new CVec[] { };

		[Desc("Where can the aircraft supply collectors can collect the supplies from.")]
		public readonly CVec[] AircraftCollectionOffsets = new CVec[] { };

		[Desc("Collector faces this way before taking the supplies; if -1, faces towards the center of dock.")]
		public readonly int Facing = -1;

		[Desc("Conditions to grant when dock has more than specified amount of supplies.",
			"A dictionary of [integer]: [condition].")]
		public readonly Dictionary<int, string> FullnessConditions = new Dictionary<int, string>();

		[GrantedConditionReference]
		public IEnumerable<string> LinterFullnessConditions { get { return FullnessConditions.Values; } }

		public object Create(ActorInitializer init) { return new SupplyDock(init.Self, this); }
	}

	public class SupplyDock : IProvideTooltipInfo, INotifyCreated
	{
		public readonly SupplyDockInfo Info;
		public int Amount;

		ConditionManager conditionManager;
		readonly Dictionary<int, int> fullnessTokens = new Dictionary<int, int>();

		public SupplyDock(Actor self, SupplyDockInfo info)
		{
			Info = info;
			Amount = Info.Capacity;
		}

		void INotifyCreated.Created(Actor self)
		{
			conditionManager = self.TraitOrDefault<ConditionManager>();
			CheckConditions(self);
		}

		public bool IsFull { get { return Amount == Info.Capacity; } }
		public bool IsEmpty { get { return Amount == 0; } }
		public int Fullness { get { return Amount * 100 / Info.Capacity; } }

		public void CheckConditions(Actor self)
		{
			if (conditionManager != null)
			{
				foreach (var pair in Info.FullnessConditions)
				{
					if (Amount >= pair.Key && !fullnessTokens.ContainsKey(pair.Key))
						fullnessTokens.Add(pair.Key, conditionManager.GrantCondition(self, pair.Value));

					int fullnessToken;
					if (Amount < pair.Key && fullnessTokens.TryGetValue(pair.Key, out fullnessToken))
					{
						conditionManager.RevokeCondition(self, fullnessToken);
						fullnessTokens.Remove(pair.Key);
					}
				}
			}
		}

		public bool IsTooltipVisible(Player forPlayer) { return true; }

		public string TooltipText
		{
			get
			{
				return "$" + Amount;
			}
		}
	}
}
