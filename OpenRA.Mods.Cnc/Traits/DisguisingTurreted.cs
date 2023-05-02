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

using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Cnc.Traits.Render
{
	class DisguisingTurretedInfo : TurretedInfo, Requires<DisguiseInfo>
	{
		public override object Create(ActorInitializer init) { return new DisguisingTurreted(init, this); }
	}

	class DisguisingTurreted : Turreted
	{
		readonly Disguise disguise;
		WVec intendedTurretOffset;

		public DisguisingTurreted(ActorInitializer init, DisguisingTurretedInfo info)
			: base(init, info)
		{
			disguise = init.Self.Trait<Disguise>();
			intendedTurretOffset = disguise.TurretOffsets.First();
		}

		protected override void Tick(Actor self)
		{
			/* if (disguise.TurretOffsets.First() != intendedTurretOffset)
			{
				intendedTurretOffset = disguise.TurretOffsets.First();
				DisguiseOffset = intendedTurretOffset;
			} */

			base.Tick(self);
		}
	}
}
