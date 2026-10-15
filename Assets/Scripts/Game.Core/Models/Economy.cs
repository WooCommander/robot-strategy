using System;
using Game.Core.Types;

namespace Game.Core.Models
{
    /// <summary>Игровое время: часы на экране, доход начисляется в начале каждого игрового часа.</summary>
    public static class GameClock
    {
        public const float SecondsPerHour = 5f;
        public const int HoursPerDay = 24;
        private const float Epsilon = 0.0001f;

        /// <summary>Сколько полных игровых часов прошло с начала матча.</summary>
        public static int GetElapsedHours(float matchSeconds) => (int)Math.Floor((matchSeconds + Epsilon) / SecondsPerHour);
        public static int GetDay(float matchSeconds) => GetElapsedHours(matchSeconds) / HoursPerDay + 1;
        public static int GetHourOfDay(float matchSeconds) => GetElapsedHours(matchSeconds) % HoursPerDay;
        public static float GetHourProgress(float matchSeconds) => (matchSeconds % SecondsPerHour) / SecondsPerHour;
    }

    /// <summary>Стартовые запасы и почасовой доход. Значения прототипа, уточняются плейтестом.</summary>
    public static class EconomyRules
    {
        public const int StartingGeneral = 10;
        public const int BaseGeneralPerHour = 1;
        public const int FactoryResourcePerHour = 1;

        public static ResourceType GetProducedResource(BuildingState building) =>
            building.Type == BuildingType.Base ? ResourceType.General : building.ProducedResource;

        public static int GetHourlyIncome(BuildingState building) =>
            building.Type == BuildingType.Base ? BaseGeneralPerHour : FactoryResourcePerHour;
    }

    /// <summary>Склад ресурсов стороны: General и шесть специализированных типов.</summary>
    [Serializable]
    public sealed class ResourceStock
    {
        public const int TypeCount = 7;
        private readonly int[] _amounts = new int[TypeCount];

        public int this[ResourceType type]
        {
            get => _amounts[(int)type];
            set => _amounts[(int)type] = Math.Max(0, value);
        }

        public int Total
        {
            get
            {
                int total = 0;
                foreach (int amount in _amounts) total += amount;
                return total;
            }
        }

        public void Add(ResourceType type, int amount) => this[type] += amount;

        public void Add(int[] amounts)
        {
            if (amounts == null) return;
            for (int index = 0; index < TypeCount && index < amounts.Length; index++) _amounts[index] += Math.Max(0, amounts[index]);
        }

        public int[] ToArray() => (int[])_amounts.Clone();

        public static ResourceStock FromArray(int[] amounts)
        {
            var stock = new ResourceStock();
            stock.Add(amounts);
            return stock;
        }

        /// <summary>
        /// Планирует оплату: каждая деталь сначала берёт ресурс своего типа, недостачу покрывает General.
        /// Возвращает false, если General не хватает; склад при этом не меняется.
        /// </summary>
        public bool TryPlanPayment(int[] costByType, out int[] payment)
        {
            payment = new int[TypeCount];
            if (costByType == null) return true;
            int generalNeeded = costByType.Length > 0 ? costByType[(int)ResourceType.General] : 0;
            for (int index = 1; index < TypeCount && index < costByType.Length; index++)
            {
                int fromOwnType = Math.Min(_amounts[index], costByType[index]);
                payment[index] = fromOwnType;
                generalNeeded += costByType[index] - fromOwnType;
            }
            payment[(int)ResourceType.General] = generalNeeded;
            return generalNeeded <= _amounts[(int)ResourceType.General];
        }

        public void Spend(int[] payment)
        {
            if (payment == null) return;
            for (int index = 0; index < TypeCount && index < payment.Length; index++)
            {
                if (payment[index] > _amounts[index]) throw new InvalidOperationException($"Недостаточно ресурса {(ResourceType)index}.");
            }
            for (int index = 0; index < TypeCount && index < payment.Length; index++) _amounts[index] -= payment[index];
        }
    }
}
