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

using System.Collections.Generic;
using OpenRA.Activities;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Orders;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("This actor takes of automatically on creation.")]
	public class AutoTakesOffInfo : TraitInfo, Requires<AircraftInfo>
	{
		public override object Create(ActorInitializer init) { return new AutoTakesOff(this); }
	}

	public class AutoTakesOff : INotifyAddedToWorld
	{
		public AutoTakesOff(AutoTakesOffInfo info) { }

		void INotifyAddedToWorld.AddedToWorld(Actor self)
		{
			self.QueueActivity(new FlyIdle(self));
		}
	}
}
