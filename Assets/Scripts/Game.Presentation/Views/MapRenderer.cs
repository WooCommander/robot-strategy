using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Game.Core.Models;
using Game.Core.Types;
using Game.Presentation.Rendering;

namespace Game.Presentation.Views
{
    /// <summary>
    /// Строит визуал карты: рельеф с цветовой картой, воду, мосты, декор, здания и слой тумана войны.
    /// Всё генерируется из <see cref="MapDefinition"/> детерминированно и не влияет на симуляцию.
    /// </summary>
    public class MapRenderer : MonoBehaviour
    {
        /// <summary>Подъём слоя тумана над рельефом: накрывает валуны и кусты, но почти не сдвигается от наклона камеры.</summary>
        private const float FogLift = 0.35f;
        /// <summary>Над скалами (выше порога) туман поднят сильнее: там лежат валуны.</summary>
        private const float RockyFogLift = 0.6f;
        private const float RockyFogThreshold = 0.5f;

        [Header("Prefabs / Templates")]
        [SerializeField] private BuildingView _buildingPrefab;

        private readonly List<BuildingView> _spawnedBuildings = new List<BuildingView>();
        private readonly List<Object> _generatedAssets = new List<Object>();
        private TerrainField _terrain;
        private Texture2D _fogTexture;
        private Color32[] _fogPixels;
        private int _lastFogTick = -1;

        public IReadOnlyList<BuildingView> SpawnedBuildings => _spawnedBuildings;
        public Texture2D FogTexture => _fogTexture;

        public void BuildMap(MapDefinition map)
        {
            if (map == null) return;

            ClearGenerated();
            _terrain = new TerrainField(map);
            BuildTerrain();
            BuildWater(map);
            BuildBridgesAndDecor(map);
            SpawnBuildings(map);
            CreateFogOverlay(map);
        }

        /// <summary>Высота, на которой стоит робот: по рельефу, над водой — по её поверхности.</summary>
        public float SampleSurfaceHeight(float x, float z) => _terrain?.SampleSurface(x, z) ?? 0f;

        public void UpdateFog(MatchState state)
        {
            if (_fogTexture == null || state?.FogOfWar == null || _lastFogTick == state.Tick) return;
            FogOfWarState fog = state.FogOfWar;
            if (fog.Player1Cells == null || fog.Player1Cells.Length != _fogPixels.Length) return;
            for (int i = 0; i < _fogPixels.Length; i++)
                _fogPixels[i] = fog.Player1Cells[i] == 2 ? new Color32(8, 12, 18, 0) :
                    fog.Player1Cells[i] == 1 ? new Color32(8, 12, 18, 150) : new Color32(8, 12, 18, 240);
            _fogTexture.SetPixels32(_fogPixels);
            _fogTexture.Apply(false, false);
            _lastFogTick = state.Tick;
        }

        private void OnDestroy() => ClearGenerated();

        private void ClearGenerated()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
            foreach (Object asset in _generatedAssets)
            {
                if (asset != null) Destroy(asset);
            }
            _generatedAssets.Clear();
            _spawnedBuildings.Clear();
        }

        private T Track<T>(T asset) where T : Object
        {
            _generatedAssets.Add(asset);
            return asset;
        }

        private void BuildTerrain()
        {
            int countX = _terrain.VertexCountX, countZ = _terrain.VertexCountZ;
            var vertices = new Vector3[countX * countZ];
            var uvs = new Vector2[vertices.Length];
            for (int iz = 0; iz < countZ; iz++)
            {
                for (int ix = 0; ix < countX; ix++)
                {
                    int index = iz * countX + ix;
                    vertices[index] = _terrain.GetVertex(ix, iz);
                    uvs[index] = new Vector2((float)ix / (countX - 1), (float)iz / (countZ - 1));
                }
            }

            var mesh = Track(new Mesh { name = "TerrainMesh", indexFormat = IndexFormat.UInt32 });
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = GridTriangles(countX, countZ);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            Texture2D albedo = Track(TerrainTextures.BuildAlbedo(_terrain));
            Texture2D detail = Track(TerrainTextures.BuildDetailAlbedo());
            Texture2D detailNormal = Track(TerrainTextures.BuildNormalMap("TerrainDetailNormal", 128, 8, 6f, 31));
            // Один повтор деталей на две клетки.
            var tiling = new Vector2(_terrain.CellCountX * 0.5f, _terrain.CellCountZ * 0.5f);
            Material material = Track(MaterialFactory.CreateTerrain("Mat_Terrain", albedo, detail, detailNormal, tiling));

            var go = new GameObject("Terrain");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        /// <summary>Индексы регулярной сетки; порядок обхода — лицом вверх в соглашении Unity.</summary>
        private static int[] GridTriangles(int countX, int countZ)
        {
            var triangles = new int[(countX - 1) * (countZ - 1) * 6];
            int t = 0;
            for (int iz = 0; iz < countZ - 1; iz++)
            {
                for (int ix = 0; ix < countX - 1; ix++)
                {
                    int v00 = iz * countX + ix, v10 = v00 + 1, v01 = v00 + countX, v11 = v01 + 1;
                    triangles[t++] = v00; triangles[t++] = v11; triangles[t++] = v10;
                    triangles[t++] = v00; triangles[t++] = v01; triangles[t++] = v11;
                }
            }
            return triangles;
        }

        private void BuildWater(MapDefinition map)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            float size = map.TileSize, y = TerrainField.WaterLevel;
            const float rippleScale = 0.15f;
            for (int cy = 0; cy < map.Height; cy++)
            {
                for (int cx = 0; cx < map.Width; cx++)
                {
                    if (_terrain.GetSurface(cx, cy) != TerrainField.Surface.Water) continue;
                    int start = vertices.Count;
                    float x0 = cx * size, z0 = cy * size, x1 = x0 + size, z1 = z0 + size;
                    vertices.Add(new Vector3(x0, y, z0)); vertices.Add(new Vector3(x1, y, z0));
                    vertices.Add(new Vector3(x1, y, z1)); vertices.Add(new Vector3(x0, y, z1));
                    uvs.Add(new Vector2(x0, z0) * rippleScale); uvs.Add(new Vector2(x1, z0) * rippleScale);
                    uvs.Add(new Vector2(x1, z1) * rippleScale); uvs.Add(new Vector2(x0, z1) * rippleScale);
                    triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
                    triangles.Add(start); triangles.Add(start + 3); triangles.Add(start + 2);
                }
            }
            if (vertices.Count == 0) return;

            var mesh = Track(new Mesh { name = "WaterMesh", indexFormat = IndexFormat.UInt32 });
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();

            Texture2D ripples = Track(TerrainTextures.BuildNormalMap("WaterNormal", 128, 6, 4f, 57));
            Material material = Track(MaterialFactory.CreateWater("Mat_Water", new Color(0.16f, 0.42f, 0.5f, 0.72f), ripples));

            var go = new GameObject("Water");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            go.AddComponent<WaterSurface>().Initialize(material);
        }

        private void BuildBridgesAndDecor(MapDefinition map)
        {
            Texture2D palette = Track(LowPolyMeshBuilder.CreatePaletteTexture());
            Material material = Track(MaterialFactory.CreateTexturedLit("Mat_LowPoly", palette, 0.15f));

            var builder = new LowPolyMeshBuilder();
            float size = map.TileSize;
            for (int cy = 0; cy < map.Height; cy++)
            {
                for (int cx = 0; cx < map.Width; cx++)
                {
                    if (map.GetTile(cx, cy) != TileType.Bridge) continue;
                    var center = new Vector3((cx + 0.5f) * size, 0f, (cy + 0.5f) * size);
                    Matrix4x4 at = Matrix4x4.Translate(center);
                    // Верх настила на уровне земли: по нему едут роботы.
                    builder.Box(at, new Vector3(0f, -0.12f, 0f), new Vector3(size, 0.26f, size), LowPolyMeshBuilder.Swatch.Concrete);
                    builder.Box(at, new Vector3(0f, -0.6f, 0f), new Vector3(0.3f, 0.7f, 0.3f), LowPolyMeshBuilder.Swatch.RockDark);
                    // Перила там, где мост кончается поперёк движения.
                    if (map.GetTile(cx, cy - 1) != TileType.Bridge)
                        builder.Box(at, new Vector3(0f, 0.12f, -size * 0.46f), new Vector3(size, 0.22f, 0.08f), LowPolyMeshBuilder.Swatch.RockDark);
                    if (map.GetTile(cx, cy + 1) != TileType.Bridge)
                        builder.Box(at, new Vector3(0f, 0.12f, size * 0.46f), new Vector3(size, 0.22f, 0.08f), LowPolyMeshBuilder.Swatch.RockDark);
                }
            }
            if (builder.VertexCount > 0)
            {
                var bridges = new GameObject("Bridges");
                bridges.transform.SetParent(transform, false);
                bridges.AddComponent<MeshFilter>().sharedMesh = Track(builder.ToMesh("BridgesMesh"));
                bridges.AddComponent<MeshRenderer>().sharedMaterial = material;
            }

            var decor = new GameObject("Decor");
            decor.transform.SetParent(transform, false);
            DecorationScatter.Build(_terrain, material, decor.transform);
            foreach (MeshFilter filter in decor.GetComponentsInChildren<MeshFilter>()) Track(filter.sharedMesh);
        }

        /// <summary>Туман лежит на рельефе (с небольшим подъёмом) и потому не «съезжает» при наклонной камере.</summary>
        private void CreateFogOverlay(MapDefinition map)
        {
            _fogTexture = Track(new Texture2D(map.Width, map.Height, TextureFormat.RGBA32, false)
            {
                name = "FogOfWar", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            });
            _fogPixels = new Color32[map.Width * map.Height];

            int samples = TerrainField.SamplesPerTile;
            int countX = map.Width * samples + 1, countZ = map.Height * samples + 1;
            var vertices = new Vector3[countX * countZ];
            var uvs = new Vector2[vertices.Length];
            for (int iz = 0; iz < countZ; iz++)
            {
                for (int ix = 0; ix < countX; ix++)
                {
                    // Та же сетка, что у рельефа (со сдвигом на рамку). Высота берётся по максимуму соседних вершин:
                    // валун на краю гребня нависает над склоном и иначе торчал бы из тумана.
                    int tx = ix + TerrainField.Margin * samples, tz = iz + TerrainField.Margin * samples;
                    Vector3 vertex = _terrain.GetVertex(tx, tz);
                    float top = TerrainField.WaterLevel;
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        for (int dx = -1; dx <= 1; dx++) top = Mathf.Max(top, _terrain.GetVertexHeight(tx + dx, tz + dz));
                    }
                    vertex.y = top + (top > RockyFogThreshold ? RockyFogLift : FogLift);
                    int index = iz * countX + ix;
                    vertices[index] = vertex;
                    uvs[index] = new Vector2((float)ix / (countX - 1), (float)iz / (countZ - 1));
                }
            }

            var mesh = Track(new Mesh { name = "FogOfWarMesh", indexFormat = IndexFormat.UInt32 });
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = GridTriangles(countX, countZ);
            mesh.RecalculateBounds();

            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent");
            Material material = Track(new Material(shader) { name = "FogOfWarMaterial", mainTexture = _fogTexture, renderQueue = 3100 });

            var go = new GameObject("FogOfWarOverlay");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _lastFogTick = -1;
        }

        private void SpawnBuildings(MapDefinition map)
        {
            var container = new GameObject("Buildings");
            container.transform.SetParent(transform, false);

            foreach (var bDef in map.Buildings)
            {
                BuildingView bv;
                if (_buildingPrefab != null)
                {
                    bv = Instantiate(_buildingPrefab, container.transform);
                }
                else
                {
                    var bGo = new GameObject();
                    bGo.transform.SetParent(container.transform, false);
                    bv = bGo.AddComponent<BuildingView>();
                }

                bv.Initialize(bDef);
                _spawnedBuildings.Add(bv);
            }
        }
    }
}
