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

using System.Linq;
using Eluant;
using OpenRA.Mods.Cnc.Traits;
using OpenRA.Scripting;
using OpenRA.Traits;

namespace OpenRA.Mods.Cnc.Scripting
{
	[ScriptPropertyGroup("Ability")]
	public class DisguiseProperties : ScriptActorProperties, Requires<DisguiseInfo>
	{
		readonly Disguise[] disguise;

		public DisguiseProperties(ScriptContext context, Actor self)
			: base(context, self)
		{
			disguise = Self.TraitsImplementing<Disguise>().ToArray();
		}

		[ScriptContextAttribute(ScriptContextType.Mission)]
		[Desc("Disguises as the target actor.")]
		public void DisguiseAs(Actor target)
		{
			if (disguise.Any(x => !x.IsTraitDisabled))
			{
				var activeDisguise = disguise.FirstOrDefault(c => !c.IsTraitDisabled);

				activeDisguise.DisguiseAs(target);
			}
			else
				throw new LuaException("Actor '{0}' cannot disguise as actor '{1}'!".F(Self, target));
		}

		[ScriptContext(ScriptContextType.Mission)]
		[Desc("Disguises as the target type with the specified owner.")]
		public void DisguiseAsType(string actorType, Player newOwner)
		{
			if (disguise.Any(x => !x.IsTraitDisabled))
			{
				var activeDisguise = disguise.FirstOrDefault(c => !c.IsTraitDisabled);
				var actorInfo = Self.World.Map.Rules.Actors[actorType];

				activeDisguise.DisguiseAs(actorInfo, newOwner);
			}
			else
				throw new LuaException("Actor '{0}' cannot disguise as actor type '{1}'!".F(Self, actorType));
		}
	}
}
