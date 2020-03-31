using System;
using System.Drawing;
using System.Linq;
using OpenRA.Activities;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Mods.Yupgi_alert.Traits;
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.Yupgi_alert.Activities
{
	public class FindGoods : Activity
	{
		readonly SupplyCollector collector;
		readonly SupplyCollectorInfo collectorInfo;
		readonly IMove move;
		readonly Mobile mobile;

		public FindGoods(Actor self)
		{
			collector = self.Trait<SupplyCollector>();
			collectorInfo = self.Info.TraitInfo<SupplyCollectorInfo>();
			move = self.Trait<IMove>();
			mobile = self.TraitOrDefault<Mobile>();
		}

		public override bool Tick(Actor self)
		{
			if (IsCanceling)
				return true;

			if (collector.CollectionBuilding == null || !collector.CollectionBuilding.IsInWorld || !collectorInfo.CollectionStances.HasStance(self.Owner.Stances[collector.CollectionBuilding.Owner]) || collector.CollectionBuilding.Trait<SupplyDock>().IsEmpty)
			{
				collector.CollectionBuilding = collector.ClosestTradeBuilding(self);
			}

			if (collector.CollectionBuilding == null || !collector.CollectionBuilding.IsInWorld)
			{
				QueueChild(new Wait(collectorInfo.SearchForCollectionBuildingDelay));
				return false;
			}

			var dock = collector.CollectionBuilding;
			var center = collector.DeliveryBuilding;
			self.ShowTargetLines();

			CPos cell;
			var dockTrait = dock.Trait<SupplyDock>();
			var centerTrait = center == null || !center.IsInWorld ? null : center.Trait<SupplyCenter>();
			var offsets = (mobile == null || collectorInfo.IsAircraft) && dockTrait.Info.AircraftCollectionOffsets.Any() ? dockTrait.Info.AircraftCollectionOffsets : dockTrait.Info.CollectionOffsets;
			var deliveryOffsets = centerTrait != null ? centerTrait.Info.DeliveryOffsets : null;
			if (mobile != null)
				cell = self.ClosestCell(offsets.Select(c => dock.Location + c).Where(c => mobile.CanEnterCell(c) && (centerTrait == null || !deliveryOffsets.Select(d => center.Location + d).Contains(c))));
			else
				cell = self.ClosestCell(offsets.Select(c => dock.Location + c).Where(c => centerTrait == null || !deliveryOffsets.Select(d => center.Location + d).Contains(c)));

			if (!offsets.Select(c => dock.Location + c).Where(c => centerTrait == null || !deliveryOffsets.Select(d => center.Location + d).Contains(c)).Contains(self.Location))
			{
				QueueChild(move.MoveTo(cell, 2));
				return false;
			}

			if (self.TraitOrDefault<IFacing>() != null)
			{
				if (dockTrait.Info.Facing >= 0 && self.Trait<IFacing>().Facing != dockTrait.Info.Facing)
				{
					QueueChild(new Turn(self, dockTrait.Info.Facing));
					return false;
				}
				else if (dockTrait.Info.Facing == -1)
				{
					var facing = (dock.CenterPosition - self.CenterPosition).Yaw.Facing;
					if (self.Trait<IFacing>().Facing != facing)
					{
						QueueChild(new Turn(self, facing));
						return false;
					}
				}
			}

			if (!collector.Waiting)
			{
				collector.Waiting = true;
				QueueChild(new Wait(collectorInfo.CollectionDelay));
				return false;
			}

			var wsb = self.TraitsImplementing<WithSpriteBody>().Where(t => !t.IsTraitDisabled).FirstOrDefault();
			var wsco = self.TraitOrDefault<WithSupplyCollectionOverlay>();
			if (wsb != null && wsco != null && !collector.DeliveryAnimPlayed)
			{
				if (!wsco.Visible)
				{
					wsco.Visible = true;
					wsco.Anim.PlayThen(wsco.Info.Sequence, () => wsco.Visible = false);
					collector.DeliveryAnimPlayed = true;
					QueueChild(new Wait(wsco.Info.WaitDelay));
					return false;
				}
			}

			collector.Waiting = false;
			collector.DeliveryAnimPlayed = false;
			var cash = Math.Min(collectorInfo.Capacity - collector.Amount, dockTrait.Amount);
			collector.Amount = cash;
			dockTrait.Amount = dockTrait.Amount - cash;
			collector.CheckConditions(self);
			dockTrait.CheckConditions(dock);

			Queue(new DeliverGoods(self));
			return true;
		}
	}
}
