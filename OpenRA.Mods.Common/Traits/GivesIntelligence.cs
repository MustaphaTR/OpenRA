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
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("This actor activates other player's actors with 'RevealsShroudToIntelligenceOwner' trait to its owner.")]
	public class GivesIntelligenceInfo : ConditionalTraitInfo
	{
		[FieldLoader.Require]
		[Desc("Types of intelligence this actor gives.")]
		public readonly HashSet<string> Types = new HashSet<string>();

		public override object Create(ActorInitializer init) { return new GivesIntelligence(this); }
	}

	public class GivesIntelligence : ConditionalTrait<GivesIntelligenceInfo>, INotifyActorDisposing, INotifyKilled
	{
		public GivesIntelligence(GivesIntelligenceInfo info)
			: base(info) { }

		void RemoveIntelligence(Actor self)
		{
			foreach (var a in self.World.ActorsWithTrait<RevealsShroudToIntelligenceOwner>().Where(rs => rs.Trait.RSTIOInfo.Types.Overlaps(Info.Types) && !rs.Actor.Owner.NonCombatant))
			{
				if (!self.World.ActorsWithTrait<GivesIntelligence>().Where(gi => gi.Actor != self && gi.Actor.Owner == self.Owner && gi.Trait.Info.Types.Overlaps(a.Trait.RSTIOInfo.Types)).Any())
				{
					a.Trait.RemoveCellsFromPlayerShroud(a.Actor, self.Owner);
					a.Trait.IntelOwners.Remove(self.Owner);
				}
			}
		}

		protected override void TraitEnabled(Actor self)
		{
			foreach (var a in self.World.ActorsWithTrait<RevealsShroudToIntelligenceOwner>().Where(rs => rs.Trait.RSTIOInfo.Types.Overlaps(Info.Types) && !rs.Actor.Owner.NonCombatant))
			{
				if (!a.Actor.IsInWorld)
					return;

				if (a.Actor.Owner.NonCombatant)
					return;

				var cells = a.Trait.ProjectedCells(a.Actor);

				a.Trait.RemoveCellsFromPlayerShroud(a.Actor, self.Owner);
				a.Trait.AddCellsToPlayerShroud(a.Actor, self.Owner, cells);
				a.Trait.IntelOwners.Add(self.Owner);
			}
		}

		protected override void TraitDisabled(Actor self)
		{
			RemoveIntelligence(self);
		}

		void INotifyKilled.Killed(Actor self, AttackInfo e)
		{
			RemoveIntelligence(self);
		}

		void INotifyActorDisposing.Disposing(Actor self)
		{
			RemoveIntelligence(self);
		}
	}
}
