using Game.Core.Commands;
using Game.Core.Models;

namespace Game.AI
{
    public class AiCommander
    {
        private readonly ICommandDispatcher _dispatcher;

        public AiCommander(ICommandDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public void Think(MatchState state)
        {
            // Планирование ИИ будет реализовано на Этапе 4
        }
    }
}
