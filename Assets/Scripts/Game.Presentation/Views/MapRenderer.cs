using System.Collections.Generic;
using UnityEngine;
using Game.Content.Definitions;
using Game.Core.Models;
using Game.Core.Types;

namespace Game.Presentation.Views
{
    public class MapRenderer : MonoBehaviour
    {
        [Header("Materials / Shaders")]
        [SerializeField] private Material _groundMaterial;
        [SerializeField] private Material _roadMaterial;
        [SerializeField] private Material _mountainMaterial;
        [SerializeField] private Material _waterMaterial;
        [SerializeField] private Material _bridgeMaterial;
        [SerializeField] private Material _crackMaterial;
        [SerializeField] private Material _roughMaterial;

        [Header("Prefabs / Templates")]
        [SerializeField] private BuildingView _buildingPrefab;

        private readonly List<BuildingView> _spawnedBuildings = new List<BuildingView>();
        private Texture2D _fogTexture;
        private Color32[] _fogPixels;
        private Material _fogMaterial;
        private Mesh _fogMesh;
        private int _lastFogTick = -1;

        public IReadOnlyList<BuildingView> SpawnedBuildings => _spawnedBuildings;
        public Texture2D FogTexture => _fogTexture;

        public void BuildMap(MapDefinition map)
        {
            if (map == null) return;

            EnsureMaterials();
            GenerateMapGeometry(map);
            SpawnBuildings(map);
            CreateFogOverlay(map);
        }

        private void CreateFogOverlay(MapDefinition map)
        {
            _fogTexture = new Texture2D(map.Width, map.Height, TextureFormat.RGBA32, false)
            {
                name = "FogOfWar", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
            };
            _fogPixels = new Color32[map.Width * map.Height];
            var go = new GameObject("FogOfWarOverlay");
            go.transform.SetParent(transform, false);
            float width = map.Width * map.TileSize, height = map.Height * map.TileSize;
            _fogMesh = new Mesh { name = "FogOfWarMesh" };
            _fogMesh.vertices = new[] { new Vector3(0f, 3.2f, 0f), new Vector3(width, 3.2f, 0f), new Vector3(width, 3.2f, height), new Vector3(0f, 3.2f, height) };
            _fogMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            _fogMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _fogMesh.RecalculateNormals();
            go.AddComponent<MeshFilter>().sharedMesh = _fogMesh;
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent");
            _fogMaterial = new Material(shader) { name = "FogOfWarMaterial", mainTexture = _fogTexture, renderQueue = 3100 };
            go.AddComponent<MeshRenderer>().sharedMaterial = _fogMaterial;
            _lastFogTick = -1;
        }

        public void UpdateFog(MatchState state)
        {
            if (_fogTexture == null || state?.FogOfWar == null || _lastFogTick == state.Tick) return;
            FogOfWarState fog = state.FogOfWar;
            if (fog.Player1Cells == null || fog.Player1Cells.Length != _fogPixels.Length) return;
            for (int i = 0; i < _fogPixels.Length; i++)
                _fogPixels[i] = fog.Player1Cells[i] == 2 ? new Color32(0, 0, 0, 0) :
                    fog.Player1Cells[i] == 1 ? new Color32(0, 0, 0, 155) : new Color32(0, 0, 0, 245);
            _fogTexture.SetPixels32(_fogPixels);
            _fogTexture.Apply(false, false);
            _lastFogTick = state.Tick;
        }

        private void OnDestroy()
        {
            if (_fogTexture != null) Destroy(_fogTexture);
            if (_fogMaterial != null) Destroy(_fogMaterial);
            if (_fogMesh != null) Destroy(_fogMesh);
        }

        private void EnsureMaterials()
        {
            if (_groundMaterial == null)
                _groundMaterial = CreateMaterial("Mat_Ground", new Color(0.28f, 0.48f, 0.22f)); // Сочный зелёный
            if (_roadMaterial == null)
                _roadMaterial = CreateMaterial("Mat_Road", new Color(0.24f, 0.24f, 0.28f));     // Тёмный асфальт
            if (_mountainMaterial == null)
                _mountainMaterial = CreateMaterial("Mat_Mountain", new Color(0.52f, 0.46f, 0.38f)); // Камень
            if (_waterMaterial == null)
                _waterMaterial = CreateMaterial("Mat_Water", new Color(0.18f, 0.45f, 0.85f));   // Вода
            if (_bridgeMaterial == null)
                _bridgeMaterial = CreateMaterial("Mat_Bridge", new Color(0.65f, 0.55f, 0.42f)); // Мост
            if (_crackMaterial == null)
                _crackMaterial = CreateMaterial("Mat_Crack", new Color(0.12f, 0.09f, 0.07f));  // Тёмный разлом
            if (_roughMaterial == null)
                _roughMaterial = CreateMaterial("Mat_Rough", new Color(0.55f, 0.47f, 0.3f));   // Каменистые неровности
        }

        private Material CreateMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null || !shader.isSupported)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }
            if (shader == null || !shader.isSupported)
            {
                shader = Shader.Find("Unlit/Color");
            }
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            var mat = new Material(shader) { name = name };
            mat.color = color;
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }
            if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", color);
            }
            return mat;
        }

        private void GenerateMapGeometry(MapDefinition map)
        {
            // Очищаем старые дочерние объекты
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(transform.GetChild(i).gameObject);
            }

            // Генерируем 5 комбинированных мешей (по одному на каждый тип поверхности) для минимизации Draw Calls
            var tileMeshes = new Dictionary<TileType, (List<Vector3> verts, List<int> tris, List<Vector3> norms)>();
            foreach (TileType t in System.Enum.GetValues(typeof(TileType)))
            {
                tileMeshes[t] = (new List<Vector3>(), new List<int>(), new List<Vector3>());
            }

            float sz = map.TileSize;

            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    var type = map.GetTile(x, y);
                    var (verts, tris, norms) = tileMeshes[type];

                    float heightOffset = GetTileHeight(type);
                    AddTileQuad(verts, tris, norms, x * sz, y * sz, sz, heightOffset, type == TileType.Mountain);
                }
            }

            // Создаем объекты с MeshRenderer для каждого типа тайла
            CreateSubMeshObject("Terrain_Ground", tileMeshes[TileType.Ground], _groundMaterial);
            CreateSubMeshObject("Terrain_Road", tileMeshes[TileType.Road], _roadMaterial);
            CreateSubMeshObject("Terrain_Mountains", tileMeshes[TileType.Mountain], _mountainMaterial);
            CreateSubMeshObject("Terrain_Water", tileMeshes[TileType.Water], _waterMaterial);
            CreateSubMeshObject("Terrain_Bridge", tileMeshes[TileType.Bridge], _bridgeMaterial);
            CreateSubMeshObject("Terrain_Crack", tileMeshes[TileType.Crack], _crackMaterial);
            CreateSubMeshObject("Terrain_Rough", tileMeshes[TileType.Rough], _roughMaterial);
        }

        /// <summary>Высота поверхности клетки в мире; по ней визуал поднимает роботов над рельефом.</summary>
        public static float GetTileHeight(TileType type) => type switch
        {
            TileType.Mountain => 1.5f,
            TileType.Water => -0.2f,
            TileType.Crack => -0.35f,
            TileType.Rough => 0.12f,
            _ => 0f
        };

        private void AddTileQuad(List<Vector3> verts, List<int> tris, List<Vector3> norms, float x, float z, float sz, float h, bool isCube)
        {
            int startIdx = verts.Count;

            if (isCube)
            {
                // Для гор строим призму/куб с верхней и боковыми гранями
                Vector3 p0 = new Vector3(x, 0, z);
                Vector3 p1 = new Vector3(x + sz, 0, z);
                Vector3 p2 = new Vector3(x + sz, 0, z + sz);
                Vector3 p3 = new Vector3(x, 0, z + sz);

                Vector3 t0 = new Vector3(x, h, z);
                Vector3 t1 = new Vector3(x + sz, h, z);
                Vector3 t2 = new Vector3(x + sz, h, z + sz);
                Vector3 t3 = new Vector3(x, h, z + sz);

                // Верхняя грань
                verts.Add(t0); verts.Add(t1); verts.Add(t2); verts.Add(t3);
                norms.Add(Vector3.up); norms.Add(Vector3.up); norms.Add(Vector3.up); norms.Add(Vector3.up);
                tris.Add(startIdx); tris.Add(startIdx + 2); tris.Add(startIdx + 1);
                tris.Add(startIdx); tris.Add(startIdx + 3); tris.Add(startIdx + 2);
            }
            else
            {
                // Обычная плоская плитка
                verts.Add(new Vector3(x, h, z));
                verts.Add(new Vector3(x + sz, h, z));
                verts.Add(new Vector3(x + sz, h, z + sz));
                verts.Add(new Vector3(x, h, z + sz));

                norms.Add(Vector3.up);
                norms.Add(Vector3.up);
                norms.Add(Vector3.up);
                norms.Add(Vector3.up);

                tris.Add(startIdx);
                tris.Add(startIdx + 2);
                tris.Add(startIdx + 1);
                tris.Add(startIdx);
                tris.Add(startIdx + 3);
                tris.Add(startIdx + 2);
            }
        }

        private void CreateSubMeshObject(string name, (List<Vector3> verts, List<int> tris, List<Vector3> norms) meshData, Material mat)
        {
            if (meshData.verts.Count == 0) return;

            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            // Крупная карта может превысить 65535 вершин в одном меше.
            var mesh = new Mesh { name = $"{name}_Mesh", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(meshData.verts);
            mesh.SetTriangles(meshData.tris, 0);
            mesh.SetNormals(meshData.norms);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;

            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
        }

        private void SpawnBuildings(MapDefinition map)
        {
            _spawnedBuildings.Clear();

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
