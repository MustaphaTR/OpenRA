#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.AS.Widgets.Logic
{
	public class ExperienceMeterWidget : Widget
	{
		readonly World world;

		[FieldLoader.Require]
		[Desc("Reset the experience bar at these levels. When the last level is reached the bar stays full.")]
		public int[] Levels;

		public string Background = "progressbar-bg";
		public string ImageName = "progressbar-thumb-yellow";
		public Size BarMargin = new(2, 2);

		[Desc("Experience bar is horizontal rather than vertical.")]
		public readonly bool Horizontal = false;

		[ObjectCreator.UseCtor]
		public ExperienceMeterWidget(World world, WorldRenderer worldRenderer)
		{
			this.world = world;
		}

		public override void Draw()
		{
			var rb = RenderBounds;
			WidgetUtils.DrawPanel(Background, rb);

			var percentage = GetPercentage();

			if (Horizontal)
			{
				var minBarWidth = ChromeProvider.GetMinimumPanelSize(ImageName).Width;
				var maxBarWidth = rb.Width - BarMargin.Width * 2;
				var barWidth = percentage * maxBarWidth / 100;
				barWidth = Math.Max(barWidth, minBarWidth);

				var barRect = new Rectangle(rb.X + BarMargin.Width, rb.Y + BarMargin.Height, barWidth, rb.Height - 2 * BarMargin.Height);
				WidgetUtils.DrawPanel(ImageName, barRect);
			}
			else
			{
				var minBarHeight = ChromeProvider.GetMinimumPanelSize(ImageName).Height;
				var maxBarHeight = rb.Height - BarMargin.Height * 2;
				var barHeight = percentage * maxBarHeight / 100;
				barHeight = Math.Max(barHeight, minBarHeight);
				var barY = Bounds.Height - barHeight;

				var barRect = new Rectangle(rb.X + BarMargin.Width, rb.Y + BarMargin.Height + barY, rb.Width - 2 * BarMargin.Width, barHeight);
				WidgetUtils.DrawPanel(ImageName, barRect);
			}
		}

		int GetPercentage()
		{
			if (world.LocalPlayer == null)
				return 0;

			var xpValue = world.LocalPlayer.PlayerActor.TraitOrDefault<PlayerExperience>().Experience;
			var maxXpValue = Levels.FirstOrDefault(l => l > xpValue);
			if (maxXpValue == 0)
				return 100;

			return 100 - (int)((float)(maxXpValue - xpValue) / maxXpValue * 100);
		}
	}
}
