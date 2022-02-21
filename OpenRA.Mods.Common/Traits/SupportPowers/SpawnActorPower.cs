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
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Effects;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Spawns an actor that stays for a limited amount of time.")]
	public class SpawnActorPowerInfo : SupportPowerInfo
	{
		[FieldLoader.Require]
		[Desc("Actors to spawn for each level.")]
		public readonly Dictionary<int, string> Actors = new Dictionary<int, string>();

		[Desc("Amount of time to keep the actor alive in ticks. Value < 0 means this actor will not remove itself.")]
		public readonly int LifeTime = 250;

		public readonly string DeploySound = null;

		public readonly string EffectImage = null;

		[SequenceReference("EffectImage")]
		public readonly string EffectSequence = "idle";

		[PaletteReference]
		public readonly string EffectPalette = null;

		public override object Create(ActorInitializer init) { return new SpawnActorPower(init.Self, this); }
	}

	public class SpawnActorPower : SupportPower
	{
		public SpawnActorPower(Actor self, SpawnActorPowerInfo info)
			: base(self, info) { }

		public override void Activate(Actor self, Order order, SupportPowerManager manager)
		{
			base.Activate(self, order, manager);

			var info = Info as SpawnActorPowerInfo;

			if (info.Actors != null)
			{
				self.World.AddFrameEndTask(w =>
				{
					PlayLaunchSounds();
					Game.Sound.Play(SoundType.World, info.DeploySound, order.Target.CenterPosition);

					if (!string.IsNullOrEmpty(info.EffectSequence) && !string.IsNullOrEmpty(info.EffectPalette))
						w.Add(new SpriteEffect(order.Target.CenterPosition, w, info.EffectImage, info.EffectSequence, info.EffectPalette));

					var actor = w.CreateActor(info.Actors.First(a => a.Key == GetLevel()).Value, new TypeDictionary
					{
						new LocationInit(self.World.Map.CellContaining(order.Target.CenterPosition)),
						new OwnerInit(self.Owner),
					});

					if (info.LifeTime > -1)
					{
						actor.QueueActivity(new Wait(info.LifeTime));
						actor.QueueActivity(new RemoveSelf());
					}
				});
			}
		}
	}
}
