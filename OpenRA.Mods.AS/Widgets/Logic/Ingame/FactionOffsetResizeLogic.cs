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

using System.Collections.Generic;
using OpenRA.Widgets;

namespace OpenRA.Mods.AS.Widgets.Logic
{
	public class FactionOffsetResizeLogic : ChromeLogic
	{
		[ObjectCreator.UseCtor]
		public FactionOffsetResizeLogic(Widget widget, World world, Dictionary<string, MiniYaml> logicArgs)
		{
			if (world.LocalPlayer == null || world.LocalPlayer.Spectating)
				return;

			if (!ChromeMetrics.TryGet("FactionSuffix-" + world.LocalPlayer.Faction.InternalName, out string faction))
				faction = world.LocalPlayer.Faction.InternalName;

			var offsetKey = "Offset-" + faction;
			if (logicArgs.TryGetValue(offsetKey, out var offset))
			{
				var offsetValue = FieldLoader.GetValue<int2>(offsetKey, offset.Value);
				widget.Bounds.X += offsetValue.X;
				widget.Bounds.Y += offsetValue.Y;
			}

			var resizeKey = "Resize-" + faction;
			if (logicArgs.TryGetValue(resizeKey, out var resize))
			{
				var resizeValue = FieldLoader.GetValue<int2>(resizeKey, resize.Value);
				widget.Bounds.Width += resizeValue.X;
				widget.Bounds.Height += resizeValue.Y;
			}
		}
	}
}
