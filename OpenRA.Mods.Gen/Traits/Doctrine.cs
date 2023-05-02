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
using OpenRA.Traits;

namespace OpenRA.Mods.Yupgi_alert.Traits
{
	[Desc("When created, this actor kills all actors with this trait owned by it's owner.")]
	public class DoctrineInfo : TraitInfo
	{
		[Desc("Type of the doctrine. If empty, it falls back to the actor's type.")]
		public readonly string Type = null;

		public override object Create(ActorInitializer init) { return new Doctrine(init.Self, this); }
	}

	public class Doctrine : INotifyCreated
	{
		public readonly string Type;

		public Doctrine(Actor self, DoctrineInfo info)
		{
			Type = string.IsNullOrEmpty(info.Type) ? self.Info.Name : info.Type;
		}

		void INotifyCreated.Created(Actor self)
		{
			var actors = self.World.ActorsWithTrait<Doctrine>().Where(x => x.Trait.Type == Type && x.Actor.Owner == self.Owner && x.Actor != self);

			foreach (var a in actors)
			{
				if (a.Actor.TraitOrDefault<IHealth>() != null)
					a.Actor.Kill(a.Actor);
				else
					a.Actor.Dispose();
			}
		}
	}
}
