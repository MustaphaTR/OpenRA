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

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	class ExternalConditionThatRevokedOnCaptureInfo : ExternalConditionInfo, Requires<CapturableInfo>
	{
		public override object Create(ActorInitializer init) { return new ExternalConditionThatRevokedOnCapture(init.Self, this); }
	}

	class ExternalConditionThatRevokedOnCapture : ExternalCondition, INotifyCapture
	{
		public ExternalConditionThatRevokedOnCapture(Actor self, ExternalConditionThatRevokedOnCaptureInfo info)
			: base(self, info) { }

		void INotifyCapture.OnCapture(Actor self, Actor captor, Player oldOwner, Player newOwner, BitSet<CaptureType> captureTypes)
		{
			TryRevokeCondition(self);
		}
	}
}
