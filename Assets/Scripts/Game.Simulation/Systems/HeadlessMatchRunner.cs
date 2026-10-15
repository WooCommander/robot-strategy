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
        private readonly IReadOnlyDictionary<string, ModuleDefinition> _catalog;
        private readonly MapDefinition _map;
        private readonly IMatchAgent[] _agents;
        private MatchSimulation _simulation;

        /// <param name="map">Карта матча; без неё движение не моделируется.</param>
        /// <param name="agents">Те же агенты (ИИ), что участвовали в записанном матче.</param>
        public HeadlessMatchRunner(IReadOnlyDictionary<string, ModuleDefinition> catalog, MapDefinition map = null, params IMatchAgent[] agents)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _map = map;
            _agents = agents ?? Array.Empty<IMatchAgent>();
        }

        public void Advance(MatchState state, ReplayCommandLog log, float deltaSeconds)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            GetSimulation(state).Step(deltaSeconds, log);
        }

        public void RunTicks(MatchState state, ReplayCommandLog log, int count, float deltaSeconds = MatchSimulation.FixedDeltaSeconds)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            for (int tick = 0; tick < count; tick++) Advance(state, log, deltaSeconds);
        }

        private MatchSimulation GetSimulation(MatchState state)
        {
            if (_simulation != null && _simulation.State == state) return _simulation;
            _simulation = new MatchSimulation(_catalog, _map, state);
            foreach (IMatchAgent agent in _agents) _simulation.AddAgent(agent);
            return _simulation;
        }
    }
}
