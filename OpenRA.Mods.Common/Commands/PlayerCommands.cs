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
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Commands
{
	[Desc("Allows the player to pause or surrender the game via the chatbox. Attach this to the world actor.")]
	public class PlayerCommandsInfo : TraitInfo<PlayerCommands> { }

	public class PlayerCommands : IChatCommand, IWorldLoaded
	{
		World world;
		Taunts taunts;

		public void WorldLoaded(World w, WorldRenderer wr)
		{
			world = w;

			if (world.LocalPlayer != null)
				taunts = world.LocalPlayer.PlayerActor.Trait<Taunts>();

			var console = world.WorldActor.Trait<ChatCommands>();
			var help = world.WorldActor.Trait<HelpCommand>();

			Action<string, string> register = (name, helpText) =>
			{
				console.RegisterCommand(name, this);
				help.RegisterHelp(name, helpText);
			};

			register("pause", "pause or unpause the game");
			register("surrender", "self-destruct everything and lose the game");
			register("taunt", "plays a taunt");
		}

		public void InvokeCommand(string name, string arg)
		{
			switch (name)
			{
				case "pause":
					if (Game.IsHost || (world.LocalPlayer != null && world.LocalPlayer.WinState != WinState.Lost))
						world.SetPauseState(!world.Paused);

					break;
				case "surrender":
					if (world.LocalPlayer != null)
						world.IssueOrder(new Order("Surrender", world.LocalPlayer.PlayerActor, false));

					break;
				case "taunt":
					if (!taunts.Enabled)
					{
						Game.Debug("Taunts are disabled.");
						return;
					}

					if (world.LocalPlayer != null)
						world.IssueOrder(new Order("Taunt", world.LocalPlayer.PlayerActor, false) { TargetString = arg });

					break;
			}
		}
	}
}
