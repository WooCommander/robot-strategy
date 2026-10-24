using System.Collections.Generic;
using UnityEngine;
using Game.Core.Types;

namespace Game.Presentation.Rendering
{
    /// <summary>
    /// Один материал на сторону для роботов и зданий: палитра, где ячейки команды и огни окрашены в цвет стороны.
    /// Все модели стороны делят материал, а захват здания — это смена материала, без перестройки меша.
    /// </summary>
    public static class TeamPalette
    {
        private static readonly Dictionary<PlayerId, Material> Materials = new Dictionary<PlayerId, Material>();

        public static Material Get(PlayerId owner, Color teamColor)
        {
            if (Materials.TryGetValue(owner, out Material cached) && cached != null) return cached;
            Material material = MaterialFactory.CreatePaletteEmissive($"Mat_Team_{owner}",
                LowPolyMeshBuilder.CreatePaletteTexture(teamColor), LowPolyMeshBuilder.CreateEmissionTexture(teamColor), 0.35f, 0.45f);
            Materials[owner] = material;
            return material;
        }
    }
}
