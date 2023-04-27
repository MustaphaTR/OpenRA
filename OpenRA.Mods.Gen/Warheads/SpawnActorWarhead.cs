#region Copyright & License Information
/*
 * Copyright 2015- OpenRA.Mods.AS Developers (see AUTHORS)
 * This file is a part of a third-party plugin for OpenRA, which is
 * free software. It is made available to you under the terms of the
 * GNU General Public License as published by the Free Software
 * Foundation. For more information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using OpenRA.GameRules;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Warheads;
using OpenRA.Mods.Yupgi_alert.Activities;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Yupgi_alert.Warheads
{
	[Desc("Spawn actors upon explosion.")]
	public class SpawnActorWarhead : WarheadAS
	{
		[Desc("The cell range to try placing the actors within.")]
		public readonly int Range = 10;

		[Desc("Actors to spawn.")]
		public readonly string[] Actors = { };

		[Desc("Try to parachute the actors. When unset, actors will just fall down visually using FallRate."
			+ " Requires the Parachutable trait on all actors if set.")]
		public readonly bool Paradrop = false;

		public readonly int FallRate = 130;

		[Desc("Always spawn the actors on the ground.")]
		public readonly bool ForceGround = false;

		[Desc("Map player to give the actors to. Defaults to the firer.")]
		public readonly string Owner = null;

		public override void DoImpact(in Target target, WarheadArgs args)
		{
			var firedBy = args.SourceActor;
			var map = firedBy.World.Map;
			var targetCell = map.CellContaining(target.CenterPosition);

			if (!IsValidImpact(target.CenterPosition, firedBy))
				return;

			var targetCells = map.FindTilesInCircle(targetCell, Range);
			var cell = targetCells.GetEnumerator();

			foreach (var a in Actors)
			{
				var placed = false;
				var td = new TypeDictionary();
				var ai = map.Rules.Actors[a.ToLowerInvariant()];

				if (Owner == null)
					td.Add(new OwnerInit(firedBy.Owner));
				else
					td.Add(new OwnerInit(firedBy.World.Players.First(p => p.InternalName == Owner)));

				// HACK HACK HACK
				// Immobile does not offer a check directly if the actor can exist in a position.
				// It also crashes the game if it's actor's created without a LocationInit.
				// See AS/Engine#84.
				if (ai.HasTraitInfo<ImmobileInfo>())
				{
					var immobileInfo = ai.TraitInfo<ImmobileInfo>();

					while (cell.MoveNext())
					{
						if (!immobileInfo.OccupiesSpace || !firedBy.World.ActorMap.GetActorsAt(cell.Current).Any())
						{
							firedBy.World.AddFrameEndTask(w =>
							{
								td.Add(new LocationInit(cell.Current));
								var immobileunit = firedBy.World.CreateActor(false, a.ToLowerInvariant(), td);

								w.Add(immobileunit);
							});

							break;
						}
					}

					continue;
				}

				// Lambdas can't use 'in' variables, so capture a copy for later
				var delayedTarget = target;

				firedBy.World.AddFrameEndTask(w =>
				{
					var unit = firedBy.World.CreateActor(false, a.ToLowerInvariant(), td);
					var positionable = unit.TraitOrDefault<IPositionable>();
					cell = targetCells.GetEnumerator();

					while (cell.MoveNext() && !placed)
					{
						var subCell = positionable.GetAvailableSubCell(cell.Current);

						if (ai.HasTraitInfo<AircraftInfo>()
							&& ai.TraitInfo<AircraftInfo>().CanEnterCell(firedBy.World, unit, cell.Current))
							subCell = SubCell.FullCell;

						if (subCell != SubCell.Invalid)
						{
							positionable.SetPosition(unit, cell.Current, subCell);

							var pos = unit.CenterPosition;
							if (!ForceGround)
								pos += new WVec(WDist.Zero, WDist.Zero, firedBy.World.Map.DistanceAboveTerrain(delayedTarget.CenterPosition));

							positionable.SetVisualPosition(unit, pos);
							w.Add(unit);

							if (Paradrop)
								unit.QueueActivity(new Parachute(unit));
							else
								unit.QueueActivity(new FallDown(unit, pos, FallRate));

							placed = true;
						}
					}

					if (!placed)
						unit.Dispose();
				});
			}
		}
	}
}
