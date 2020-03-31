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

using System;
using System.Collections.Generic;
using OpenRA.Traits;

namespace OpenRA.Mods.Yupgi_alert.Orders
{
	public class GenericTargeter<T> : IOrderTargeter where T : ITraitInfo
	{
		readonly Func<Actor, bool> canTarget;
		readonly Func<Actor, string> useCursor;

		public GenericTargeter(string order, int priority, Func<Actor, bool> canTarget, Func<Actor, string> useCursor)
		{
			OrderID = order;
			OrderPriority = priority;
			this.canTarget = canTarget;
			this.useCursor = useCursor;
		}

		public string OrderID { get; private set; }
		public int OrderPriority { get; private set; }

		public bool CanTarget(Actor self, Target target, List<Actor> othersAtTarget, ref TargetModifiers modifiers, ref string cursor)
		{
			var type = target.Type;
			if (type != TargetType.Actor && type != TargetType.FrozenActor)
				return false;

			IsQueued = modifiers.HasModifier(TargetModifiers.ForceQueue);

			var actor = type == TargetType.FrozenActor ? target.FrozenActor.Actor : target.Actor;
			var owner = actor.Owner;
			var playerRelationship = self.Owner.Stances[owner];

			return CanTargetActor(self, actor, modifiers, ref cursor);
		}

		public virtual bool IsQueued { get; protected set; }

		public bool TargetOverridesSelection(TargetModifiers modifiers) { return true; }

		public bool CanTargetActor(Actor self, Actor target, TargetModifiers modifiers, ref string cursor)
		{
			if (!target.Info.HasTraitInfo<T>() || !canTarget(target))
				return false;

			cursor = useCursor(target);
			return true;
		}
	}
}
