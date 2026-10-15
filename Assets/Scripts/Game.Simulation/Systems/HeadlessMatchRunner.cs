using System;
using System.Collections.Generic;
using Game.Content.Definitions;
using Game.Core.Commands;
using Game.Core.Models;

namespace Game.Simulation.Systems
{
    /// <summary>Воспроизводит симуляцию без Unity-сцены по фиксированным тикам и журналу команд.</summary>
    public sealed class HeadlessMatchRunner
    {
        private readonly CommandDispatcher _dispatcher;
        private readonly ProductionSystem _production;
        private readonly CombatSystem _combat;
        private readonly CaptureSystem _capture;

        public HeadlessMatchRunner(IReadOnlyDictionary<string, ModuleDefinition> catalog)
        {
            _dispatcher = new CommandDispatcher(catalog);
            _production = new ProductionSystem(catalog);
            _combat = new CombatSystem(catalog);
            _capture = new CaptureSystem();
        }

        public void Advance(MatchState state, ReplayCommandLog log, float deltaSeconds)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (deltaSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            if (!state.IsPaused && state.Result == MatchResult.InProgress)
            {
                if (log != null)
                {
                    foreach (ReplayCommandRecord record in log.GetForTick(state.Tick))
                    {
                        CommandResult result = _dispatcher.Dispatch(record.ToCommand(), state);
                        if (!result.IsSuccess) throw new InvalidOperationException($"Replay-команда на тике {state.Tick} отклонена: {result.ErrorReason}");
                    }
                }
                _production.Update(state, deltaSeconds);
                _combat.Update(state, deltaSeconds);
                _capture.Update(state, deltaSeconds);
                state.MatchTimeSeconds += deltaSeconds;
                state.Tick++;
            }
        }

        public void RunTicks(MatchState state, ReplayCommandLog log, int count, float deltaSeconds)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            for (int tick = 0; tick < count; tick++) Advance(state, log, deltaSeconds);
        }
    }
}
