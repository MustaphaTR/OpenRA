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

using OpenRA.Mods.RA.Activities;
using OpenRA.Mods.RA.Traits;
using OpenRA.Scripting;
using OpenRA.Traits;
using System.Collections.Generic;
using System.Linq;

namespace OpenRA.Mods.RA.Scripting
{
	[ScriptPropertyGroup("Ability")]
	public class InfiltrateProperties : ScriptActorProperties, Requires<InfiltratesInfo>
	{
		public InfiltrateProperties(ScriptContext context, Actor self)
			: base(context, self) { }

		[Desc("Infiltrate the target actor.")]
		public void Infiltrate(Actor target)
		{
			var trait = Self.TraitsImplementing<Infiltrates>().FirstOrDefault(x => !x.IsTraitDisabled && x.Info.Types.Overlaps(target.GetEnabledTargetTypes()));

			if (trait == null)
			{
				Log.Write("lua", "{0} tried to infiltrate invalid target {1}!", Self, target);
				return;
			}

			Self.QueueActivity(new Infiltrate(Self, target, trait));
		}
	}
}
