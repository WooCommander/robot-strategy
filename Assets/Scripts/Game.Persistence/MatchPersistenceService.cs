using Game.Core.Models;

namespace Game.Persistence
{
    public class MatchPersistenceService
    {
        public string Serialize(MatchState state)
        {
            // Сериализация JSON снимка (Этап 5)
            return string.Empty;
        }

        public MatchState Deserialize(string json)
        {
            return new MatchState();
        }
    }
}
