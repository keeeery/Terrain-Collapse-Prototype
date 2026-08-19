using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace TerrainCollapsePrototype
{
    /// <summary>
    /// Tilemap → 물리 Chunk → Grid Sampling → Tilemap 전환을 총괄한다.
    /// 모든 Chunk가 안정화되기 전에는 Tilemap 복원을 시작하지 않는다.
    /// </summary>
    public sealed class CollapseManager : MonoBehaviour
    {
        [SerializeField] private TerrainManager terrainManager;
        [SerializeField] private TerrainSampler terrainSampler;
        [SerializeField] private float gravityScale = 2f;
        [SerializeField] private float chunkMass = 100f;
        private readonly List<FallingChunk> activeChunks = new();
        private bool rebuilding;

        public bool IsRebuilding => rebuilding;

        /// <summary>Scene Builder에서 Manager 간 참조를 연결한다.</summary>
        public void Configure(TerrainManager terrain, TerrainSampler sampler)
        {
            terrainManager = terrain;
            terrainSampler = sampler;
        }

        /// <summary>Floating Group을 원본 Tilemap에서 제거하고 동일한 모양의 동적 Tilemap Chunk로 만든다.</summary>
        public void CreateChunk(IReadOnlyList<Vector3Int> cells)
        {
            Tilemap source = terrainManager.GroundTilemap;
            // 첫 셀을 로컬 원점으로 사용하면 큰 월드 좌표에서도 Chunk 내부 셀 좌표가 작게 유지된다.
            Vector3Int anchor = cells[0];
            var root = new GameObject($"FallingChunk_{activeChunks.Count + 1}", typeof(Grid));
            root.transform.position = source.CellToWorld(anchor);
            Grid grid = root.GetComponent<Grid>();
            grid.cellSize = source.layoutGrid.cellSize;

            var tileObject = new GameObject("Tiles", typeof(Tilemap), typeof(TilemapRenderer),
                typeof(Rigidbody2D), typeof(TilemapCollider2D), typeof(CompositeCollider2D), typeof(FallingChunk));
            tileObject.transform.SetParent(root.transform, false);
            Tilemap chunkMap = tileObject.GetComponent<Tilemap>();
            TilemapRenderer renderer = tileObject.GetComponent<TilemapRenderer>();
            renderer.sortingOrder = source.GetComponent<TilemapRenderer>().sortingOrder + 1;
            Rigidbody2D body = tileObject.GetComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.gravityScale = gravityScale;
            body.mass = chunkMass;
            // 포팅 전까지 Player 충돌로 복원 위치가 바뀌지 않도록 수직 낙하만 허용한다.
            // 큰 질량은 아래에서 Player가 점프로 밀어 올리는 영향도 최소화한다.
            body.constraints = RigidbodyConstraints2D.FreezePositionX | RigidbodyConstraints2D.FreezeRotation;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            TilemapCollider2D collider = tileObject.GetComponent<TilemapCollider2D>();
            collider.compositeOperation = Collider2D.CompositeOperation.Merge;
            // 타일 덩어리의 Bounds 직사각형이 아니라 실제 외곽선을 합친 다각형 Collider를 사용한다.
            // 오목한 형상은 Physics2D가 여러 convex polygon으로 내부 분할해 시뮬레이션한다.
            CompositeCollider2D composite = tileObject.GetComponent<CompositeCollider2D>();
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons;

            // 이 순간부터 해당 셀의 실제 데이터는 원본 Tilemap이 아니라 Chunk가 소유한다.
            foreach (Vector3Int worldCell in cells)
            {
                chunkMap.SetTile(worldCell - anchor, source.GetTile(worldCell));
                source.SetTile(worldCell, null);
            }
            source.RefreshAllTiles();
            chunkMap.RefreshAllTiles();
            Physics2D.SyncTransforms();
            FallingChunk chunk = tileObject.GetComponent<FallingChunk>();
            // 원본 Tilemap과 공유하던 꼭짓점 접촉을 잠시 해제해야 새 Dynamic Body가 실제로 낙하를 시작한다.
            chunk.BeginInitialGroundSeparation(source.GetComponent<CompositeCollider2D>());
            activeChunks.Add(chunk);
            Debug.Log($"[Terrain Collapse] Chunk Created: {cells.Count} tiles");
        }

        private void Update()
        {
            // 여러 Chunk가 동시에 생성되어도 전부 안정화된 뒤 한 번만 Sampling한다.
            if (!rebuilding && activeChunks.Count > 0 && activeChunks.TrueForAll(chunk => chunk != null && chunk.IsSettled))
                StartCoroutine(RebuildCycle());
        }

        private IEnumerator RebuildCycle()
        {
            rebuilding = true;
            Debug.Log("[Terrain Collapse] Sampling Started");
            terrainSampler.RebuildFromChunks(activeChunks);
            Debug.Log("[Terrain Collapse] Sampling Finished");
            foreach (FallingChunk chunk in activeChunks)
                if (chunk != null) Destroy(chunk.transform.parent.gameObject);
            activeChunks.Clear();
            // Destroy와 Collider 변경이 Physics World에 반영된 뒤 연쇄 붕괴를 검사한다.
            yield return new WaitForFixedUpdate();
            Physics2D.SyncTransforms();
            Debug.Log("[Terrain Collapse] Tilemap Rebuilt");
            rebuilding = false;
            // 복원 결과가 다른 지형을 공중에 고립시켰을 수 있으므로 다시 연결 그룹을 계산한다.
            terrainManager.FindAndReleaseFloatingGroups();
        }
    }
}
