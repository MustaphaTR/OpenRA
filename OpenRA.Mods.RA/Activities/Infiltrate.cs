#region Copyright & License Information
/*
 * Copyright 2007-2017 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using OpenRA.Activities;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.RA.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.RA.Activities
{
	class Infiltrate : Enter
	{
		readonly Actor target;
		readonly Infiltrates trait;
		readonly Cloak cloak;

		public Infiltrate(Actor self, Actor target, Infiltrates infiltrate)
			: base(self, target, infiltrate.Info.EnterBehaviour)
		{
			this.target = target;
			trait = infiltrate;
			cloak = self.TraitOrDefault<Cloak>();
		}

		protected override void OnInside(Actor self)
		{
			if (target.IsDead)
				return;

			var stance = self.Owner.Stances[target.Owner];
			if (!trait.Info.ValidStances.HasStance(stance))
				return;

			if (cloak != null && cloak.Info.UncloakOn.HasFlag(UncloakType.Infiltrate))
				cloak.Uncloak();

			foreach (var t in target.TraitsImplementing<INotifyInfiltrated>())
				t.Infiltrated(target, self);

			var exp = self.Owner.PlayerActor.TraitOrDefault<PlayerExperience>();
			if (exp != null)
				exp.GiveExperience(trait.Info.PlayerExperience);

			if (!string.IsNullOrEmpty(trait.Info.Notification))
				Game.Sound.PlayNotification(self.World.Map.Rules, self.Owner, "Speech",
					trait.Info.Notification, self.Owner.Faction.InternalName);
		}

		public override Activity Tick(Actor self)
		{
			if (trait.IsTraitDisabled)
				return CanceledTick(self);

			return base.Tick(self);
		}
	}
}
