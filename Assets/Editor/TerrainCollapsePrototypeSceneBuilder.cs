using System.IO;
using System.Collections.Generic;
using TerrainCollapsePrototype;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// 기술 검증에 필요한 Tile 자산, 테스트 맵, Manager, Camera, Player를 한 번에 생성한다.
/// 생성된 모든 참조는 코드에서 연결하므로 Inspector Drag &amp; Drop이 필요 없다.
/// </summary>
public static class TerrainCollapsePrototypeSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/TerrainCollapsePrototype.unity";
    private const string GeneratedFolder = "Assets/TerrainCollapseGenerated";
    private const string TexturePath = GeneratedFolder + "/PrototypeTile.png";
    private const string TilePath = GeneratedFolder + "/PrototypeTile.asset";

    /// <summary>빈 Scene을 만들고 프로토타입 전체 구성을 저장한다.</summary>
    [MenuItem("Tools/Prototype/Build Terrain Collapse Test Scene")]
    public static void BuildScene()
    {
        EnsureFolder(GeneratedFolder);
        Tile tile = CreateOrLoadTile();
        if (tile == null || tile.sprite == null)
            throw new InvalidDataException("Prototype Tile 또는 Sprite를 불러오지 못했습니다.");

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "TerrainCollapsePrototype";

        CreateCamera(out Camera camera);
        Tilemap ground = CreateTerrain(tile);

        // 런타임 역할별 Manager를 분리하고 서로 필요한 참조만 연결한다.
        var managers = new GameObject("Managers");
        TerrainManager terrainManager = new GameObject("TerrainManager").AddComponent<TerrainManager>();
        terrainManager.transform.SetParent(managers.transform);
        CollapseManager collapseManager = new GameObject("CollapseManager").AddComponent<CollapseManager>();
        collapseManager.transform.SetParent(managers.transform);
        TerrainSampler sampler = new GameObject("TerrainSampler").AddComponent<TerrainSampler>();
        sampler.transform.SetParent(managers.transform);

        terrainManager.Configure(ground, tile, collapseManager, camera);
        collapseManager.Configure(terrainManager, sampler);
        sampler.Configure(ground, tile);
        CreatePlayer(tile.sprite);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        Selection.activeGameObject = ground.gameObject;
        AssetDatabase.SaveAssets();
        Debug.Log($"[Terrain Collapse] Test scene built and saved: {ScenePath}");
    }

    private static Tilemap CreateTerrain(Tile tile)
    {
        // GroundTilemap Collider들은 CompositeCollider2D로 합쳐 경계 사이의 불필요한 충돌을 줄인다.
        var gridObject = new GameObject("Grid", typeof(Grid));
        Grid grid = gridObject.GetComponent<Grid>();
        grid.cellSize = Vector3.one;

        var groundObject = new GameObject("GroundTilemap", typeof(Tilemap), typeof(TilemapRenderer),
            typeof(Rigidbody2D), typeof(TilemapCollider2D), typeof(CompositeCollider2D));
        groundObject.transform.SetParent(gridObject.transform, false);
        Rigidbody2D body = groundObject.GetComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Static;
        TilemapCollider2D tileCollider = groundObject.GetComponent<TilemapCollider2D>();
        tileCollider.compositeOperation = Collider2D.CompositeOperation.Merge;
        CompositeCollider2D composite = groundObject.GetComponent<CompositeCollider2D>();
        composite.geometryType = CompositeCollider2D.GeometryType.Polygons;

        Tilemap map = groundObject.GetComponent<Tilemap>();
        // 바닥 20칸, 상부 7x2칸, 중앙 지지대 2x3칸을 배치한다.
        var cells = new List<Vector3Int>(40);
        for (int x = -10; x < 10; x++) cells.Add(new Vector3Int(x, 0, 0));
        for (int y = 4; y <= 5; y++)
        for (int x = -4; x <= 2; x++) cells.Add(new Vector3Int(x, y, 0));
        for (int y = 1; y <= 3; y++)
        for (int x = -1; x <= 0; x++) cells.Add(new Vector3Int(x, y, 0));

        var tiles = new TileBase[cells.Count];
        for (int i = 0; i < tiles.Length; i++) tiles[i] = tile;
        map.SetTiles(cells.ToArray(), tiles);
        map.CompressBounds();
        map.RefreshAllTiles();
        EditorUtility.SetDirty(map);

        // 성공 로그 전에 직렬화 대상 Tilemap에 실제로 40개 셀이 들어갔는지 검증한다.
        if (map.GetUsedTilesCount() != 1 || CountOccupiedCells(map) != cells.Count)
            throw new InvalidDataException($"GroundTilemap 생성 검증 실패: 기대 {cells.Count}개 타일.");
        return map;
    }

    private static int CountOccupiedCells(Tilemap map)
    {
        int count = 0;
        foreach (Vector3Int cell in map.cellBounds.allPositionsWithin)
            if (map.HasTile(cell)) count++;
        return count;
    }

    private static void CreateCamera(out Camera camera)
    {
        var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 3f, -10f);
        camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 7f;
        camera.backgroundColor = new Color(.08f, .1f, .14f);
        camera.clearFlags = CameraClearFlags.SolidColor;
    }

    private static void CreatePlayer(Sprite sprite)
    {
        var player = new GameObject("Player", typeof(SpriteRenderer), typeof(Rigidbody2D),
            typeof(CapsuleCollider2D), typeof(TestPlayerController));
        player.transform.position = new Vector3(-7f, 2f, 0f);
        player.transform.localScale = new Vector3(.75f, 1.5f, 1f);
        SpriteRenderer renderer = player.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = new Color(.25f, .8f, 1f);
        renderer.sortingOrder = 5;
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        body.freezeRotation = true;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        CapsuleCollider2D capsule = player.GetComponent<CapsuleCollider2D>();
        capsule.size = Vector2.one;
    }

    private static Tile CreateOrLoadTile()
    {
        Tile existing = AssetDatabase.LoadAssetAtPath<Tile>(TilePath);
        if (existing != null) return existing;

        if (!File.Exists(TexturePath))
        {
            // 외부 이미지 없이도 실행되도록 32x32 테스트 Sprite를 코드로 생성한다.
            var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            Color face = new Color(.37f, .69f, .35f, 1f);
            Color edge = new Color(.22f, .42f, .2f, 1f);
            var pixels = new Color[32 * 32];
            for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++) pixels[y * 32 + x] = x < 2 || y < 2 ? edge : face;
            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(TexturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport);
        }

        // 한 셀이 정확히 1 Unity Unit이 되도록 32 PPU Point Sprite로 임포트한다.
        var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spritePixelsPerUnit = 32f;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();

        var tile = ScriptableObject.CreateInstance<Tile>();
        tile.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(TexturePath);
        tile.colliderType = Tile.ColliderType.Sprite;
        AssetDatabase.CreateAsset(tile, TilePath);
        EditorUtility.SetDirty(tile);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(TilePath, ImportAssetOptions.ForceSynchronousImport);
        return AssetDatabase.LoadAssetAtPath<Tile>(TilePath);
    }

    private static void EnsureFolder(string path)
    {
        // AssetDatabase.CreateFolder는 한 단계씩만 만들 수 있으므로 경로를 순차 생성한다.
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
