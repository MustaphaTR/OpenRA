using System;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Orders
{
	public class EnterSharedTransportTargeter : EnterAlliedActorTargeter<SharedCargoInfo>
	{
		public EnterSharedTransportTargeter(string order, int priority, string enterCursor, string enterBlockedCursor,
			Func<Actor, TargetModifiers, bool> canTarget, Func<Actor, bool> useEnterCursor)
			: base(order, priority, enterCursor, enterBlockedCursor, canTarget, useEnterCursor) { }

		public override bool CanTargetActor(Actor self, Actor target, TargetModifiers modifiers, ref string cursor)
		{
			return base.CanTargetActor(self, target, modifiers, ref cursor);
		}
	}
}
