using System.IO;
using TerrainCollapsePrototype;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Unity Tilemap 없이 커스텀 Grid 기반 붕괴 테스트 Scene을 자동 생성한다.</summary>
public static class TerrainCollapsePrototypeSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/TerrainCollapsePrototype.unity";
    private const string GeneratedFolder = "Assets/TerrainCollapseGenerated";
    private const string TexturePath = GeneratedFolder + "/PrototypeTile.png";
    private const string MapJsonPath = GeneratedFolder + "/PerformanceMap.json";
    private const string TileCatalogPath = GeneratedFolder + "/TerrainTileCatalog.asset";

    [MenuItem("Tools/Prototype/Build Terrain Collapse Test Scene")]
    public static void BuildScene()
    {
        EnsureFolder(GeneratedFolder);
        Sprite terrainSprite = CreateOrLoadSprite();
        if (terrainSprite == null) throw new InvalidDataException("Prototype Sprite를 불러오지 못했습니다.");
        TerrainTileCatalog tileCatalog = CreateOrLoadTileCatalog(terrainSprite);
        TerrainMapFile mapFile = TerrainMapFactory.CreatePerformanceMap();
        TextAsset mapJson = SaveAndLoadMapAsset(mapFile);

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "TerrainCollapsePrototype";
        CreateCamera(out Camera camera);
        TerrainGridWorld terrainWorld = CreateTerrainWorld(terrainSprite, tileCatalog, mapJson);

        var managers = new GameObject("Managers");
        TerrainManager terrainManager = new GameObject("TerrainManager").AddComponent<TerrainManager>();
        terrainManager.transform.SetParent(managers.transform);
        CollapseManager collapseManager = new GameObject("CollapseManager").AddComponent<CollapseManager>();
        collapseManager.transform.SetParent(managers.transform);
        TerrainSampler sampler = new GameObject("TerrainSampler").AddComponent<TerrainSampler>();
        sampler.transform.SetParent(managers.transform);
        TerrainMapBuilder mapBuilder = new GameObject("TerrainMapBuilder").AddComponent<TerrainMapBuilder>();
        mapBuilder.transform.SetParent(managers.transform);

        sampler.Configure(terrainWorld);
        terrainManager.Configure(terrainWorld, camera);
        collapseManager.Configure(terrainWorld, sampler);
        mapBuilder.Configure(terrainWorld, terrainManager, camera);

        var coordinatorObject = new GameObject("TerrainCollapseCoordinator");
        coordinatorObject.SetActive(false);
        coordinatorObject.transform.SetParent(managers.transform);
        TerrainCollapseCoordinator coordinator = coordinatorObject.AddComponent<TerrainCollapseCoordinator>();
        coordinator.Configure(terrainManager, collapseManager);
        coordinatorObject.SetActive(true);
        CreatePlayer(terrainSprite);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        Selection.activeGameObject = terrainWorld.gameObject;
        AssetDatabase.SaveAssets();
        Debug.Log($"[Terrain Collapse] Custom Grid test scene saved: {ScenePath}");
    }

    private static TerrainGridWorld CreateTerrainWorld(
        Sprite sprite,
        TerrainTileCatalog tileCatalog,
        TextAsset mapJson)
    {
        var root = new GameObject("TerrainGrid", typeof(Rigidbody2D),
            typeof(TerrainGridWorld), typeof(TerrainMapLoader));
        root.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
        TerrainGridWorld world = root.GetComponent<TerrainGridWorld>();
        world.ConfigureVisual(sprite, Color.white, tileCatalog);
        root.GetComponent<TerrainMapLoader>().Configure(mapJson, world);
        EditorUtility.SetDirty(world);
        return world;
    }

    private static TerrainTileCatalog CreateOrLoadTileCatalog(Sprite terrainSprite)
    {
        TerrainTileCatalog catalog = AssetDatabase.LoadAssetAtPath<TerrainTileCatalog>(TileCatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<TerrainTileCatalog>();
            AssetDatabase.CreateAsset(catalog, TileCatalogPath);
        }

        catalog.Configure(new[]
        {
            new TerrainTileDefinition
            {
                Type = TerrainTileType.Ground,
                Sprite = terrainSprite,
                Color = Color.white,
                CreatesCollider = true
            },
            new TerrainTileDefinition
            {
                Type = TerrainTileType.Bedrock,
                Sprite = terrainSprite,
                Color = new Color(0.3f, 0.3f, 0.35f),
                CreatesCollider = true
            }
        });
        EditorUtility.SetDirty(catalog);
        return catalog;
    }

    private static TextAsset SaveAndLoadMapAsset(TerrainMapFile map)
    {
        File.WriteAllText(MapJsonPath, map.ToJson());
        AssetDatabase.ImportAsset(MapJsonPath, ImportAssetOptions.ForceSynchronousImport);
        TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(MapJsonPath);
        if (asset == null) throw new InvalidDataException($"Map JSON Asset 생성 실패: {MapJsonPath}");
        Debug.Log($"[Terrain Collapse] Data map generated: {MapJsonPath} ({map.Width}x{map.Height})");
        return asset;
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
            typeof(BoxCollider2D), typeof(TestPlayerController));
        player.transform.position = new Vector3(-7f, 2f, 0f);
        player.transform.localScale = new Vector3(.75f, 1.5f, 1f);
        SpriteRenderer renderer = player.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = new Color(.25f, .8f, 1f);
        renderer.sortingOrder = 5;
        player.GetComponent<BoxCollider2D>().size = Vector2.one;
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.useFullKinematicContacts = true;
        body.gravityScale = 0f;
        body.freezeRotation = true;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
    }

    private static Sprite CreateOrLoadSprite()
    {
        if (File.Exists(TexturePath) == false)
        {
            var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            Color face = new(.37f, .69f, .35f, 1f);
            Color edge = new(.22f, .42f, .2f, 1f);
            var pixels = new Color[32 * 32];
            for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++) pixels[y * 32 + x] = x < 2 || y < 2 ? edge : face;
            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(TexturePath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport);
        }

        var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 32f;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(TexturePath);
    }

    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (AssetDatabase.IsValidFolder(next) == false) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
