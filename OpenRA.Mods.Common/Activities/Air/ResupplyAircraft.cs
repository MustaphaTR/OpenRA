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

using System.Linq;
using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class ResupplyAircraft : CompositeActivity
	{
		Activity[] resupplyActivities;

		public ResupplyAircraft(Actor self) { }

		protected override void OnFirstRun(Actor self)
		{
			var aircraft = self.Trait<Aircraft>();
			var host = aircraft.GetSupplierActorBelow();

			if (host == null)
				return;

			if (!aircraft.Info.TakeOffOnResupply)
			{
				resupplyActivities = aircraft.GetResupplyActivities(host)
					.Append(new AllowYieldingReservation(self))
					.Append(new WaitFor(() => NextInQueue != null || aircraft.ReservedActor == null))
					.ToArray();
			}
			else
			{
				resupplyActivities = aircraft.GetResupplyActivities(host)
					.Append(new AllowYieldingReservation(self))
					.Append(new TakeOff(self, (a, b, c) => NextInQueue == null && b.NextInQueue == null))
					.ToArray();
			}
		}

		public override Activity Tick(Actor self)
		{
			if (resupplyActivities == null)
				return NextActivity;

			int cnt = 0;
			for (int i = 0; i < resupplyActivities.Length; i++)
			{
				if (resupplyActivities[i] == null)
					continue;
				resupplyActivities[i] = ActivityUtils.RunActivity(self, resupplyActivities[i]);
				cnt++;
			}

			if (cnt > 0)
				return this;

			return NextActivity;
		}
	}
}
