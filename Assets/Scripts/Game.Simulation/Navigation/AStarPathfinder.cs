using System;
using System.Collections.Generic;
using Game.Core.Models;
using Game.Core.Types;

namespace Game.Simulation.Navigation
{
    /// <summary>Сетка проходимости карты для одного класса шасси.</summary>
    public class NavigationGrid
    {
        private readonly int _width;
        private readonly int _height;
        private readonly float _tileSize;
        private readonly float[] _speedMultipliers; // 0 — непроходимо

        public int Width => _width;
        public int Height => _height;
        public float TileSize => _tileSize;
        public MobilityClass Mobility { get; }

        public NavigationGrid(MapDefinition map, MobilityClass mobility = MobilityClass.Tracked)
        {
            _width = map.Width;
            _height = map.Height;
            _tileSize = map.TileSize;
            Mobility = mobility;
            _speedMultipliers = new float[_width * _height];

            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    bool mapEdge = x == 0 || y == 0 || x == _width - 1 || y == _height - 1;
                    _speedMultipliers[y * _width + x] = mapEdge ? 0f : GetTerrainSpeed(map.GetTile(x, y), mobility);
                }
            }
        }

        /// <summary>
        /// Множитель скорости на поверхности; 0 — шасси здесь не пройдёт.
        /// Антиграв летит над любой поверхностью с одной скоростью, гусеницы преодолевают неровности медленнее,
        /// шагающим нужна ровная земля.
        /// </summary>
        public static float GetTerrainSpeed(TileType tile, MobilityClass mobility)
        {
            if (mobility == MobilityClass.Hover) return 1f;
            switch (tile)
            {
                case TileType.Road: return 1.3f;
                case TileType.Bridge: return 1.1f;
                case TileType.Ground: return 1.0f;
                case TileType.Rough: return mobility == MobilityClass.Tracked ? 0.7f : 0f;
                default: return 0f; // скалы, вода, трещины
            }
        }

        public bool IsWalkable(int x, int y)
        {
            if (x < 0 || x >= _width || y < 0 || y >= _height) return false;
            return _speedMultipliers[y * _width + x] > 0f;
        }

        public float GetSpeedMultiplier(int x, int y)
        {
            if (x < 0 || x >= _width || y < 0 || y >= _height) return 0f;
            return _speedMultipliers[y * _width + x];
        }

        public void BlockCircle(SimVector2 center, float radius)
        {
            float radiusSq = radius * radius;
            GridCoord min = WorldToGrid(new SimVector2(center.X - radius, center.Y - radius));
            GridCoord max = WorldToGrid(new SimVector2(center.X + radius, center.Y + radius));
            for (int y = Math.Max(0, min.Y); y <= Math.Min(_height - 1, max.Y); y++)
            {
                for (int x = Math.Max(0, min.X); x <= Math.Min(_width - 1, max.X); x++)
                {
                    if (SimVector2.SqrDistance(GridToWorld(new GridCoord(x, y)), center) > radiusSq) continue;
                    _speedMultipliers[y * _width + x] = 0f;
                }
            }
        }

        public GridCoord WorldToGrid(SimVector2 worldPos) => GridCoord.FromSimVector2(worldPos, _tileSize);
        public SimVector2 GridToWorld(GridCoord grid) => grid.ToSimVector2(_tileSize);
    }

    public class AStarPathfinder
    {
        private static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] Dy = { 0, 0, 1, -1, 1, -1, 1, -1 };
        private static readonly float[] StepCost = { 1f, 1f, 1f, 1f, 1.4142f, 1.4142f, 1.4142f, 1.4142f };

        private readonly NavigationGrid _grid;

        // Буферы переиспользуются между поисками: актуальность ячейки отмечает номер поиска.
        private readonly float[] _gScore;
        private readonly int[] _parent;
        private readonly int[] _openedStamp;
        private readonly int[] _closedStamp;
        private readonly PriorityQueueSimple<Node> _openSet = new PriorityQueueSimple<Node>();
        private readonly List<int> _pathIndices = new List<int>();
        private int _searchStamp;

        private struct Node : IComparable<Node>
        {
            public int Index;
            public float FScore;

            public int CompareTo(Node other) => FScore.CompareTo(other.FScore);
        }

        public AStarPathfinder(NavigationGrid grid)
        {
            _grid = grid;
            int total = grid.Width * grid.Height;
            _gScore = new float[total];
            _parent = new int[total];
            _openedStamp = new int[total];
            _closedStamp = new int[total];
        }

        public NavigationGrid Grid => _grid;

        public bool TryFindPath(SimVector2 startWorld, SimVector2 endWorld, out List<SimVector2> waypoints)
        {
            waypoints = new List<SimVector2>();

            var startGrid = _grid.WorldToGrid(startWorld);
            var endGrid = _grid.WorldToGrid(endWorld);
            bool requestedEndIsWalkable = _grid.IsWalkable(endGrid.X, endGrid.Y);

            if (!_grid.IsWalkable(startGrid.X, startGrid.Y)) return false;
            if (!requestedEndIsWalkable && !TryFindNearestWalkable(endGrid, out endGrid)) return false;

            if (startGrid == endGrid)
            {
                waypoints.Add(_grid.GridToWorld(endGrid));
                return true;
            }

            int w = _grid.Width;
            int startIndex = startGrid.Y * w + startGrid.X;
            int endIndex = endGrid.Y * w + endGrid.X;

            if (++_searchStamp == int.MaxValue)
            {
                Array.Clear(_openedStamp, 0, _openedStamp.Length);
                Array.Clear(_closedStamp, 0, _closedStamp.Length);
                _searchStamp = 1;
            }
            int stamp = _searchStamp;
            _openSet.Clear();
            Open(startIndex, -1, 0f, stamp);
            _openSet.Enqueue(new Node { Index = startIndex, FScore = Heuristic(startGrid.X, startGrid.Y, endGrid.X, endGrid.Y) });

            bool found = false;
            while (_openSet.Count > 0)
            {
                int currIdx = _openSet.Dequeue().Index;
                if (currIdx == endIndex)
                {
                    found = true;
                    break;
                }
                if (_closedStamp[currIdx] == stamp) continue;
                _closedStamp[currIdx] = stamp;

                int cx = currIdx % w;
                int cy = currIdx / w;
                for (int i = 0; i < 8; i++)
                {
                    int nx = cx + Dx[i];
                    int ny = cy + Dy[i];
                    if (!_grid.IsWalkable(nx, ny)) continue;

                    // Защита от срезания угла через диагональ у непроходимой клетки
                    if (i >= 4 && (!_grid.IsWalkable(cx + Dx[i], cy) || !_grid.IsWalkable(cx, cy + Dy[i]))) continue;

                    int nIdx = ny * w + nx;
                    if (_closedStamp[nIdx] == stamp) continue;

                    float tentativeG = _gScore[currIdx] + StepCost[i] / Math.Max(0.1f, _grid.GetSpeedMultiplier(nx, ny));
                    if (_openedStamp[nIdx] == stamp && tentativeG >= _gScore[nIdx]) continue;

                    Open(nIdx, currIdx, tentativeG, stamp);
                    _openSet.Enqueue(new Node { Index = nIdx, FScore = tentativeG + Heuristic(nx, ny, endGrid.X, endGrid.Y) });
                }
            }

            if (!found) return false;

            _pathIndices.Clear();
            for (int trace = endIndex; trace != -1; trace = _parent[trace]) _pathIndices.Add(trace);
            for (int i = _pathIndices.Count - 1; i >= 0; i--)
            {
                int idx = _pathIndices[i];
                waypoints.Add(_grid.GridToWorld(new GridCoord(idx % w, idx / w)));
            }

            // Точную точку оставляем только для исходно проходимой клетки.
            // Иначе робот вернётся в недостижимую позицию за границей или на препятствии.
            if (requestedEndIsWalkable && waypoints.Count > 0)
            {
                waypoints[waypoints.Count - 1] = endWorld;
            }

            return true;
        }

        private void Open(int index, int parent, float gScore, int stamp)
        {
            _openedStamp[index] = stamp;
            _parent[index] = parent;
            _gScore[index] = gScore;
        }

        private bool TryFindNearestWalkable(GridCoord target, out GridCoord result)
        {
            result = target;
            for (int r = 1; r <= 3; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int nx = target.X + dx;
                        int ny = target.Y + dy;
                        if (_grid.IsWalkable(nx, ny))
                        {
                            result = new GridCoord(nx, ny);
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private static float Heuristic(int x1, int y1, int x2, int y2)
        {
            int dx = Math.Abs(x1 - x2);
            int dy = Math.Abs(y1 - y2);
            // Octile distance; делим на максимальный множитель дороги, чтобы эвристика не переоценивала путь.
            return (Math.Max(dx, dy) + 0.4142f * Math.Min(dx, dy)) / 1.3f;
        }
    }

    internal class PriorityQueueSimple<T> where T : IComparable<T>
    {
        private readonly List<T> _data = new List<T>();
        public int Count => _data.Count;

        public void Clear() => _data.Clear();

        public void Enqueue(T item)
        {
            _data.Add(item);
            int ci = _data.Count - 1;
            while (ci > 0)
            {
                int pi = (ci - 1) / 2;
                if (_data[ci].CompareTo(_data[pi]) >= 0) break;
                T tmp = _data[ci]; _data[ci] = _data[pi]; _data[pi] = tmp;
                ci = pi;
            }
        }

        public T Dequeue()
        {
            int li = _data.Count - 1;
            T frontItem = _data[0];
            _data[0] = _data[li];
            _data.RemoveAt(li);
            --li;
            int pi = 0;
            while (true)
            {
                int ci = pi * 2 + 1;
                if (ci > li) break;
                int rc = ci + 1;
                if (rc <= li && _data[rc].CompareTo(_data[ci]) < 0) ci = rc;
                if (_data[pi].CompareTo(_data[ci]) <= 0) break;
                T tmp = _data[pi]; _data[pi] = _data[ci]; _data[ci] = tmp;
                pi = ci;
            }
            return frontItem;
        }
    }
}
