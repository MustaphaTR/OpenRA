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
using OpenRA.GameRules;
using OpenRA.Mods.Yupgi_alert.Warheads;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Yupgi_alert.Traits
{
	public class DelayedWeaponTrigger
	{
		public readonly BitSet<DamageType> DeathTypes;

		public readonly int TriggerTime;

		public int RemainingTime { get; private set; }

		public Actor AttachedBy { get; private set; }

		private WeaponInfo weaponInfo;

		public bool IsValid { get; private set; }

		public DelayedWeaponTrigger(AttachDelayedWeaponWarhead warhead, Actor attachedBy)
		{
			TriggerTime = warhead.TriggerTime;
			RemainingTime = TriggerTime;
			DeathTypes = warhead.DeathTypes;
			weaponInfo = warhead.WeaponInfo;
			AttachedBy = attachedBy;
			IsValid = true;
		}

		public void Tick(Actor attachable)
		{
			RemainingTime--;
			if (!attachable.IsDead && attachable.IsInWorld && IsValid)
			{
				if (RemainingTime == 0)
				{
					Activate(attachable);
				}
			}
		}

		public void Activate(Actor attachable)
		{
			IsValid = false;
			var target = Target.FromPos(attachable.CenterPosition);
			attachable.World.AddFrameEndTask(w => weaponInfo.Impact(target, AttachedBy));
		}

		public void Deactivate()
		{
			IsValid = false;
		}
	}
}
