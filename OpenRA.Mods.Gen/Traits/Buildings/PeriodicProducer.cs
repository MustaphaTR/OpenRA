#region Copyright & License Information
/*
 * Copyright 2015- OpenRA.Mods.AS Developers (see AUTHORS)
 * This file is a part of a third-party plugin for OpenRA, which is
 * free software. It is made available to you under the terms of the
 * GNU General Public License as published by the Free Software
 * Foundation. For more information, see COPYING.
 */
#endregion

using System.Linq;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.AS.Traits
{
	[Desc("Produces an actor without using the standard production queue.")]
	public class PeriodicProducerInfo : PausableConditionalTraitInfo
	{
		[ActorReference, FieldLoader.Require]
		[Desc("Actors to produce.")]
		public readonly string[] Actors = null;

		[FieldLoader.Require]
		[Desc("Production queue type to use")]
		public readonly string Type = null;

		[Desc("Notification played when production is activated.",
			"The filename of the audio is defined per faction in notifications.yaml.")]
		public readonly string ReadyAudio = null;

		[Desc("Notification played when the exit is jammed.",
			"The filename of the audio is defined per faction in notifications.yaml.")]
		public readonly string BlockedAudio = null;

		[Desc("Duration between productions.")]
		public readonly int ChargeDuration = 1000;

		public readonly bool ShowSelectionBar = false;
		public readonly Color ChargeColor = Color.DarkOrange;

		public override object Create(ActorInitializer init) { return new PeriodicProducer(init, this); }
	}

	public class PeriodicProducer : PausableConditionalTrait<PeriodicProducerInfo>, ISelectionBar, ITick, ISync
	{
		readonly string faction;
		readonly PeriodicProducerInfo info;

		[Sync] int ticks;

		public PeriodicProducer(ActorInitializer init, PeriodicProducerInfo info)
			: base(info)
		{
			faction = init.Contains<FactionInit>() ? init.Get<FactionInit, string>() : init.Self.Owner.Faction.InternalName;
			this.info = info;
			ticks = info.ChargeDuration;
		}

		void ITick.Tick(Actor self)
		{
			if (IsTraitPaused || IsTraitDisabled)
				return;

			if (--ticks < 0)
			{
				var sp = self.TraitsImplementing<Production>()
				.FirstOrDefault(p => p.Info.Produces.Contains(info.Type));

				var activated = false;
				if (sp != null)
				{
					foreach (var name in info.Actors)
					{
						var ai = self.World.Map.Rules.Actors[name];
						var inits = new TypeDictionary
						{
							new OwnerInit(self.Owner),
							new FactionInit(BuildableInfo.GetInitialFaction(ai, faction))
						};

						activated |= sp.Produce(self, ai, info.Type, inits);
					}
				}

				if (activated)
					Game.Sound.PlayNotification(self.World.Map.Rules, self.Owner, "Speech", info.ReadyAudio, self.Owner.Faction.InternalName);
				else
					Game.Sound.PlayNotification(self.World.Map.Rules, self.Owner, "Speech", info.BlockedAudio, self.Owner.Faction.InternalName);

				ticks = info.ChargeDuration;
			}
		}

		protected override void TraitEnabled(Actor self)
		{
			ticks = info.ChargeDuration;
		}

		float ISelectionBar.GetValue()
		{
			if (!info.ShowSelectionBar || IsTraitDisabled)
				return 0f;

			return (float)(info.ChargeDuration - ticks) / info.ChargeDuration;
		}

		Color ISelectionBar.GetColor()
		{
			return info.ChargeColor;
		}

		bool ISelectionBar.DisplayWhenEmpty
		{
			get { return info.ShowSelectionBar && !IsTraitDisabled; }
		}
	}
}
