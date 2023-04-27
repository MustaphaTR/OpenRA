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
using OpenRA.Mods.Common.Effects;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Cnc.Traits
{
	[Desc("This structure can be infiltrated causing a condition to be given itself.")]
	class InfiltrateForConditionInfo : ConditionalTraitInfo
	{
		public readonly BitSet<TargetableType> Types;

		[FieldLoader.Require]
		[GrantedConditionReference]
		[Desc("Condition to grant.")]
		public readonly string Condition = null;

		[Desc("Sound the victim will hear.")]
		public readonly string Notification = null;

		[Desc("How long the condition will last. Use `0` for infinite.")]
		public readonly int Duration = 0;

		public override object Create(ActorInitializer init) { return new InfiltrateForCondition(this); }
	}

	class InfiltrateForCondition : INotifyInfiltrated, ITick, ISync
	{
		readonly InfiltrateForConditionInfo info;
		int token = Actor.InvalidConditionToken;

		[Sync] public int Ticks { get; private set; }
		bool infiltrated = false;

		public InfiltrateForCondition(InfiltrateForConditionInfo info)
		{ 
			this.info = info;
		}

		void ITick.Tick(Actor self)
		{
			if (!infiltrated)
				return;

			if (--Ticks < 0)
			{
				infiltrated = false;

				if (token != Actor.InvalidConditionToken)
					token = self.RevokeCondition(token);
			}
		}

		void INotifyInfiltrated.Infiltrated(Actor self, Actor infiltrator, BitSet<TargetableType> types)
		{
			if (!info.Types.Overlaps(types))
				return;

			if (token == Actor.InvalidConditionToken)
			{
				token = self.GrantCondition(info.Condition);

				if (info.Duration > 0)
					infiltrated = true;
			}

			if (info.Duration > 0)
				Ticks = info.Duration;

			if (info.Notification != null)
				Game.Sound.PlayNotification(self.World.Map.Rules, self.Owner, "Speech", info.Notification, self.Owner.Faction.InternalName);
		}
	}
}
