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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Activities;
using OpenRA.Mods.Cnc.Traits;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Cnc.Activities
{
	// Assumes you have Minelayer on that unit
	public class LayMines : Activity, IDockActivity
	{
		readonly Minelayer minelayer;
		readonly AmmoPool[] ammoPools;
		readonly IMove movement;
		readonly RearmableInfo rearmableInfo;

		List<CPos> minefield;
		bool returnToBase;
		Actor rearmTarget;

		public LayMines(Actor self, List<CPos> minefield = null)
		{
			minelayer = self.Trait<Minelayer>();
			ammoPools = self.TraitsImplementing<AmmoPool>().ToArray();
			movement = self.Trait<IMove>();
			rearmableInfo = self.Info.TraitInfoOrDefault<RearmableInfo>();
			this.minefield = minefield;
		}

		protected override void OnFirstRun(Actor self)
		{
			if (minefield == null)
				minefield = new List<CPos> { self.Location };
		}

		CPos? NextValidCell(Actor self)
		{
			if (minefield != null)
				foreach (var c in minefield)
					if (CanLayMine(self, c))
						return c;

			return null;
		}

		public override bool Tick(Actor self)
		{
			returnToBase = false;

			if (IsCanceling)
				return true;

			if ((minefield == null || minefield.Contains(self.Location)) && CanLayMine(self, self.Location))
			{
				if (rearmableInfo != null && ammoPools.Any(p => p.Info.Name == info.AmmoPoolName && !p.HasAmmo()))
				{
					// Rearm (and possibly repair) at rearm building, then back out here to refill the minefield some more
					var rearmTarget = self.World.Actors.Where(a => self.Owner.Stances[a.Owner] == Stance.Ally
						&& rearmableInfo.RearmActors.Contains(a.Info.Name))
						.ClosestTo(self);

					if (rearmTarget == null)
						return new Wait(20);

					rearmTarget.Trait<DockManager>().ReserveDock(rearmTarget, self, this);
					return NextActivity;
				}

				LayMine(self);
				QueueChild(new Wait(20)); // A little wait after placing each mine, for show
				minefield.Remove(self.Location);
				return false;
			}

			var nextCell = NextValidCell(self);
			if (nextCell != null)
			{
				QueueChild(movement.MoveTo(nextCell.Value, 0));
				return false;
			}

			// TODO: Return somewhere likely to be safe (near rearm building) so we're not sitting out in the minefield.
			return true;
		}

		public void CleanPlacedMines(Actor self)
		{
			// Remove cells that have already been mined
			if (minefield != null)
				minefield.RemoveAll(c => self.World.ActorMap.GetActorsAt(c)
					.Any(a => a.Info.Name == minelayer.Info.Mine.ToLowerInvariant()));
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			if (returnToBase)
				yield return new TargetLineNode(Target.FromActor(rearmTarget), Color.Green);

			if (minefield == null || minefield.Count == 0)
				yield break;

			var nextCell = NextValidCell(self);
			if (nextCell != null)
				yield return new TargetLineNode(Target.FromCell(self.World, nextCell.Value), Color.Crimson);

			foreach (var c in minefield)
				yield return new TargetLineNode(Target.FromCell(self.World, c), Color.Crimson, tile: minelayer.Tile);
		}

		static bool CanLayMine(Actor self, CPos p)
		{
			// If there is no unit (other than me) here, we can place a mine here
			return self.World.ActorMap.GetActorsAt(p).All(a => a == self);
		}

		void LayMine(Actor self)
		{
			if (ammoPools != null)
			{
				var pool = ammoPools.FirstOrDefault(x => x.Info.Name == minelayer.Info.AmmoPoolName);
				if (pool == null)
					return;
				pool.TakeAmmo(self, 1);
			}

			self.World.AddFrameEndTask(w => w.CreateActor(minelayer.Info.Mine, new TypeDictionary
			{
				new LocationInit(self.Location),
				new OwnerInit(self.Owner),
			}));
		}

		Activity IDockActivity.ApproachDockActivities(Actor host, Actor client, Dock dock)
		{
			return DockUtils.GenericApproachDockActivities(host, client, dock, this, true);
		}

		Activity IDockActivity.DockActivities(Actor host, Actor client, Dock dock)
		{
			return ActivityUtils.SequenceActivities(
				new Rearm(client, host, new WDist(512)),
				new Repair(client, host, new WDist(512)));
		}

		Activity IDockActivity.ActivitiesAfterDockDone(Actor host, Actor client, Dock dock)
		{
			return new LayMines(client);
		}

		Activity IDockActivity.ActivitiesOnDockFail(Actor client)
		{
			// Find another FIX or something.
			return new LayMines(client);
		}
	}
}
