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
using OpenRA.Mods.Common.Effects;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Yupgi_alert.Traits
{
	[Desc("Lets the actor gain experience in a set periodic time.")]
	public class ExperienceTricklerInfo : PausableConditionalTraitInfo, Requires<GainsExperienceInfo>
	{
		[Desc("Number of ticks to wait between giving experience.")]
		public readonly int Interval = 50;

		[Desc("Number of ticks to wait before giving first experience.")]
		public readonly int InitialDelay = 0;

		[Desc("Amount of experience to give each time.")]
		public readonly int Amount = 15;

		public override object Create(ActorInitializer init) { return new ExperienceTrickler(init.Self, this); }
	}

	public class ExperienceTrickler : PausableConditionalTrait<ExperienceTricklerInfo>, ITick, ISync, INotifyCreated
	{
		readonly ExperienceTricklerInfo info;
		GainsExperience gainsExperience;

		[Sync]
		public int Ticks { get; private set; }

		public ExperienceTrickler(Actor self, ExperienceTricklerInfo info)
			: base(info)
		{
			this.info = info;
			Ticks = info.InitialDelay;
			gainsExperience = self.Trait<GainsExperience>();
		}

		void ITick.Tick(Actor self)
		{
			if (IsTraitDisabled)
				Ticks = info.Interval;

			if (IsTraitPaused || IsTraitDisabled)
				return;

			if (--Ticks < 0)
			{
				Ticks = info.Interval;
				gainsExperience.GiveExperience(info.Amount, false);
			}
		}
	}
}
