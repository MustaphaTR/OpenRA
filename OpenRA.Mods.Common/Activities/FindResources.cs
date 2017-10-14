#region Copyright & License Information
/*
 * Copyright 2007-2017 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Drawing;
using OpenRA.Activities;
using OpenRA.Mods.Common.Pathfinder;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class FindResources : Activity
	{
		readonly Harvester harv;
		readonly HarvesterInfo harvInfo;
		readonly Mobile mobile;
		readonly MobileInfo mobileInfo;
		readonly ResourceClaimLayer claimLayer;
		readonly IPathFinder pathFinder;
		readonly DomainIndex domainIndex;

		CPos? avoidCell;

		public FindResources(Actor self)
		{
			harv = self.Trait<Harvester>();
			harvInfo = self.Info.TraitInfo<HarvesterInfo>();
			mobile = self.Trait<Mobile>();
			mobileInfo = self.Info.TraitInfo<MobileInfo>();
			claimLayer = self.World.WorldActor.Trait<ResourceClaimLayer>();
			pathFinder = self.World.WorldActor.Trait<IPathFinder>();
			domainIndex = self.World.WorldActor.Trait<DomainIndex>();
		}

		public FindResources(Actor self, CPos avoidCell)
			: this(self)
		{
			this.avoidCell = avoidCell;
		}

		public override Activity Tick(Actor self)
		{
			if (IsCanceled)
				return NextActivity;

			if (NextInQueue != null)
				return NextInQueue;

			var deliver = new DeliverResources(self);

			if (harv.IsFull)
			{
				Queue(deliver);
				return NextActivity;
			}

			var closestHarvestablePosition = ClosestHarvestablePos(self);

			// If no harvestable position could be found, either deliver the remaining resources
			// or get out of the way and do not disturb.
			if (!closestHarvestablePosition.HasValue)
			{
				if (!harv.IsEmpty)
				{
					Queue(deliver);
					return NextActivity;
				}

				harv.LastSearchFailed = true;

				var unblockCell = harv.LastHarvestedCell ?? (self.Location + harvInfo.UnblockCell);
				var moveTo = mobile.NearestMoveableCell(unblockCell, 2, 5);
				self.QueueActivity(mobile.MoveTo(moveTo, 1));
				self.SetTargetLine(Target.FromCell(self.World, moveTo), Color.Gray, false);

				// TODO: The harvest-deliver-return sequence is a horrible mess of duplicated code and edge-cases
				var notify = self.TraitsImplementing<INotifyHarvesterAction>();
				foreach (var n in notify)
					n.MovingToResources(self, moveTo, this);

				var randFrames = self.World.SharedRandom.Next(100, 175);

				// Avoid creating an activity cycle
				var next = NextInQueue;
				NextInQueue = null;

				if (next != null)
					return ActivityUtils.SequenceActivities(next, new Wait(randFrames), this);
				else
					return ActivityUtils.SequenceActivities(new Wait(randFrames), this);
			}
			else
			{
				// Attempt to claim the target cell
				if (!claimLayer.TryClaimCell(self, closestHarvestablePosition.Value))
					return ActivityUtils.SequenceActivities(new Wait(25), this);

				harv.LastSearchFailed = false;

				// If not given a direct order, assume ordered to the first resource location we find:
				if (!harv.LastOrderLocation.HasValue)
					harv.LastOrderLocation = closestHarvestablePosition;

				self.SetTargetLine(Target.FromCell(self.World, closestHarvestablePosition.Value), Color.Red, false);

				var move = mobile.MoveTo(closestHarvestablePosition.Value, 2);

				Activity extraActivities = null;
				var notify = self.TraitsImplementing<INotifyHarvesterAction>();
				foreach (var n in notify)
				{
					var extra = n.MovingToResources(self, closestHarvestablePosition.Value, move);
					if (extra != null)
					{
						if (extraActivities != null)
							throw new InvalidOperationException("Actor {0} has conflicting activities to perform for INotifyHarvesterAction.".F(self.ToString()));

						extraActivities = extra;
					}
				}

				if (extraActivities != null)
					Queue(extraActivities);
				else
					Queue(move);

				Queue(new HarvestResource(self));
				return NextActivity;
			}
		}

		/// <summary>
		/// Finds the closest harvestable pos between the current position of the harvester
		/// and the last order location
		/// </summary>
		CPos? ClosestHarvestablePos(Actor self)
		{
			if (harv.CanHarvestCell(self, self.Location) && claimLayer.CanClaimCell(self, self.Location))
				return self.Location;

			// Determine where to search from and how far to search:
			var searchFromLoc = harv.LastOrderLocation ?? (harv.LastLinkedProc ?? harv.LinkedProc ?? self).Location;
			var searchRadius = harv.LastOrderLocation.HasValue ? harvInfo.SearchFromOrderRadius : harvInfo.SearchFromProcRadius;
			var searchRadiusSquared = searchRadius * searchRadius;

			// Find any harvestable resources:
			var passable = (uint)mobileInfo.GetMovementClass(self.World.Map.Rules.TileSet);
			List<CPos> path;
			using (var search = PathSearch.Search(self.World, mobileInfo, self, true, loc =>
					domainIndex.IsPassable(self.Location, loc, mobileInfo, passable) && harv.CanHarvestCell(self, loc) && claimLayer.CanClaimCell(self, loc))
				.WithCustomCost(loc =>
				{
					if ((avoidCell.HasValue && loc == avoidCell.Value) ||
						(loc - self.Location).LengthSquared > searchRadiusSquared)
						return int.MaxValue;

					return 0;
				})
				.FromPoint(self.Location)
				.FromPoint(searchFromLoc))
				path = pathFinder.FindPath(search);

			if (path.Count > 0)
				return path[0];

			return null;
		}

		public override IEnumerable<Target> GetTargets(Actor self)
		{
			yield return Target.FromCell(self.World, self.Location);
		}
	}
}
