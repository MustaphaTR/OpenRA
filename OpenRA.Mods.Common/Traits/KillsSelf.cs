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

using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	class KillsSelfInfo : PausableConditionalTraitInfo
	{
		[Desc("Remove the actor from the world (and destroy it) instead of killing it.")]
		public readonly bool RemoveInstead = false;

		[Desc("The amount of time (in ticks) before the actor dies. Two values indicate a range between which a random value is chosen.")]
		public readonly int[] Delay = { 0 };

		[Desc("Types of damage that this trait causes. Leave empty for no damage types.")]
		public readonly BitSet<DamageType> DamageTypes = default(BitSet<DamageType>);

		[GrantedConditionReference]
		[Desc("The condition to grant moments before suiciding.")]
		public readonly string GrantsCondition = null;

		public override object Create(ActorInitializer init) { return new KillsSelf(init.Self, this); }
	}

	class KillsSelf : PausableConditionalTrait<KillsSelfInfo>, INotifyAddedToWorld, ITick
	{
		int lifetime;
		ConditionManager conditionManager;

		public KillsSelf(Actor self, KillsSelfInfo info)
			: base(info) { }

		protected override void TraitEnabled(Actor self)
		{
			lifetime = Util.RandomDelay(self.World, Info.Delay);

			// Actors can be created without being added to the world
			// We want to make sure that this only triggers once they are inserted into the world
			if (lifetime == 0 && self.IsInWorld)
				self.World.AddFrameEndTask(w => Kill(self));
		}

		protected override void Created(Actor self)
		{
			conditionManager = self.TraitOrDefault<ConditionManager>();
			base.Created(self);
		}

		void INotifyAddedToWorld.AddedToWorld(Actor self)
		{
			if (lifetime == 0 && !IsTraitDisabled && !IsTraitPaused)
				self.World.AddFrameEndTask(w => Kill(self));
		}

		void ITick.Tick(Actor self)
		{
			if (!self.IsInWorld || self.IsDead || IsTraitDisabled || IsTraitPaused)
				return;

			if (!self.World.Map.Contains(self.Location))
				return;

			if (lifetime-- <= 0)
				self.World.AddFrameEndTask(w => Kill(self));
		}

		void Kill(Actor self)
		{
			if (self.IsDead)
				return;

			if (conditionManager != null && !string.IsNullOrEmpty(Info.GrantsCondition))
				conditionManager.GrantCondition(self, Info.GrantsCondition);

			if (Info.RemoveInstead || !self.Info.HasTraitInfo<IHealthInfo>())
				self.Dispose();
			else
				self.Kill(self, Info.DamageTypes);
		}
	}
}
