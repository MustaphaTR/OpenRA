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

using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Yupgi_alert.Traits
{
	[Desc("Applies a condition to the actor at when health is less or equal to a specific value.")]
	public class GrantConditionOnHealthInfo : ITraitInfo, Requires<HealthInfo>
	{
		[FieldLoader.Require]
		[GrantedConditionReference]
		[Desc("Condition to grant.")]
		public readonly string Condition = null;

		[Desc("Play a random sound from this list when enabled.")]
		public readonly string[] EnabledSounds = { };

		[Desc("Play a random sound from this list when disabled.")]
		public readonly string[] DisabledSounds = { };

		[Desc("Levels of health at which to grant the condition.")]
		public readonly int HP = 50;

		[Desc("Is the condition irrevocable once it has been activated?")]
		public readonly bool GrantPermanently = false;

		public object Create(ActorInitializer init) { return new GrantConditionOnHealth(init.Self, this); }
	}

	public class GrantConditionOnHealth : INotifyCreated, INotifyDamage
	{
		readonly GrantConditionOnHealthInfo info;
		readonly Health health;

		ConditionManager conditionManager;
		int conditionToken = ConditionManager.InvalidConditionToken;

		public GrantConditionOnHealth(Actor self, GrantConditionOnHealthInfo info)
		{
			this.info = info;
			health = self.Trait<Health>();
		}

		void INotifyCreated.Created(Actor self)
		{
			conditionManager = self.TraitOrDefault<ConditionManager>();
			GrantConditionOnValidHealth(self, health.HP);
		}

		void GrantConditionOnValidHealth(Actor self, int hp)
		{
			if (info.HP < hp || conditionToken != ConditionManager.InvalidConditionToken)
				return;

			conditionToken = conditionManager.GrantCondition(self, info.Condition);

			var sound = info.EnabledSounds.RandomOrDefault(Game.CosmeticRandom);
			Game.Sound.Play(SoundType.World, sound, self.CenterPosition);
		}

		void INotifyDamage.Damaged(Actor self, AttackInfo e)
		{
			var granted = conditionToken != ConditionManager.InvalidConditionToken;
			if ((granted && info.GrantPermanently) || conditionManager == null)
				return;

			if (!granted)
				GrantConditionOnValidHealth(self, health.HP);
			else if (granted && info.HP < health.HP)
			{
				conditionToken = conditionManager.RevokeCondition(self, conditionToken);

				var sound = info.DisabledSounds.RandomOrDefault(Game.CosmeticRandom);
				Game.Sound.Play(SoundType.World, sound, self.CenterPosition);
			}
		}
	}
}
