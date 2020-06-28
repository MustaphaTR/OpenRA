﻿#region Copyright & License Information
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
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.AS.Traits
{
	// What to do when master is killed or mind controlled
	public enum SpawnerSlaveDisposal
	{
		DoNothing,
		KillSlaves,
		GiveSlavesToAttacker
	}

	public class BaseSpawnerSlaveEntry
	{
		public string ActorName = null;
		public Actor Actor = null;
		public BaseSpawnerSlave SpawnerSlave = null;
		public bool IsLaunched;

		public bool IsValid { get { return Actor != null && !Actor.IsDead; } }
	}

	[Desc("This actor can spawn actors.")]
	public class BaseSpawnerMasterInfo : PausableConditionalTraitInfo
	{
		[Desc("Spawn these units. Define this like paradrop support power.")]
		public readonly string[] Actors;

		[Desc("Slave actors to contain upon creation. Set to -1 to start with full slaves.")]
		public readonly int InitialActorCount = -1;

		[Desc("Name of the armaments that grant this condition.")]
		public readonly HashSet<string> ArmamentNames = new HashSet<string>() { "primary" };

		[Desc("What happens to the slaves when the master is killed?")]
		public readonly SpawnerSlaveDisposal SlaveDisposalOnKill = SpawnerSlaveDisposal.KillSlaves;

		[Desc("What happens to the slaves when the master is mind controlled?")]
		public readonly SpawnerSlaveDisposal SlaveDisposalOnOwnerChange = SpawnerSlaveDisposal.GiveSlavesToAttacker;

		[Desc("Only spawn initial load of slaves?")]
		public readonly bool NoRegeneration = false;

		[Desc("Spawn all slaves at once when regenerating slaves, instead of one by one?")]
		public readonly bool SpawnAllAtOnce = false;

		[Desc("Spawn regen delay, in ticks")]
		public readonly int RespawnTicks = 150;

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			base.RulesetLoaded(rules, ai);

			if (Actors == null || Actors.Length == 0)
				throw new YamlException("Actors is null or empty for a spawner trait in actor type {0}!".F(ai.Name));

			if (InitialActorCount > Actors.Length)
				throw new YamlException("InitialActorCount can't be larger than the actors defined! (Actor type = {0})".F(ai.Name));

			if (InitialActorCount < -1)
				throw new YamlException("InitialActorCount must be -1 or non-negative. Actor type = {0}".F(ai.Name));
		}

		public override object Create(ActorInitializer init) { return new BaseSpawnerMaster(init, this); }
	}

	public class BaseSpawnerMaster : PausableConditionalTrait<BaseSpawnerMasterInfo>, INotifyKilled, INotifyOwnerChanged, INotifyActorDisposing
	{
		readonly Actor self;

		IFacing facing;

		protected IReloadModifier[] reloadModifiers;

		public readonly BaseSpawnerSlaveEntry[] SlaveEntries;

		public BaseSpawnerMaster(ActorInitializer init, BaseSpawnerMasterInfo info)
			: base(info)
		{
			self = init.Self;

			// Initialize slave entries (doesn't instantiate the slaves yet)
			SlaveEntries = CreateSlaveEntries(info);

			for (var i = 0; i < info.Actors.Length; i++)
			{
				var entry = SlaveEntries[i];
				entry.ActorName = info.Actors[i].ToLowerInvariant();
			}
		}

		public virtual BaseSpawnerSlaveEntry[] CreateSlaveEntries(BaseSpawnerMasterInfo info)
		{
			var slaveEntries = new BaseSpawnerSlaveEntry[info.Actors.Length];

			for (int i = 0; i < slaveEntries.Length; i++)
				slaveEntries[i] = new BaseSpawnerSlaveEntry();

			return slaveEntries;
		}

		protected override void Created(Actor self)
		{
			base.Created(self);

			facing = self.TraitOrDefault<IFacing>();

			reloadModifiers = self.TraitsImplementing<IReloadModifier>().ToArray();
		}

		/// <summary>
		/// Replenish destoyed slaves or create new ones from nothing.
		/// Follows policy defined by Info.OneShotSpawn.
		/// </summary>
		public void Replenish(Actor self, BaseSpawnerSlaveEntry[] slaveEntries)
		{
			if (Info.SpawnAllAtOnce)
			{
				foreach (var se in slaveEntries)
					if (!se.IsValid)
						Replenish(self, se);
			}
			else
			{
				BaseSpawnerSlaveEntry entry = SelectEntryToSpawn(slaveEntries);

				// All are alive and well.
				if (entry == null)
					return;

				Replenish(self, entry);
			}
		}

		/// <summary>
		/// Replenish one slave entry.
		/// </summary>
		public virtual void Replenish(Actor self, BaseSpawnerSlaveEntry entry)
		{
			if (entry.IsValid)
				throw new InvalidOperationException("Replenish must not be run on a valid entry!");

			// Some members are missing. Create a new one.
			var slave = self.World.CreateActor(false, entry.ActorName,
				new TypeDictionary { new OwnerInit(self.Owner) });

			// Initialize slave entry
			InitializeSlaveEntry(slave, entry);
			entry.SpawnerSlave.LinkMaster(entry.Actor, self, this);
		}

		/// <summary>
		/// Slave entry initializer function.
		/// Override this function from derived classes to initialize their own specific stuff.
		/// </summary>
		public virtual void InitializeSlaveEntry(Actor slave, BaseSpawnerSlaveEntry entry)
		{
			entry.Actor = slave;
			entry.SpawnerSlave = slave.Trait<BaseSpawnerSlave>();
		}

		protected BaseSpawnerSlaveEntry SelectEntryToSpawn(BaseSpawnerSlaveEntry[] slaveEntries)
		{
			// If any thing is marked dead or null, that's a candidate.
			var candidates = slaveEntries.Where(m => !m.IsValid);
			if (!candidates.Any())
				return null;

			return candidates.Random(self.World.SharedRandom);
		}

		public virtual void OnOwnerChanged(Actor self, Player oldOwner, Player newOwner)
		{
			self.World.AddFrameEndTask(w =>
			{
				foreach (var slaveEntry in SlaveEntries)
					if (slaveEntry.IsValid)
						slaveEntry.SpawnerSlave.OnMasterOwnerChanged(slaveEntry.Actor, oldOwner, newOwner, Info.SlaveDisposalOnOwnerChange);
			});
		}

		void INotifyActorDisposing.Disposing(Actor self)
		{
			// Just dispose them regardless of slave disposal options.
			foreach (var slaveEntry in SlaveEntries)
				if (slaveEntry.IsValid)
					slaveEntry.Actor.Dispose();
		}

		public virtual void SpawnIntoWorld(Actor self, Actor slave, WPos centerPosition)
		{
			var exit = self.RandomExitOrDefault(self.World, null);
			SetSpawnedFacing(slave, self, exit);

			self.World.AddFrameEndTask(w =>
			{
				if (self.IsDead)
					return;

				var spawnOffset = exit == null ? WVec.Zero : exit.Info.SpawnOffset;
				slave.Trait<IPositionable>().SetVisualPosition(slave, centerPosition + spawnOffset);

				var location = self.World.Map.CellContaining(centerPosition + spawnOffset);

				var mv = slave.Trait<IMove>();
				slave.QueueActivity(mv.ReturnToCell(slave));

				slave.QueueActivity(mv.MoveTo(location, 2));

				w.Add(slave);
			});
		}

		void SetSpawnedFacing(Actor spawned, Actor spawner, Exit exit)
		{
			WAngle facingOffset = facing == null ? WAngle.Zero : facing.Facing;

			var exitFacing = exit != null ? exit.Info.Facing : 0;

			var spawnFacing = spawned.TraitOrDefault<IFacing>();
			if (spawnFacing != null)
				spawnFacing.Facing = WAngle.FromFacing((facingOffset.Facing + exitFacing) % 256);

			foreach (var t in spawned.TraitsImplementing<Turreted>())
				t.TurretFacing = (facingOffset.Facing + exitFacing) % 256;
		}

		public void StopSlaves()
		{
			foreach (var slaveEntry in SlaveEntries)
			{
				if (!slaveEntry.IsValid)
					continue;

				slaveEntry.SpawnerSlave.Stop(slaveEntry.Actor);
			}
		}

		public virtual void OnSlaveKilled(Actor self, Actor slave) { }

		void INotifyKilled.Killed(Actor self, AttackInfo e)
		{
			Killed(self, e);
		}

		protected virtual void Killed(Actor self, AttackInfo e)
		{
			// Notify slaves.
			foreach (var slaveEntry in SlaveEntries)
				if (slaveEntry.IsValid)
					slaveEntry.SpawnerSlave.OnMasterKilled(slaveEntry.Actor, e.Attacker, Info.SlaveDisposalOnKill);
		}
	}
}
