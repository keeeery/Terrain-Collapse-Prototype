using UnityEngine;

namespace TerrainCollapsePrototype
{
    /// <summary>TextAsset JSON을 읽어 TerrainGridWorld의 런타임 데이터와 표현을 생성한다.</summary>
    [RequireComponent(typeof(TerrainGridWorld))]
    public sealed class TerrainMapLoader : MonoBehaviour
    {
        [SerializeField] private TextAsset mapJson;
        [SerializeField] private TerrainGridWorld targetWorld;

        public TerrainMapFile LoadedMap { get; private set; }

        public void Configure(TextAsset json, TerrainGridWorld world)
        {
            mapJson = json;
            targetWorld = world;
        }

        private void Awake()
        {
            if (targetWorld == null) targetWorld = GetComponent<TerrainGridWorld>();
            if (mapJson == null)
            {
                Debug.LogError("[Terrain Map Loader] Map JSON이 연결되지 않았습니다.");
                return;
            }

            LoadedMap = TerrainMapFile.FromJson(mapJson.text);
            if (LoadedMap == null)
            {
                Debug.LogError("[Terrain Map Loader] JSON 역직렬화에 실패했습니다.");
                return;
            }
            string validationError;
            if (!LoadedMap.IsValid(out validationError))
            {
                Debug.LogError($"[Terrain Map Loader] 맵 로드 실패: {validationError}");
                return;
            }

            targetWorld.LoadMap(LoadedMap);
            Debug.Log($"[Terrain Map Loader] '{LoadedMap.Name}' 로드 완료 — " +
                      $"{LoadedMap.Width}x{LoadedMap.Height}, cells={LoadedMap.Tiles.Count}");
        }
    }
}
