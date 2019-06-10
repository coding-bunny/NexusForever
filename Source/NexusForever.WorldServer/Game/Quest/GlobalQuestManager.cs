﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using NexusForever.Shared.GameTable;
using NexusForever.Shared.GameTable.Model;
using NLog;

namespace NexusForever.WorldServer.Game.Quest
{
    public class GlobalQuestManager
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// <see cref="DateTime"/> representing the next daily reset.
        /// </summary>
        public static DateTime NextDailyReset { get; private set; }

        /// <summary>
        /// <see cref="DateTime"/> representing the next weekly reset.
        /// </summary>
        public static DateTime NextWeeklyReset { get; private set; }

        private static ImmutableDictionary<ushort, QuestInfo> questInfoStore;
        private static ImmutableDictionary<ushort, ImmutableList<CommunicatorMessage>> communicatorQuestStore;
        private static ImmutableDictionary<ushort, ImmutableList<uint>> questGiverStore;
        private static ImmutableDictionary<ushort, ImmutableList<uint>> questReceiverStore;

        public static void Initialise()
        {
            Stopwatch sw = Stopwatch.StartNew();

            CalculateResetTimes(); 
            InitialiseQuestInfo();
            InitialiseQuestRelations();
            InitialiseCommunicator();

            log.Info($"Cached {questInfoStore.Count} quests in {sw.ElapsedMilliseconds}ms.");
        }

        private static void CalculateResetTimes()
        {
            DateTime now = DateTime.UtcNow;
            var resetTime = new DateTime(now.Year, now.Month, now.Day, 10, 0, 0);

            // calculate daily reset (every day 10AM UTC)
            NextDailyReset = resetTime.AddDays(1);

            // calculate weekly reset (every tuesday 10AM UTC)
            NextWeeklyReset = resetTime.AddDays((DayOfWeek.Tuesday - now.DayOfWeek + 7) % 7);
        }

        private static void InitialiseQuestInfo()
        {
            var builder = ImmutableDictionary.CreateBuilder<ushort, QuestInfo>();
            foreach (Quest2Entry entry in GameTableManager.Quest2.Entries)
                builder.Add((ushort)entry.Id, new QuestInfo(entry));

            questInfoStore = builder.ToImmutable();
        }

        private static void InitialiseQuestRelations()
        {
            var questGivers = new Dictionary<ushort, List<uint>>();
            var questReceivers = new Dictionary<ushort, List<uint>>();

            foreach (Creature2Entry entry in GameTableManager.Creature2.Entries)
            {
                // ReSharper disable once PossibleInvalidCastExceptionInForeachLoop
                foreach (ushort questId in entry.QuestIdGiven.Where(q => q != 0u))
                {
                    if (!questGivers.ContainsKey(questId))
                        questGivers.Add(questId, new List<uint>());

                    questGivers[questId].Add(entry.Id);
                }

                // ReSharper disable once PossibleInvalidCastExceptionInForeachLoop
                foreach (ushort questId in entry.QuestIdReceive.Where(q => q != 0u))
                {
                    if (!questReceivers.ContainsKey(questId))
                        questReceivers.Add(questId, new List<uint>());

                    questReceivers[questId].Add(entry.Id);
                }
            }

            questGiverStore = questGivers.ToImmutableDictionary(k => k.Key, v => v.Value.ToImmutableList());
            questReceiverStore = questReceivers.ToImmutableDictionary(k => k.Key, v => v.Value.ToImmutableList());
        }

        private static void InitialiseCommunicator()
        {
            var builder = new Dictionary<ushort, List<CommunicatorMessage>>();
            foreach (CommunicatorMessagesEntry entry in GameTableManager.CommunicatorMessages.Entries
                .Where(e => e.QuestIdDelivered != 0u))
            {
                var quest = new CommunicatorMessage(entry);
                if (!builder.ContainsKey(quest.Id))
                    builder.Add(quest.Id, new List<CommunicatorMessage>());

                builder[quest.Id].Add(quest);
            }

            communicatorQuestStore = builder.ToImmutableDictionary(e => e.Key, e => e.Value.ToImmutableList());
        }

        public static void Update(double lastTick)
        {
            DateTime now = DateTime.UtcNow;
            if (NextDailyReset <= now)
                NextDailyReset = NextDailyReset.AddDays(1);

            if (NextWeeklyReset <= now)
                NextWeeklyReset = NextWeeklyReset.AddDays(7);
        }

        /// <summary>
        /// Return <see cref="QuestInfo"/> for supplied quest.
        /// </summary>
        public static QuestInfo GetQuestInfo(ushort questId)
        {
            return questInfoStore.TryGetValue(questId, out QuestInfo questInfo) ? questInfo : null;
        }

        /// <summary>
        /// Return a collection of creatures that start the supplied quest.
        /// </summary>
        public static IEnumerable<uint> GetQuestGivers(ushort questId)
        {
            return questGiverStore.TryGetValue(questId, out ImmutableList<uint> creatureIds) ? creatureIds : Enumerable.Empty<uint>();
        }

        /// <summary>
        /// Return a collection of creatures that finish the supplied quest.
        /// </summary>
        public static IEnumerable<uint> GetQuestReceivers(ushort questId)
        {
            return questReceiverStore.TryGetValue(questId, out ImmutableList<uint> creatureIds) ? creatureIds : Enumerable.Empty<uint>();
        }

        /// <summary>
        /// Return a collection of communicator messages that start the supplied quest.
        /// </summary>
        public static IEnumerable<CommunicatorMessage> GetQuestCommunicatorMessages(ushort questId)
        {
            return communicatorQuestStore.TryGetValue(questId, out ImmutableList<CommunicatorMessage> creatureIds)
                ? creatureIds : Enumerable.Empty<CommunicatorMessage>();
        }
    }
}
