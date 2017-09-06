#region Copyright & License Information
/*
 * Copyright 2007-2017 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Traits;

namespace OpenRA.Mods.Cnc.Traits.Render
{
	class WithDisguisingInfantryBodyInfo : WithInfantryBodyInfo, Requires<DisguiseInfo>
	{
		public override object Create(ActorInitializer init) { return new WithDisguisingInfantryBody(init, this); }
	}

	class WithDisguisingInfantryBody : WithInfantryBody
	{
		readonly WithDisguisingInfantryBodyInfo info;
		readonly Disguise[] disguise;
		readonly RenderSprites rs;
		readonly Disguise activeDisguise;
		string intendedSprite;

		public WithDisguisingInfantryBody(ActorInitializer init, WithDisguisingInfantryBodyInfo info)
			: base(init, info)
		{
			this.info = info;
			rs = init.Self.Trait<RenderSprites>();
			disguise = init.Self.TraitsImplementing<Disguise>().ToArray();
			activeDisguise = disguise.FirstOrDefault(c => !c.IsTraitDisabled);
			intendedSprite = activeDisguise.AsSprite;
		}

		public override void Tick(Actor self)
		{
			if (activeDisguise.AsSprite != intendedSprite)
			{
				intendedSprite = activeDisguise.AsSprite;
				var sequence = DefaultAnimation.GetRandomExistingSequence(info.StandSequences, Game.CosmeticRandom);
				if (sequence != null)
					DefaultAnimation.ChangeImage(intendedSprite ?? rs.GetImage(self), sequence);
				rs.UpdatePalette();
			}

			base.Tick(self);
		}
	}
}
