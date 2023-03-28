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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public class RevealsShroudToIntelligenceOwnerInfo : RevealsShroudInfo
	{
		[FieldLoader.Require]
		[Desc("Types of intelligence this trait requires.")]
		public readonly HashSet<string> Types = new HashSet<string>();

		public override object Create(ActorInitializer init) { return new RevealsShroudToIntelligenceOwner(init.Self, this); }
	}

	public class RevealsShroudToIntelligenceOwner : RevealsShroud, INotifyAddedToWorld, INotifyMoving, INotifyVisualPositionChanged, ITick
	{
		public readonly new RevealsShroudToIntelligenceOwnerInfo Info;
		public List<Player> IntelOwners = new List<Player>();

		public RevealsShroudToIntelligenceOwner(Actor self, RevealsShroudToIntelligenceOwnerInfo info)
			: base(self, info)
		{
			Info = info;
		}

		public override void AddCellsToPlayerShroud(Actor self, Player p, PPos[] uv)
		{
			p.Shroud.AddSource(this, Type, uv);
		}

		void INotifyVisualPositionChanged.VisualPositionChanged(Actor self, byte oldLayer, byte newLayer)
		{
			if (!self.IsInWorld)
				return;

			if (self.Owner.NonCombatant)
				return;

			if (!IntelOwners.Any())
				return;

			var centerPosition = self.CenterPosition;
			var projectedPos = centerPosition - new WVec(0, centerPosition.Z, centerPosition.Z);
			var projectedLocation = self.World.Map.CellContaining(projectedPos);
			var pos = self.CenterPosition;

			var dirty = Info.MoveRecalculationThreshold.Length > 0 && (pos - cachedPos).LengthSquared > Info.MoveRecalculationThreshold.LengthSquared;
			if (!dirty && cachedLocation == projectedLocation)
				return;

			cachedLocation = projectedLocation;
			cachedPos = pos;

			var cells = ProjectedCells(self);
			foreach (var p in self.World.Players)
			{
				RemoveCellsFromPlayerShroud(self, p);
				if (IntelOwners.Contains(p))
					AddCellsToPlayerShroud(self, p, cells);
			}
		}

		void ITick.Tick(Actor self)
		{
			if (!self.IsInWorld)
				return;

			if (self.Owner.NonCombatant)
				return;

			if (!IntelOwners.Any())
				return;

			var traitDisabled = IsTraitDisabled;
			var range = Range;

			if (cachedRange == range && traitDisabled == cachedTraitDisabled)
				return;

			cachedRange = range;
			cachedTraitDisabled = traitDisabled;

			var cells = ProjectedCells(self);
			foreach (var p in self.World.Players)
			{
				RemoveCellsFromPlayerShroud(self, p);
				if (IntelOwners.Contains(p))
					AddCellsToPlayerShroud(self, p, cells);
			}
		}

		void INotifyAddedToWorld.AddedToWorld(Actor self)
		{
			if (!self.IsInWorld)
				return;

			if (self.Owner.NonCombatant)
				return;

			var centerPosition = self.CenterPosition;
			var projectedPos = centerPosition - new WVec(0, centerPosition.Z, centerPosition.Z);
			cachedLocation = self.World.Map.CellContaining(projectedPos);
			cachedTraitDisabled = IsTraitDisabled;
			var cells = ProjectedCells(self);

			foreach (var p in self.World.Players)
			{
				var hasIntel = self.World.ActorsWithTrait<GivesIntelligence>().Where(t => t.Actor.Owner == p && t.Trait.Info.Types.Overlaps(Info.Types) && !t.Trait.IsTraitDisabled).Any();

				if (hasIntel)
				{
					RemoveCellsFromPlayerShroud(self, p);
					AddCellsToPlayerShroud(self, p, cells);

					IntelOwners.Add(p);
				}
			}
		}

		void INotifyMoving.MovementTypeChanged(Actor self, MovementType type)
		{
			if (self.Owner.NonCombatant)
				return;

			if (!IntelOwners.Any())
				return;

			// Recalculate the visiblity at our final stop position
			if (type == MovementType.None && self.IsInWorld)
			{
				var centerPosition = self.CenterPosition;
				var projectedPos = centerPosition - new WVec(0, centerPosition.Z, centerPosition.Z);
				var projectedLocation = self.World.Map.CellContaining(projectedPos);
				var pos = self.CenterPosition;

				cachedLocation = projectedLocation;
				cachedPos = pos;

				var cells = ProjectedCells(self);
				foreach (var p in self.World.Players)
				{
					RemoveCellsFromPlayerShroud(self, p);
					if (IntelOwners.Contains(p))
						AddCellsToPlayerShroud(self, p, cells);
				}
			}
		}
	}
}
