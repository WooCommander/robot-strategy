using System;
using System.Collections.Generic;
using Game.Core.Models;
using Game.Core.Types;

namespace Game.Simulation.Navigation
{
    public class NavigationGrid
    {
        private readonly int _width;
        private readonly int _height;
        private readonly float _tileSize;
        private readonly byte[] _walkability; // 0 - непроходимо, 1 - обычная почва, 2 - дорога, 3 - мост
        private readonly float[] _speedMultipliers;

        public int Width => _width;
        public int Height => _height;
        public float TileSize => _tileSize;

        public NavigationGrid(MapDefinition map)
        {
            _width = map.Width;
            _height = map.Height;
            _tileSize = map.TileSize;
            _walkability = new byte[_width * _height];
            _speedMultipliers = new float[_width * _height];

            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    var tile = map.GetTile(x, y);
                    int idx = y * _width + x;
                    switch (tile)
                    {
                        case TileType.Road:
                            _walkability[idx] = 1;
                            _speedMultipliers[idx] = 1.3f;
                            break;
                        case TileType.Bridge:
                            _walkability[idx] = 1;
                            _speedMultipliers[idx] = 1.1f;
                            break;
                        case TileType.Ground:
                            _walkability[idx] = 1;
                            _speedMultipliers[idx] = 1.0f;
                            break;
                        case TileType.Water:
                        case TileType.Mountain:
                        default:
                            _walkability[idx] = 0;
                            _speedMultipliers[idx] = 0f;
                            break;
                    }
                }
            }
        }

        public bool IsWalkable(int x, int y)
        {
            if (x < 0 || x >= _width || y < 0 || y >= _height) return false;
            return _walkability[y * _width + x] > 0;
        }

        public float GetSpeedMultiplier(int x, int y)
        {
            if (x < 0 || x >= _width || y < 0 || y >= _height) return 0f;
            return _speedMultipliers[y * _width + x];
        }

        public GridCoord WorldToGrid(SimVector2 worldPos) => GridCoord.FromSimVector2(worldPos, _tileSize);
        public SimVector2 GridToWorld(GridCoord grid) => grid.ToSimVector2(_tileSize);
    }

    public class AStarPathfinder
    {
        private readonly NavigationGrid _grid;

        private struct Node : IComparable<Node>
        {
            public int Index;
            public float FScore;

            public int CompareTo(Node other) => FScore.CompareTo(other.FScore);
        }

        public AStarPathfinder(NavigationGrid grid)
        {
            _grid = grid;
        }

        public bool TryFindPath(SimVector2 startWorld, SimVector2 endWorld, out List<SimVector2> waypoints)
        {
            waypoints = new List<SimVector2>();

            var startGrid = _grid.WorldToGrid(startWorld);
            var endGrid = _grid.WorldToGrid(endWorld);

            // Если старт или финиш за пределами сетки или непроходимы
            if (!_grid.IsWalkable(startGrid.X, startGrid.Y))
            {
                return false;
            }

            if (!_grid.IsWalkable(endGrid.X, endGrid.Y))
            {
                // Поиск ближайшей проходимой соседней клетки
                if (!TryFindNearestWalkable(endGrid, out endGrid))
                {
                    return false;
                }
            }

            if (startGrid == endGrid)
            {
                waypoints.Add(endWorld);
                return true;
            }

            int w = _grid.Width;
            int total = w * _grid.Height;

            int startIndex = startGrid.Y * w + startGrid.X;
            int endIndex = endGrid.Y * w + endGrid.X;

            var gScore = new float[total];
            var fScore = new float[total];
            var parent = new int[total];
            var closed = new bool[total];

            for (int i = 0; i < total; i++)
            {
                gScore[i] = float.MaxValue;
                fScore[i] = float.MaxValue;
                parent[i] = -1;
            }

            gScore[startIndex] = 0f;
            fScore[startIndex] = Heuristic(startGrid.X, startGrid.Y, endGrid.X, endGrid.Y);

            var openSet = new PriorityQueueSimple<Node>();
            openSet.Enqueue(new Node { Index = startIndex, FScore = fScore[startIndex] });

            // 4 основных направления + 4 диагональных
            int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
            int[] dy = { 0, 0, 1, -1, 1, -1, 1, -1 };
            float[] cost = { 1f, 1f, 1f, 1f, 1.4142f, 1.4142f, 1.4142f, 1.4142f };

            bool found = false;

            while (openSet.Count > 0)
            {
                var current = openSet.Dequeue();
                int currIdx = current.Index;

                if (currIdx == endIndex)
                {
                    found = true;
                    break;
                }

                if (closed[currIdx]) continue;
                closed[currIdx] = true;

                int cx = currIdx % w;
                int cy = currIdx / w;

                for (int i = 0; i < 8; i++)
                {
                    int nx = cx + dx[i];
                    int ny = cy + dy[i];

                    if (!_grid.IsWalkable(nx, ny)) continue;

                    // Защита от срезания угла через диагональ у непроходимой стены
                    if (i >= 4)
                    {
                        if (!_grid.IsWalkable(cx + dx[i], cy) || !_grid.IsWalkable(cx, cy + dy[i]))
                            continue;
                    }

                    int nIdx = ny * w + nx;
                    if (closed[nIdx]) continue;

                    float speedMultiplier = _grid.GetSpeedMultiplier(nx, ny);
                    float moveCost = (cost[i] / Math.Max(0.1f, speedMultiplier));
                    float tentativeG = gScore[currIdx] + moveCost;

                    if (tentativeG < gScore[nIdx])
                    {
                        parent[nIdx] = currIdx;
                        gScore[nIdx] = tentativeG;
                        float h = Heuristic(nx, ny, endGrid.X, endGrid.Y);
                        fScore[nIdx] = tentativeG + h;
                        openSet.Enqueue(new Node { Index = nIdx, FScore = fScore[nIdx] });
                    }
                }
            }

            if (!found) return false;

            // Восстановление пути
            var pathIndices = new List<int>();
            int trace = endIndex;
            while (trace != -1)
            {
                pathIndices.Add(trace);
                trace = parent[trace];
            }
            pathIndices.Reverse();

            // Преобразование в мировые координаты
            for (int i = 0; i < pathIndices.Count; i++)
            {
                int idx = pathIndices[i];
                var coord = new GridCoord(idx % w, idx / w);
                waypoints.Add(_grid.GridToWorld(coord));
            }

            // Финальная точная позиция клика
            if (waypoints.Count > 0)
            {
                waypoints[waypoints.Count - 1] = endWorld;
            }

            return true;
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
            // Octile distance
            return Math.Max(dx, dy) + 0.4142f * Math.Min(dx, dy);
        }
    }

    internal class PriorityQueueSimple<T> where T : IComparable<T>
    {
        private readonly List<T> _data = new List<T>();
        public int Count => _data.Count;

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
