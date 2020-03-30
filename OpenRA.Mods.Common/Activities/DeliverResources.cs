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
using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class DeliverResources : Activity, IDockActivity
	{
		readonly Harvester harv;
		readonly Actor targetActor;
		readonly INotifyHarvesterAction[] notifyHarvesterActions;

		Actor proc;

		public DeliverResources(Actor self, Actor targetActor = null)
		{
			harv = self.Trait<Harvester>();
			this.targetActor = targetActor;
			notifyHarvesterActions = self.TraitsImplementing<INotifyHarvesterAction>().ToArray();
		}

		protected override void OnFirstRun(Actor self)
		{
			if (targetActor != null && targetActor.IsInWorld)
				harv.LinkProc(self, targetActor);
		}

		public override bool Tick(Actor self)
		{
			if (IsCanceling)
				return true;

			// If a refinery is explicitly specified, link it.
			if (harv.OwnerLinkedProc != null && harv.OwnerLinkedProc.IsInWorld)
			{
				harv.LinkProc(self, harv.OwnerLinkedProc);
				harv.OwnerLinkedProc = null;
			}
			//// at this point, harv.OwnerLinkedProc == null.

			// Find the nearest best refinery if not explicitly ordered to a specific refinery:
			if (harv.LinkedProc == null || !harv.LinkedProc.IsInWorld)
				harv.ChooseNewProc(self, null);

			// No refineries exist; check again after delay defined in Harvester.
			if (harv.LinkedProc == null)
			{
				QueueChild(new Wait(harv.Info.SearchForDeliveryBuildingDelay));
				return false;
			}

			proc = harv.LinkedProc;
			var dm = proc.Trait<DockManager>();

			if (self.Location != dm.DockLocations.FirstOrDefault())
			{
				foreach (var n in notifyHarvesterActions)
					n.MovingToRefinery(self, proc, this);
			}

			if (!self.Info.TraitInfo<HarvesterInfo>().OreTeleporter)
				dm.ReserveDock(proc, self, this);
			else
			{
				var dock = proc.TraitsImplementing<Dock>().First();
				Queue(DockActivities(proc, self, dock));
				Queue(new CallFunc(() => harv.ContinueHarvesting(self)));
			}

			return true;
		}

		public override void Cancel(Actor self, bool keepQueue = false)
		{
			foreach (var n in notifyHarvesterActions)
				n.MovementCancelled(self);

			base.Cancel(self, keepQueue);
		}

		Activity IDockActivity.ApproachDockActivities(Actor host, Actor client, Dock dock)
		{
			var moveToDock = DockUtils.GenericApproachDockActivities(host, client, dock, this);
			Activity extraActivities = null;

			var notify = client.TraitsImplementing<INotifyHarvesterAction>();
			foreach (var n in notify)
			{
				var extra = n.MovingToRefinery(client, host, moveToDock);

				// We have multiple MovingToRefinery actions to do!
				// Don't know which one to perform.
				if (extra != null)
				{
					if (extraActivities != null)
						throw new InvalidOperationException("Actor {0} has conflicting activities to perform for INotifyHarvesterAction.".F(client.ToString()));

					extraActivities = extra;
				}
			}

			if (extraActivities != null)
				return extraActivities;

			return moveToDock;
		}

		public Activity DockActivities(Actor host, Actor client, Dock dock)
		{
			return host.Trait<Refinery>().DockSequence(client, host, dock);
		}

		Activity IDockActivity.ActivitiesAfterDockDone(Actor host, Actor client, Dock dock)
		{
			// Move to south of the ref to avoid cluttering up with other dock locations
			return new CallFunc(() => harv.ContinueHarvesting(client));
		}

		Activity IDockActivity.ActivitiesOnDockFail(Actor client)
		{
			// go to somewhere else
			return new CallFunc(() => harv.ContinueHarvesting(client));
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			if (proc != null)
				yield return new TargetLineNode(Target.FromActor(proc), Color.Green);
			else
				yield return new TargetLineNode(Target.FromActor(harv.LinkedProc), Color.Green);
		}
	}
}
