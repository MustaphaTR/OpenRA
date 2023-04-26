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
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Commands
{
	[Desc("Enables developer cheats via the chatbox. Attach this to the world actor.")]
	public class DevCommandsInfo : TraitInfo<DevCommands> { }

	public class DevCommands : IChatCommand, IWorldLoaded
	{
		World world;
		DeveloperMode developerMode;

		public void WorldLoaded(World w, WorldRenderer wr)
		{
			world = w;

			if (world.LocalPlayer != null)
				developerMode = world.LocalPlayer.PlayerActor.Trait<DeveloperMode>();

			var console = world.WorldActor.Trait<ChatCommands>();
			var help = world.WorldActor.Trait<HelpCommand>();

			Action<string, string> register = (name, helpText) =>
			{
				console.RegisterCommand(name, this);
				help.RegisterHelp(name, helpText);
			};

			register("visibility", "toggles visibility checks and minimap.");
			register("givecash", "gives the default or specified amount of money.");
			register("givecashall", "gives the default or specified amount of money to all players and ai.");
			register("instantbuild", "toggles instant building.");
			register("instantbuildall", "toggles instant building for all players.");
			register("buildanywhere", "toggles you the ability to build anywhere.");
			register("buildanywhereall", "toggles you the ability to build anywhere for all players.");
			register("unlimitedpower", "toggles infinite power.");
			register("unlimitedpowerall", "toggles infinite power for all players.");
			register("enabletech", "toggles the ability to build everything.");
			register("enabletechall", "toggles the ability to build everything for all players.");
			register("instantcharge", "toggles instant support power charging.");
			register("instantchargeall", "toggles instant support power charging for all players.");
			register("all", "toggles all cheats and gives you some cash for your trouble.");
			register("allforall", "toggles all cheats for all players and gives everyone some cash for their troubles.");
			register("crash", "crashes the game.");
			register("levelup", "adds a specified number of levels to the selected actors.");
			register("playerexperience", "adds a specified amount of player experience to the owner(s) of selected actors.");
			register("poweroutage", "causes owner(s) of selected actors to have a 5 second power outage.");
			register("kill", "kills selected actors.");
			register("dispose", "disposes selected actors.");
			register("produce", "makes the selected actor produce given actor.");
			register("clearresources", "removes all the ore from the map.");
		}

		public void InvokeCommand(string name, string arg)
		{
			if (world.LocalPlayer == null)
				return;

			if (!developerMode.Enabled)
			{
				Game.Debug("Cheats are disabled.");
				return;
			}

			switch (name)
			{
				case "givecash": IssueGiveCashDevCommand(false, arg); break;
				case "givecashall": IssueGiveCashDevCommand(true, arg); break;
				case "visibility": IssueDevCommand(world, "DevVisibility"); break;
				case "instantbuild": IssueDevCommand(world, "DevFastBuild"); break;
				case "buildanywhere": IssueDevCommand(world, "DevBuildAnywhere"); break;
				case "unlimitedpower": IssueDevCommand(world, "DevUnlimitedPower"); break;
				case "enabletech": IssueDevCommand(world, "DevEnableTech"); break;
				case "instantcharge": IssueDevCommand(world, "DevFastCharge"); break;
				case "clearresources": IssueDevCommand(world, "DevClearResources"); break;

				case "visibilityall":
					foreach (var player in world.Players.Where(p => !p.NonCombatant))
					{
						world.IssueOrder(new Order("DevVisibility", player.PlayerActor, false));
					}

					break;

				case "instantbuildall":
					foreach (var player in world.Players.Where(p => !p.NonCombatant))
					{
						world.IssueOrder(new Order("DevFastBuild", player.PlayerActor, false));
					}

					break;

				case "buildanywhereall":
					foreach (var player in world.Players.Where(p => !p.NonCombatant))
					{
						world.IssueOrder(new Order("DevBuildAnywhere", player.PlayerActor, false));
					}

					break;

				case "unlimitedpowerall":
					foreach (var player in world.Players.Where(p => !p.NonCombatant))
					{
						world.IssueOrder(new Order("DevUnlimitedPower", player.PlayerActor, false));
					}

					break;

				case "enabletechall":
					foreach (var player in world.Players.Where(p => !p.NonCombatant))
					{
						world.IssueOrder(new Order("DevEnableTech", player.PlayerActor, false));
					}

					break;

				case "instantchargeall":
					foreach (var player in world.Players.Where(p => !p.NonCombatant))
					{
						world.IssueOrder(new Order("DevFastCharge", player.PlayerActor, false));
					}

					break;

				case "all":
					IssueDevCommand(world, "DevAll");
					break;

				case "allforall":
					foreach (var player in world.Players.Where(p => !p.NonCombatant))
					{
						world.IssueOrder(new Order("DevAll", player.PlayerActor, false));
					}

					break;

				case "crash":
					throw new DevException();

				case "levelup":
					var level = 0;
					int.TryParse(arg, out level);

					foreach (var actor in world.Selection.Actors)
					{
						if (actor.IsDead || actor.Disposed)
							continue;

						var leveluporder = new Order("DevLevelUp", actor, false);
						leveluporder.ExtraData = (uint)level;

						if (actor.Info.HasTraitInfo<GainsExperienceInfo>())
							world.IssueOrder(leveluporder);
					}

					break;

				case "playerexperience":
					var experience = 0;
					int.TryParse(arg, out experience);

					foreach (var player in world.Selection.Actors.Select(a => a.Owner.PlayerActor).Distinct())
						world.IssueOrder(new Order("DevPlayerExperience", player, false) { ExtraData = (uint)experience });
					break;

				case "poweroutage":
					foreach (var player in world.Selection.Actors.Select(a => a.Owner.PlayerActor).Distinct())
						world.IssueOrder(new Order("PowerOutage", player, false) { ExtraData = 250 });
					break;

				case "kill":
					foreach (var actor in world.Selection.Actors)
					{
						if (actor.IsDead)
							continue;

						world.IssueOrder(new Order("DevKill", world.LocalPlayer.PlayerActor, Target.FromActor(actor), false) { TargetString = arg });
					}

					break;

				case "dispose":
					foreach (var actor in world.Selection.Actors)
					{
						if (actor.Disposed)
							continue;

						world.IssueOrder(new Order("DevDispose", world.LocalPlayer.PlayerActor, Target.FromActor(actor), false));
					}

					break;

				case "produce":
					foreach (var actor in world.Selection.Actors)
					{
						if (actor.IsDead)
							continue;

						world.IssueOrder(new Order("DevProduce", world.LocalPlayer.PlayerActor, Target.FromActor(actor), false) { TargetString = arg });
					}

					break;
			}
		}

		void IssueGiveCashDevCommand(bool toAll, string arg)
		{
			var orderString = toAll ? "DevGiveCashAll" : "DevGiveCash";
			var giveCashOrder = new Order(orderString, world.LocalPlayer.PlayerActor, false);

			int.TryParse(arg, out var cash);
			giveCashOrder.ExtraData = (uint)cash;

			world.IssueOrder(giveCashOrder);
		}

		static void IssueDevCommand(World world, string command)
		{
			world.IssueOrder(new Order(command, world.LocalPlayer.PlayerActor, false));
		}

		[Serializable]
		class DevException : Exception { }
	}
}
