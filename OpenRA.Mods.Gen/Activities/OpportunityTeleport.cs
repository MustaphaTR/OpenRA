#region Copyright & License Information
/*
 * Written by Boolbada of OP Mod
 * Follows GPLv3 License as the OpenRA engine:
 *
 * Copyright 2007-2019 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using OpenRA.Activities;
using OpenRA.Mods.Cnc.Activities;
using OpenRA.Mods.Cnc.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Yupgi_alert.Activities
{
	public class OpportunityTeleport : Activity
	{
		public readonly PortableChronoInfo PChronoInfo;
		public readonly PortableChrono PChrono;
		readonly CPos targetCell;

		// moveToDest: activities that will make this actor move to the destination.
		// i.e., Move.
		public OpportunityTeleport(Actor self, PortableChronoInfo pchronoInfo, CPos targetCell, Activity moveToDest)
		{
			this.PChronoInfo = pchronoInfo;
			PChrono = self.Trait<PortableChrono>();
			this.targetCell = targetCell;
			QueueChild(moveToDest);
		}

		public override bool Tick(Actor self)
		{
			if (IsCanceling)
				return true;

			if (PChrono.CanTeleport && (self.Location - targetCell).LengthSquared > 4)
			{
				QueueChild(new Teleport(self, targetCell, null,
					PChronoInfo.KillCargo, PChronoInfo.FlashScreen, PChronoInfo.ChronoshiftSound));
				return false;
			}

			return false;
		}
	}
}
