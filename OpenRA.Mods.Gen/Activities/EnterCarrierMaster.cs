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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Yupgi_alert.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Yupgi_alert.Activities
{
	class EnterCarrierMaster : Enter
	{
		readonly Actor master; // remember the spawner.
		readonly CarrierMaster spawnerMaster;

		public EnterCarrierMaster(Actor self, Actor master, CarrierMaster spawnerMaster, EnterBehaviour enterBehaviour)
			: base(self, Target.FromActor(master), WDist.Zero)
		{
			this.master = master;
			this.spawnerMaster = spawnerMaster;
		}

		protected override void OnEnterComplete(Actor self, Actor targetActor)
		{
			// Master got killed :(
			if (master.IsDead)

			// Load this thingy.
			// Issue attack move to the rally point.
			self.World.AddFrameEndTask(w =>
			{
				if (self.IsDead || master.IsDead)
					return;

				spawnerMaster.PickupSlave(master, self);
				w.Remove(self);

				// Insta repair.
				if (spawnerMaster.Info.InstaRepair)
				{
					var health = self.Trait<Health>();
					self.InflictDamage(self, new Damage(-health.MaxHP));
				}

				// Insta re-arm. (Delayed launching is handled at spawner.)
				var ammoPools = self.TraitsImplementing<AmmoPool>().ToArray();
				if (ammoPools != null)
					foreach (var pool in ammoPools)
						while (pool.GiveAmmo(self, 1)) { }
			});
		}
	}
}
