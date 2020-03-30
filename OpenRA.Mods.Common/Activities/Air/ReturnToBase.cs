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
using System.Drawing;
using System.Linq;
using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class ReturnToBase : Activity, IDockActivity
	{
		readonly Aircraft aircraft;
		readonly RepairableInfo repairableInfo;
		readonly Rearmable rearmable;
		readonly bool alwaysLand;
		Actor dest;
		int facing = -1;

		public ReturnToBase(Actor self, Actor dest = null, bool alwaysLand = false)
		{
			this.dest = dest;
			this.alwaysLand = alwaysLand;
			aircraft = self.Trait<Aircraft>();
			repairableInfo = self.Info.TraitInfoOrDefault<RepairableInfo>();
			rearmable = self.TraitOrDefault<Rearmable>();
		}

		public static IEnumerable<Actor> GetAirfields(Actor self)
		{
			var rearmActors = self.Info.TraitInfo<RearmableInfo>().RearmActors;
			return self.World.ActorsHavingTrait<DockManager>()
				.Where(a => !a.IsDead
					&& a.Owner == self.Owner
					&& rearmActors.Contains(a.Info.Name));
		}

		void CalculateLandingPath(Actor self, Dock dock, out WPos w1, out WPos w2, out WPos w3)
		{
			var landPos = dock.CenterPosition;
			var altitude = aircraft.Info.CruiseAltitude.Length;

			// Distance required for descent.
			var landDistance = altitude * 1024 / aircraft.Info.MaximumPitch.Tan();

			// Land towards the east
			var approachStart = landPos + new WVec(-landDistance, 0, altitude);

			// Add 10% to the turning radius to ensure we have enough room
			var speed = aircraft.MovementSpeed * 32 / 35;
			var turnRadius = Fly.CalculateTurnRadius(speed, aircraft.Info.TurnSpeed);

			// Find the center of the turning circles for clockwise and counterclockwise turns
			var angle = WAngle.FromFacing(aircraft.Facing);
			var fwd = -new WVec(angle.Sin(), angle.Cos(), 0);

			// Work out whether we should turn clockwise or counter-clockwise for approach
			var side = new WVec(-fwd.Y, fwd.X, fwd.Z);
			var approachDelta = self.CenterPosition - approachStart;
			var sideTowardBase = new[] { side, -side }
				.MinBy(a => WVec.Dot(a, approachDelta));

			// Calculate the tangent line that joins the turning circles at the current and approach positions
			var cp = self.CenterPosition + turnRadius * sideTowardBase / 1024;
			var posCenter = new WPos(cp.X, cp.Y, altitude);
			var approachCenter = approachStart + new WVec(0, turnRadius * Math.Sign(self.CenterPosition.Y - approachStart.Y), 0);
			var tangentDirection = approachCenter - posCenter;
			var tangentLength = tangentDirection.Length;
			var tangentOffset = WVec.Zero;
			if (tangentLength != 0)
				tangentOffset = new WVec(-tangentDirection.Y, tangentDirection.X, 0) * turnRadius / tangentLength;

			// TODO: correctly handle CCW <-> CW turns
			if (tangentOffset.X > 0)
				tangentOffset = -tangentOffset;

			w1 = posCenter + tangentOffset;
			w2 = approachCenter + tangentOffset;
			w3 = approachStart;
		}

		bool ShouldLandAtBuilding(Actor self, Actor dest)
		{
			if (alwaysLand)
				return true;

			if (repairableInfo != null && repairableInfo.RepairActors.Contains(dest.Info.Name) && self.GetDamageState() != DamageState.Undamaged)
				return true;

			return rearmable != null && rearmable.Info.RearmActors.Contains(dest.Info.Name)
					&& rearmable.RearmableAmmoPools.Any(p => !p.FullAmmo());
		}

		protected override void OnFirstRun(Actor self)
		{
			// Release first, before trying to dock.
			var dc = self.TraitOrDefault<DockClient>();
			if (dc != null)
				dc.Release();
		}

		public override Activity Tick(Actor self)
		{
			if (IsCanceled || self.IsDead)
				return NextActivity;

			// Check status and make dest correct.
			// Priorities:
			// 1. closest reloadable afld
			// 2. closest afld
			// 3. null
			if (dest == null || dest.IsDead || dest.Disposed)
			{
				var aflds = GetAirfields(self);
				var dockableAflds = aflds.Where(p => p.Trait<DockManager>().HasFreeServiceDock(self));
				if (dockableAflds.Any())
					dest = dockableAflds.ClosestTo(self);
				else if (aflds.Any())
					dest = aflds.ClosestTo(self);
				else
					dest = null;
			}

			// Owner doesn't have any feasible afld. In this case,
			if (dest == null)
			{
				// Prevent an infinite loop in case we'd return to the activity that called ReturnToBase in the first place.
				// Go idle instead.
				Cancel(self);
				return NextActivity;
			}

			// Player has an airfield but it is busy. Circle around.
			if (!dest.Trait<DockManager>().HasFreeServiceDock(self))
			{
				Queue(ActivityUtils.SequenceActivities(
					new Fly(self, Target.FromActor(dest), WDist.Zero, aircraft.Info.WaitDistanceFromResupplyBase, targetLineColor: Color.Green),
					new FlyCircle(self, aircraft.Info.NumberOfTicksToVerifyAvailableAirport),
					new ReturnToBase(self, abortOnResupply, null, alwaysLand)));
				return NextActivity;
			}

			// Now we land. Unlike helis, regardless of ShouldLandAtBuilding, we should land.
			// The difference is, do we just land or do we land and resupply.
			dest.Trait<DockManager>().ReserveDock(dest, self, this);
			return NextActivity;
		}

		public Activity LandingProcedure(Actor self, Dock dock)
		{
			WPos w1, w2, w3;
			CalculateLandingPath(self, dock, out w1, out w2, out w3);

			List<Activity> landingProcedures = new List<Activity>();

			var turnRadius = Fly.CalculateTurnRadius(aircraft.Info.Speed, aircraft.Info.TurnSpeed);

			landingProcedures.Add(new Fly(self, Target.FromPos(w1), WDist.Zero, new WDist(turnRadius * 3)));
			landingProcedures.Add(new Fly(self, Target.FromPos(w2)));

			// Fix a problem when the airplane is send to resupply near the airport
			landingProcedures.Add(new Fly(self, Target.FromPos(w3), WDist.Zero, new WDist(turnRadius / 2)));

			if (ShouldLandAtBuilding(self, dest))
				landingProcedures.Add(new Land(self, Target.FromPos(dock.CenterPosition)));

			/*
			// Causes bugs. Aircrafts should forget what they were doing.
			// if (!abortOnResupply)
			//	landingProcedures.Add(NextActivity);
			*/
			
			return ActivityUtils.SequenceActivities(landingProcedures.ToArray());
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			if (ChildActivity == null)
				yield return new TargetLineNode(Target.FromActor(dest), Color.Green);
			else
				foreach (var n in ChildActivity.TargetLineNodes(self))
					yield return n;
		}

		Activity IDockActivity.ApproachDockActivities(Actor host, Actor client, Dock dock)
		{
			// Let's reload. The assumption here is that for aircrafts, there are no waiting docks.
			return LandingProcedure(client, dock);
		}

		Activity IDockActivity.DockActivities(Actor host, Actor client, Dock dock)
		{
			client.SetTargetLine(Target.FromPos(dock.CenterPosition), Color.Green, false);
			return new ResupplyAircraft(client);
		}

		Activity IDockActivity.ActivitiesAfterDockDone(Actor host, Actor client, Dock dock)
		{
			// I'm ASSUMING rallypoint here.
			var rp = host.Trait<RallyPoint>();

			client.SetTargetLine(Target.FromCell(client.World, rp.Location), Color.Green, false);

			// ResupplyAircraft handles this.
			// Take off and move to RP.
			return ActivityUtils.SequenceActivities(
				new Fly(client, Target.FromCell(client.World, rp.Location)),
				new FlyCircle(client));
		}

		Activity IDockActivity.ActivitiesOnDockFail(Actor client)
		{
			return new ReturnToBase(client, abortOnResupply);
		}
	}
}
