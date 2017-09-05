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
using System.Collections.Generic;
using System.Linq;
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.BotModules.Squads
{
	public enum SquadType { Assault, Air, Rush, Protection, Naval }

	public class Squad
	{
		public List<Actor> Units = new List<Actor>();
		public SquadType Type;

		internal IBot Bot;
		internal World World;
		internal SquadManagerBotModule SquadManager;
		internal MersenneTwister Random;

		internal Target Target;
		internal StateMachine FuzzyStateMachine;

		public Squad(IBot bot, SquadManagerBotModule squadManager, SquadType type) : this(bot, squadManager, type, null) { }

		public Squad(IBot bot, SquadManagerBotModule squadManager, SquadType type, Actor target)
		{
			Bot = bot;
			SquadManager = squadManager;
			World = bot.Player.PlayerActor.World;
			Random = World.LocalRandom;
			Type = type;
			Target = Target.FromActor(target);
			FuzzyStateMachine = new StateMachine();

			switch (type)
			{
				case SquadType.Assault:
				case SquadType.Rush:
					FuzzyStateMachine.ChangeState(this, new GroundUnitsIdleState(), true);
					break;
				case SquadType.Air:
					FuzzyStateMachine.ChangeState(this, new AirIdleState(), true);
					break;
				case SquadType.Protection:
					FuzzyStateMachine.ChangeState(this, new UnitsForProtectionIdleState(), true);
					break;
				case SquadType.Naval:
					FuzzyStateMachine.ChangeState(this, new NavyUnitsIdleState(), true);
					break;
			}
		}

		public void Update()
		{
			if (IsValid)
				FuzzyStateMachine.Update(this);
		}

		public bool IsValid { get { return Units.Any(); } }

		public Actor TargetActor
		{
			get { return Target.Actor; }
			set { Target = Target.FromActor(value); }
		}

		public bool IsTargetValid
		{
			get { return Target.IsValidFor(Units.FirstOrDefault()) && !Target.Actor.Info.HasTraitInfo<HuskInfo>(); }
		}

		public bool IsTargetVisible
		{
			get { return TargetActor.CanBeViewedByPlayer(Bot.Player); }
		}

		public WPos CenterPosition { get { return Units.Select(u => u.CenterPosition).Average(); } }

		public CPos CenterLocation { get { return World.Map.CellContaining(CenterPosition); } }

		void ReflexAvoidance(Actor attacker)
		{
			// Like when you retract your finger when it touches hot stuff,
			// let air untis avoid the attacker very quickly. (faster than flee state's response)
			WVec vec = CenterPosition - attacker.CenterPosition;
			WPos dest = CenterPosition + vec;
			CPos cdest = World.Map.CellContaining(dest);

			foreach (var a in Units)
				Bot.QueueOrder(new Order("Move", a, false) { TargetLocation = cdest });
		}

		internal void Damage(AttackInfo e)
		{
			// Friendly fire damage can happen, as weapons have spread damage.a
			if (e.Attacker.AppearsFriendlyTo(Bot.Player.PlayerActor))
				return;

			if (Type == SquadType.Air)
			{
				// decide flee or retaliate.
				if (AirStateBase.NearToPosSafely(this, this.CenterPosition))
				{
					TargetActor = e.Attacker;
					FuzzyStateMachine.ChangeState(this, new AirAttackState(), true);
					return;
				}

				// Flee
				ReflexAvoidance(e.Attacker);
				FuzzyStateMachine.ChangeState(this, new AirFleeState(), true);
			}
		}
	}
}
