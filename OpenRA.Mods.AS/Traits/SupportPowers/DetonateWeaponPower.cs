#region Copyright & License Information
/*
 * Copyright 2015- OpenRA.Mods.AS Developers (see AUTHORS)
 * This file is a part of a third-party plugin for OpenRA, which is
 * free software. It is made available to you under the terms of the
 * GNU General Public License as published by the Free Software
 * Foundation. For more information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Effects;
using OpenRA.GameRules;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Effects;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.AS.Traits
{
	[Desc("Support power for detonating a weapon at the target position.")]
	public class DetonateWeaponPowerInfo : SupportPowerInfo, IRulesetLoaded
	{
		[FieldLoader.Require]
		public readonly Dictionary<int, string> Weapons = new Dictionary<int, string>();

		[Desc("Delay between activation and explosion")]
		public readonly int ActivationDelay = 10;

		[Desc("Amount of time before detonation to remove the beacon")]
		public readonly int BeaconRemoveAdvance = 5;

		[Desc("Range of cells the camera should reveal around target cell.")]
		public readonly WDist CameraRange = WDist.Zero;

		[Desc("Can the camera reveal shroud generated by the GeneratesShroud trait?")]
		public readonly bool RevealGeneratedShroud = true;

		[Desc("Ignore AirburstAltitude for where to spawn the camera.")]
		public readonly bool SpawnCameraAtGround = true;

		[Desc("Reveal cells to players with these stances only.")]
		public readonly Stance CameraStances = Stance.Ally;

		[Desc("Amount of time before detonation to spawn the camera")]
		public readonly int CameraSpawnAdvance = 5;

		[Desc("Amount of time after detonation to remove the camera")]
		public readonly int CameraRemoveDelay = 5;

		[SequenceReference]
		[Desc("Sequence the launching actor should play when activating this power.")]
		public readonly string ActivationSequence = "active";

		[Desc("Altitude above terrain below which to explode. Zero effectively deactivates airburst.")]
		public readonly WDist AirburstAltitude = WDist.Zero;

		public readonly Dictionary<int, WDist> TargetCircleRanges;
		public readonly Color TargetCircleColor = Color.White;
		public readonly bool TargetCircleUsePlayerColor = false;

		public readonly Dictionary<int, WeaponInfo> WeaponInfos = new Dictionary<int, WeaponInfo>();

		public override object Create(ActorInitializer init) { return new DetonateWeaponPower(init.Self, this); }
		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			foreach (var weapon in Weapons)
			{
				WeaponInfo weaponInfo;
				var weaponToLower = weapon.Value.ToLowerInvariant();
				if (!rules.Weapons.TryGetValue(weaponToLower, out weaponInfo))
					throw new YamlException("Weapons Ruleset does not contain an entry '{0}'".F(weaponToLower));

				if (!WeaponInfos.ContainsKey(weapon.Key))
					WeaponInfos.Add(weapon.Key, rules.Weapons[weaponToLower]);
			}

			base.RulesetLoaded(rules, ai);
		}
	}

	public class DetonateWeaponPower : SupportPower, ITick
	{
		public new readonly DetonateWeaponPowerInfo Info;
		int ticks;

		public DetonateWeaponPower(Actor self, DetonateWeaponPowerInfo info)
			: base(self, info)
		{
			Info = info;
		}

		public override void Activate(Actor self, Order order, SupportPowerManager manager)
		{
			base.Activate(self, order, manager);

			if (self.Owner.IsAlliedWith(self.World.RenderPlayer))
				Game.Sound.Play(SoundType.World, Info.LaunchSound);
			else
				Game.Sound.Play(SoundType.World, Info.IncomingSound);

			if (!string.IsNullOrEmpty(Info.ActivationSequence))
			{
				var wsb = self.Trait<WithSpriteBody>();
				wsb.PlayCustomAnimation(self, Info.ActivationSequence);
			}

			var targetPosition = order.Target.CenterPosition + new WVec(WDist.Zero, WDist.Zero, Info.AirburstAltitude);

			Action detonateWeapon = () => self.World.AddFrameEndTask(w => Info.WeaponInfos.First(wi => wi.Key == GetLevel()).Value.Impact(Target.FromPos(targetPosition), self));

			self.World.AddFrameEndTask(w => w.Add(new DelayedAction(Info.ActivationDelay, detonateWeapon)));

			if (Info.CameraRange != WDist.Zero)
			{
				var cameraPosition = Info.SpawnCameraAtGround ? order.Target.CenterPosition : targetPosition;
				var type = Info.RevealGeneratedShroud ? Shroud.SourceType.Visibility
					: Shroud.SourceType.PassiveVisibility;

				self.World.AddFrameEndTask(w => w.Add(new RevealShroudEffect(cameraPosition, Info.CameraRange, type, self.Owner, Info.CameraStances,
					Info.ActivationDelay - Info.CameraSpawnAdvance, Info.CameraSpawnAdvance + Info.CameraRemoveDelay)));
			}

			if (Info.DisplayBeacon)
			{
				var beacon = new Beacon(
					order.Player,
					targetPosition,
					Info.BeaconPaletteIsPlayerPalette,
					Info.BeaconPalette,
					Info.BeaconImage,
					Info.BeaconPosters.First(bp => bp.Key == GetLevel()).Value,
					Info.BeaconPosterPalette,
					Info.BeaconSequence,
					Info.ArrowSequence,
					Info.CircleSequence,
					Info.ClockSequence,
					() => FractionComplete);

				Action removeBeacon = () => self.World.AddFrameEndTask(w =>
					{
						w.Remove(beacon);
						beacon = null;
					});

				self.World.AddFrameEndTask(w =>
					{
						w.Add(beacon);
						w.Add(new DelayedAction(Info.ActivationDelay - Info.BeaconRemoveAdvance, removeBeacon));
					});
			}
		}

		void ITick.Tick(Actor self)
		{
			ticks++;
		}

		public override void SelectTarget(Actor self, string order, SupportPowerManager manager)
		{
			Game.Sound.PlayToPlayer(SoundType.UI, manager.Self.Owner, Info.SelectTargetSound);
			self.World.OrderGenerator = new SelectDetonateWeaponPowerTarget(order, manager, this);
		}

		float FractionComplete { get { return ticks * 1f / Info.ActivationDelay; } }
	}

	public class SelectDetonateWeaponPowerTarget : OrderGenerator
	{
		readonly SupportPowerManager manager;
		readonly string order;
		readonly DetonateWeaponPower power;

		public SelectDetonateWeaponPowerTarget(string order, SupportPowerManager manager, DetonateWeaponPower power)
		{
			// Clear selection if using Left-Click Orders
			if (Game.Settings.Game.UseClassicMouseStyle)
				manager.Self.World.Selection.Clear();

			this.manager = manager;
			this.order = order;
			this.power = power;
		}

		protected override IEnumerable<Order> OrderInner(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			world.CancelInputMode();
			if (mi.Button == MouseButton.Left && world.Map.Contains(cell))
				yield return new Order(order, manager.Self, Target.FromCell(world, cell), false) { SuppressVisualFeedback = true };
		}

		protected override void Tick(World world)
		{
			// Cancel the OG if we can't use the power
			if (!manager.Powers.ContainsKey(order))
				world.CancelInputMode();
		}

		protected override IEnumerable<IRenderable> Render(WorldRenderer wr, World world) { yield break; }

		protected override IEnumerable<IRenderable> RenderAboveShroud(WorldRenderer wr, World world) { yield break; }

		protected override IEnumerable<IRenderable> RenderAnnotations(WorldRenderer wr, World world)
		{
			var xy = wr.Viewport.ViewToWorld(Viewport.LastMousePos);

			if (power.Info.TargetCircleRanges == null || !power.Info.TargetCircleRanges.Any() || power.GetLevel() == 0)
			{
				yield break;
			}
			else
			{
				yield return new RangeCircleAnnotationRenderable(
					world.Map.CenterOfCell(xy),
					power.Info.TargetCircleRanges[power.GetLevel()],
					0,
					power.Info.TargetCircleUsePlayerColor ? power.Self.Owner.Color : power.Info.TargetCircleColor,
					Color.FromArgb(96, Color.Black));
			}
		}

		protected override string GetCursor(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			return world.Map.Contains(cell) ? power.Info.Cursor : "generic-blocked";
		}
	}
}
