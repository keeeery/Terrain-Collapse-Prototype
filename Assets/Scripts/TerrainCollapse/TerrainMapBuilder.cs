using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TerrainCollapsePrototype
{
    [Serializable]
    public sealed class TerrainMapGenerationSettings
    {
        public string Name = "DebugTerrainMap";
        public int Width = 100;
        public int Height = 105;
        public int SolidDepth = 100;
        public float CellSize = 1f;
        public Vector2 Origin = new(-50f, -99f);
        public int BedrockDepth = 1;
        public TerrainTileType BaseTileType = TerrainTileType.Ground;
        public bool UseRandomSeed;
        public int Seed = 12345;
        public bool AddCollapseTestStructure = true;
    }

    /// <summary>모든 생성 규칙이 동일한 시드 난수열을 공유하도록 보관하는 Context.</summary>
    public sealed class TerrainMapGenerationContext
    {
        public int Seed { get; }
        public System.Random Random { get; }

        public TerrainMapGenerationContext(int seed)
        {
            Seed = seed;
            Random = new System.Random(seed);
        }
    }

    /// <summary>광물 군집, 동굴 등 독립적인 절차 생성 단계를 추가하기 위한 확장 지점.</summary>
    public abstract class TerrainMapGenerationRule : ScriptableObject
    {
        public abstract void Apply(TerrainMapFile map, TerrainMapGenerationContext context);
    }

    /// <summary>디버깅용 JSON 맵을 생성하며 추후 시드 기반 생성 규칙을 순서대로 합성한다.</summary>
    public sealed class TerrainMapBuilder : MonoBehaviour
    {
        [SerializeField] private TerrainMapGenerationSettings settings = new();
        [SerializeField] private List<TerrainMapGenerationRule> generationRules = new();
        [SerializeField] private string outputPath = "Assets/TerrainCollapseGenerated/DebugTerrainMap.json";
        [Header("Map Painter")]
        [SerializeField] private TerrainGridWorld targetWorld;
        [SerializeField] private TerrainManager terrainManager;
        [SerializeField] private Camera painterCamera;
        [SerializeField] private TerrainTileType paintType = TerrainTileType.Ground;
        [SerializeField] private Color gridColor = new(1f, 1f, 1f, 0.2f);
        [SerializeField] private bool showPainterPanel = true;

        private readonly Rect painterPanelRect = new(10f, 10f, 270f, 250f);
        private TerrainMapFile editingMap;
        private GameObject gridOverlay;
        private Mesh gridMesh;
        private Material gridMaterial;
        private Vector2Int lastPaintedCell = new(int.MinValue, int.MinValue);
        private bool painterMode;
        private float previousTimeScale = 1f;
        private bool previousTerrainManagerEnabled;

        public void Configure(
            TerrainGridWorld world,
            TerrainManager manager,
            Camera camera)
        {
            targetWorld = world;
            terrainManager = manager;
            painterCamera = camera;
        }

        private void Update()
        {
            if (painterMode == false || Mouse.current == null) return;
            Vector2 pointer = Mouse.current.position.ReadValue();
            Vector2 guiPointer = new(pointer.x, Screen.height - pointer.y);
            if (showPainterPanel && painterPanelRect.Contains(guiPointer)) return;

            bool painting = Mouse.current.leftButton.isPressed;
            bool erasing = Mouse.current.rightButton.isPressed;
            if (painting == false && erasing == false)
            {
                lastPaintedCell = new Vector2Int(int.MinValue, int.MinValue);
                return;
            }

            Vector3 worldPosition = painterCamera.ScreenToWorldPoint(
                new Vector3(pointer.x, pointer.y,
                    Mathf.Abs(targetWorld.transform.position.z - painterCamera.transform.position.z)));
            Vector2Int cell = targetWorld.WorldToCell(worldPosition);
            if (IsInsideMap(cell) == false || cell == lastPaintedCell) return;

            if (erasing) targetWorld.RemoveCell(cell);
            else targetWorld.SetCell(cell, paintType);
            lastPaintedCell = cell;
        }

        private void OnGUI()
        {
            if (showPainterPanel == false) return;
            GUILayout.BeginArea(painterPanelRect, GUI.skin.box);
            GUILayout.Label("Terrain Map Painter");

            if (painterMode == false)
            {
                if (GUILayout.Button("Begin Painter Mode")) BeginPainterMode();
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label($"Map: {editingMap.Name} ({editingMap.Width} x {editingMap.Height})");
            GUILayout.Label($"Paint Type: {paintType}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Previous Type")) SelectPreviousPaintType();
            if (GUILayout.Button("Next Type")) SelectNextPaintType();
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Create Empty Grid")) CreateEmptyEditableMap();
            if (GUILayout.Button("Generate From Rules")) GenerateEditableMap();
            if (GUILayout.Button("Save Current Grid To JSON")) SavePaintedMapJson();
            if (GUILayout.Button("End Painter Mode")) EndPainterMode();
            GUILayout.Label("Left Drag: Paint / Right Drag: Erase");
            GUILayout.EndArea();
        }

        [ContextMenu("Begin Painter Mode")]
        public void BeginPainterMode()
        {
            if (editingMap == null) CreateEmptyEditableMap();
            painterMode = true;
            previousTimeScale = Time.timeScale;
            previousTerrainManagerEnabled = terrainManager.enabled;
            Time.timeScale = 0f;
            terrainManager.enabled = false;
            gridOverlay.SetActive(true);
        }

        [ContextMenu("End Painter Mode")]
        public void EndPainterMode()
        {
            painterMode = false;
            terrainManager.enabled = previousTerrainManagerEnabled;
            Time.timeScale = previousTimeScale;
            gridOverlay.SetActive(false);
        }

        [ContextMenu("Create Empty Editable Map")]
        public void CreateEmptyEditableMap()
        {
            int seed = settings.UseRandomSeed ? Environment.TickCount : settings.Seed;
            editingMap = CreateEmptyMap(settings, seed);
            targetWorld.LoadMap(editingMap);
            RebuildGridOverlay();
        }

        [ContextMenu("Generate Editable Map From Rules")]
        public void GenerateEditableMap()
        {
            editingMap = Build(settings, generationRules);
            targetWorld.LoadMap(editingMap);
            RebuildGridOverlay();
        }

        [ContextMenu("Save Painted Map JSON")]
        public void SavePaintedMapJson()
        {
            TerrainMapFile map = CaptureCurrentGrid();
            SaveMapJson(map);
        }

        [ContextMenu("Generate Debug Map JSON")]
        public void GenerateDebugMapJson()
        {
            TerrainMapFile map = Build(settings, generationRules);
            SaveMapJson(map);
        }

        private void SaveMapJson(TerrainMapFile map)
        {
            string path = ResolveOutputPath(outputPath);
            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory) == false) Directory.CreateDirectory(directory);
            File.WriteAllText(path, map.ToJson());

#if UNITY_EDITOR
            AssetDatabase.Refresh();
#endif
            Debug.Log($"[Terrain Map Builder] JSON 생성 완료: {path}, seed={map.Seed}", this);
        }

        private TerrainMapFile CaptureCurrentGrid()
        {
            TerrainMapFile map = CreateEmptyMap(settings, editingMap.Seed);
            map.Name = editingMap.Name;
            foreach (Vector2Int coord in targetWorld.GetOccupiedCells())
            {
                if (IsInsideMap(coord) == false) continue;
                TerrainTileType type = targetWorld.Data.GetCellOrNull(coord).Type;
                map.SetTile(coord, type);
            }
            editingMap = map;
            return map;
        }

        private bool IsInsideMap(Vector2Int cell)
            => cell.x >= 0 && cell.x < editingMap.Width && cell.y >= 0 && cell.y < editingMap.Height;

        private void SelectPreviousPaintType()
        {
            TerrainTileType[] values = (TerrainTileType[])Enum.GetValues(typeof(TerrainTileType));
            int index = Array.IndexOf(values, paintType);
            paintType = values[(index - 1 + values.Length) % values.Length];
            if (paintType == TerrainTileType.Empty) SelectPreviousPaintType();
        }

        private void SelectNextPaintType()
        {
            TerrainTileType[] values = (TerrainTileType[])Enum.GetValues(typeof(TerrainTileType));
            int index = Array.IndexOf(values, paintType);
            paintType = values[(index + 1) % values.Length];
            if (paintType == TerrainTileType.Empty) SelectNextPaintType();
        }

        private void RebuildGridOverlay()
        {
            EnsureGridOverlay();
            int lineCount = editingMap.Width + editingMap.Height + 2;
            Vector3[] vertices = new Vector3[lineCount * 2];
            int vertex = 0;
            float width = editingMap.Width * editingMap.CellSize;
            float height = editingMap.Height * editingMap.CellSize;

            for (int x = 0; x <= editingMap.Width; x++)
            {
                float position = x * editingMap.CellSize;
                vertices[vertex++] = new Vector3(position, 0f);
                vertices[vertex++] = new Vector3(position, height);
            }
            for (int y = 0; y <= editingMap.Height; y++)
            {
                float position = y * editingMap.CellSize;
                vertices[vertex++] = new Vector3(0f, position);
                vertices[vertex++] = new Vector3(width, position);
            }

            gridMesh.Clear();
            gridMesh.vertices = vertices;
            int[] indices = new int[vertices.Length];
            for (int i = 0; i < indices.Length; i++) indices[i] = i;
            gridMesh.SetIndices(indices, MeshTopology.Lines, 0);
            gridMesh.RecalculateBounds();
            gridOverlay.transform.position = new Vector3(editingMap.Origin.x, editingMap.Origin.y, -0.05f);
            gridOverlay.SetActive(painterMode);
        }

        private void EnsureGridOverlay()
        {
            if (gridOverlay != null) return;
            gridOverlay = new GameObject("MapPainterGrid", typeof(MeshFilter), typeof(MeshRenderer));
            gridOverlay.transform.SetParent(transform, false);
            gridMesh = new Mesh { name = "MapPainterGridMesh" };
            gridOverlay.GetComponent<MeshFilter>().sharedMesh = gridMesh;
            gridMaterial = new Material(Shader.Find("Sprites/Default")) { color = gridColor };
            MeshRenderer renderer = gridOverlay.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = gridMaterial;
            renderer.sortingOrder = 20;
        }

        private void OnDestroy()
        {
            if (painterMode) Time.timeScale = previousTimeScale;
            if (gridMesh != null) Destroy(gridMesh);
            if (gridMaterial != null) Destroy(gridMaterial);
        }

        public static TerrainMapFile Build(
            TerrainMapGenerationSettings settings,
            IReadOnlyList<TerrainMapGenerationRule> rules)
        {
            int seed = settings.UseRandomSeed ? Environment.TickCount : settings.Seed;
            TerrainMapGenerationContext context = new(seed);
            TerrainMapFile map = CreateEmptyMap(settings, seed);
            FillBaseTerrain(map, settings);

            if (settings.AddCollapseTestStructure)
                AddCollapseTestStructure(map, settings);

            if (rules != null)
            {
                foreach (TerrainMapGenerationRule rule in rules)
                    rule.Apply(map, context);
            }

            return map;
        }

        private static TerrainMapFile CreateEmptyMap(TerrainMapGenerationSettings settings, int seed)
        {
            TerrainMapFile map = new()
            {
                Name = settings.Name,
                Seed = seed,
                Width = settings.Width,
                Height = settings.Height,
                CellSize = settings.CellSize,
                Origin = settings.Origin,
                Tiles = new List<TerrainTileType>(settings.Width * settings.Height)
            };

            for (int i = 0; i < settings.Width * settings.Height; i++)
                map.Tiles.Add(TerrainTileType.Empty);
            return map;
        }

        private static void FillBaseTerrain(TerrainMapFile map, TerrainMapGenerationSettings settings)
        {
            int depth = Mathf.Clamp(settings.SolidDepth, 0, settings.Height);
            int bedrockDepth = Mathf.Clamp(settings.BedrockDepth, 0, depth);
            for (int y = 0; y < depth; y++)
            for (int x = 0; x < settings.Width; x++)
            {
                TerrainTileType type = y < bedrockDepth
                    ? TerrainTileType.Bedrock
                    : settings.BaseTileType;
                map.SetTile(new Vector2Int(x, y), type);
            }
        }

        private static void AddCollapseTestStructure(
            TerrainMapFile map,
            TerrainMapGenerationSettings settings)
        {
            int centerX = settings.Width / 2;
            int surfaceY = settings.SolidDepth - 1;
            for (int y = 4; y <= 5; y++)
            for (int x = -4; x <= 2; x++)
                SetTileInsideBounds(map, new Vector2Int(centerX + x, surfaceY + y), settings.BaseTileType);

            for (int y = 1; y <= 3; y++)
            for (int x = -1; x <= 0; x++)
                SetTileInsideBounds(map, new Vector2Int(centerX + x, surfaceY + y), settings.BaseTileType);
        }

        private static void SetTileInsideBounds(
            TerrainMapFile map,
            Vector2Int coord,
            TerrainTileType type)
        {
            bool inside = coord.x >= 0 && coord.x < map.Width && coord.y >= 0 && coord.y < map.Height;
            if (inside) map.SetTile(coord, type);
        }

        private static string ResolveOutputPath(string configuredPath)
        {
#if UNITY_EDITOR
            if (Path.IsPathRooted(configuredPath)) return configuredPath;
            return Path.GetFullPath(configuredPath);
#else
            return Path.Combine(Application.persistentDataPath, Path.GetFileName(configuredPath));
#endif
        }
    }
}
