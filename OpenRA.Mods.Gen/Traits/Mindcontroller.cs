#region Copyright & License Information
/*
 * Copyright 2015- OpenRA.Mods.AS Developers (see AUTHORS)
 * This file is a part of a third-party plugin for OpenRA, which is
 * free software. It is made available to you under the terms of the
 * GNU General Public License as published by the Free Software
 * Foundation. For more information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

/*
 * Works without base engine modification.
 * Mindcontroller is assumed that they aren't mindcontrollable!
 */

namespace OpenRA.Mods.Yupgi_alert.Traits
{
	public enum MindControlPolicy
	{
		NewOneUnaffected, // Like Yuri's MC tower. Best if you use ControllingCondition to forbid the dummy weapon from firing too.
		DiscardOldest, // Like Yuri Clone
		HyperControl // Like Yuri Master Mind
	}

	// No permanent MC support though, I think it is better to make a separate module based on this.
	// All you need to do is to delete all complex code and leave ownership transfer code only.
	[Desc("Can mind control other units?")]
	public class MindControllerInfo : ConditionalTraitInfo, Requires<ArmamentInfo>, Requires<HealthInfo>
	{
		[WeaponReference]
		[Desc("The name of the weapon, one of its armament. Must be specified with \"Name:\" field.",
			"To limit mind controllable targets, adjust the weapon's valid target filter.")]
		public readonly string Name = "primary";

		[Desc("Up to how many units can this unit control?")]
		public readonly int Capacity = 1;

		[Desc("Can this unit MC beyond Capacity temporarily?")]
		public readonly MindControlPolicy Policy = MindControlPolicy.DiscardOldest;

		[Desc("Condition to grant to self when controlling actors. Can stack up by the number of enslaved actors. You can use this to forbid firing of the dummy MC weapon.")]
		[GrantedConditionReference]
		public readonly string ControllingCondition;

		[Desc("Damage taken if hyper controlling beyond capacity.")]
		public readonly int HyperControlDamage = 2;

		[Desc("Interval of applying hyper control damage")]
		public readonly int HyperControlDamageInterval = 25;

		[Desc("The sound played when the unit is mindcontrolled.")]
		public readonly string[] Sound = null;

		public override object Create(ActorInitializer init) { return new MindController(init.Self, this); }
	}

	class MindController : ConditionalTrait<MindControllerInfo>, INotifyAttack, INotifyKilled, INotifyActorDisposing, ITick
	{
		readonly MindControllerInfo info;
		readonly Health health;
		readonly List<Actor> slaves = new List<Actor>();

		int ticks;
		Stack<int> controllingTokens = new Stack<int>();

		public IEnumerable<Actor> Slaves { get { return slaves; } }

		public MindController(Actor self, MindControllerInfo info)
			: base(info)
		{
			this.info = info;
			health = self.Trait<Health>();

			var armaments = self.TraitsImplementing<Armament>().Where(a => a.Info.Name == info.Name).ToArray();
			System.Diagnostics.Debug.Assert(armaments.Length == 1, "Multiple armaments with given name detected: " + info.Name);
		}

		void StackControllingCondition(Actor self, string condition)
		{
			if (string.IsNullOrEmpty(condition))
				return;

			controllingTokens.Push(self.GrantCondition(condition));
		}

		void UnstackControllingCondition(Actor self, string condition)
		{
			if (string.IsNullOrEmpty(condition))
				return;

			self.RevokeCondition(controllingTokens.Pop());
		}

		// Unlink a dead or mind-controlled-by-somebody-else slave.
		public void UnlinkSlave(Actor self, Actor slave)
		{
			if (slaves.Contains(slave))
			{
				slaves.Remove(slave);
				UnstackControllingCondition(self, info.ControllingCondition);
			}
		}

		void INotifyAttack.Attacking(Actor self, in Target target, Armament a, Barrel barrel)
		{
			// Only specified MC weapon can do mind control.
			if (info.Name != a.Info.Name)
				return;

			// Must target an actor.
			if (target.Actor == null || !target.IsValidFor(self))
				return;

			// Don't allow ally mind control
			if (self.Owner.RelationshipWith(target.Actor.Owner) == PlayerRelationship.Ally)
				return;

			var mcable = target.Actor.TraitOrDefault<MindControllable>();

			// For some reason the weapon is valid for targeting but the actor doesn't actually have
			// mindcontrollable trait.
			if (mcable == null)
			{
				Game.Debug("Warning: mindcontrol weapon targetable unit doesn't actually have mindcontrallable trait");
				return;
			}

			if (info.Policy == MindControlPolicy.NewOneUnaffected && slaves.Count() >= info.Capacity)
				return;

			// At this point, the target should be mind controlled. How we manage them is another thing.
			slaves.Add(target.Actor);
			StackControllingCondition(self, info.ControllingCondition);
			mcable.LinkMaster(target.Actor, self);

			// Play sound
			if (info.Sound != null && info.Sound.Any())
				Game.Sound.Play(SoundType.World, info.Sound.Random(self.World.SharedRandom), self.CenterPosition);

			// Let's evict the oldest one, if no hyper control.
			if (info.Policy == MindControlPolicy.DiscardOldest && slaves.Count() > info.Capacity)
				slaves[0].Trait<MindControllable>().UnMindcontrol(slaves[0], self.Owner);

			// If can hyper control, nothing to do.
			// Tick() will do the rest.
		}

		void INotifyAttack.PreparingAttack(Actor self, in Target target, Armament a, Barrel barrel) { }

		void ReleaseSlaves(Actor self)
		{
			var toUnMC = slaves.ToArray(); // UnMincdontrol modifies slaves list.
			foreach (var s in toUnMC)
			{
				if (s.IsDead || s.Disposed)
					continue;

				s.Trait<MindControllable>().UnMindcontrol(s, self.Owner);
			}
		}

		void INotifyKilled.Killed(Actor self, AttackInfo e)
		{
			ReleaseSlaves(self);
		}

		void INotifyActorDisposing.Disposing(Actor self)
		{
			ReleaseSlaves(self);
		}

		void ITick.Tick(Actor self)
		{
			if (info.Policy != MindControlPolicy.HyperControl)
				return;

			if (slaves.Count() <= info.Capacity)
				return;

			if (ticks-- > 0)
				return;

			ticks = info.HyperControlDamageInterval;
			health.InflictDamage(self, self, new Damage(info.HyperControlDamage), true);
		}
	}
}
