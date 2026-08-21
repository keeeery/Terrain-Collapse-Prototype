using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapsePrototype
{
    /// <summary>Custom Grid → 물리 Chunk → Grid Sampling → Custom Grid 전환을 총괄한다.</summary>
    public sealed class CollapseManager : MonoBehaviour
    {
        [SerializeField] private TerrainManager terrainManager;
        [SerializeField] private TerrainSampler terrainSampler;
        [SerializeField] private float gravityScale = 2f;
        [SerializeField] private float chunkMass = 100f;
        [SerializeField] private float initialSeparationDistance = 0.05f;
        private readonly List<FallingChunk> activeChunks = new();
        private bool rebuilding;

        public bool IsRebuilding => rebuilding;

        public void Configure(TerrainManager terrain, TerrainSampler sampler)
        {
            terrainManager = terrain;
            terrainSampler = sampler;
        }

        public void CreateChunk(IReadOnlyList<Vector2Int> cells)
        {
            TerrainGridWorld source = terrainManager.TerrainWorld;
            Vector2Int anchor = cells[0];
            var root = new GameObject($"FallingChunk_{activeChunks.Count + 1}",
                typeof(Rigidbody2D), typeof(CompositeCollider2D), typeof(FallingChunk));
            // 최초 꼭짓점 접촉이 Box2D 제약으로 남지 않도록 물리 생성 위치를 소폭 아래로 분리한다.
            root.transform.position = source.CellToWorldCorner(anchor) + Vector3.down * initialSeparationDistance;

            Rigidbody2D body = root.GetComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.gravityScale = gravityScale;
            body.mass = chunkMass;
            body.constraints = RigidbodyConstraints2D.FreezePositionX | RigidbodyConstraints2D.FreezeRotation;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            CompositeCollider2D composite = root.GetComponent<CompositeCollider2D>();
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons;

            var localCells = new List<Vector2Int>(cells.Count);
            var cellTypes = new List<TerrainTileType>(cells.Count);
            foreach (Vector2Int worldCell in cells)
            {
                Vector2Int localCell = worldCell - anchor;
                localCells.Add(localCell);
                cellTypes.Add(source.Data.GetCellOrNull(worldCell).Type);
                CreateChunkCell(root.transform, localCell, source);
                source.RemoveCell(worldCell);
            }

            Physics2D.SyncTransforms();
            FallingChunk chunk = root.GetComponent<FallingChunk>();
            chunk.Configure(localCells, cellTypes, source.CellSize);
            activeChunks.Add(chunk);
            Debug.Log($"[Terrain Collapse] Chunk Created: {cells.Count} cells");
        }

        private static void CreateChunkCell(Transform parent, Vector2Int localCell, TerrainGridWorld source)
        {
            var cell = new GameObject($"Cell_{localCell.x}_{localCell.y}",
                typeof(SpriteRenderer), typeof(BoxCollider2D));
            cell.transform.SetParent(parent, false);
            cell.transform.localPosition = new Vector3((localCell.x + 0.5f) * source.CellSize,
                (localCell.y + 0.5f) * source.CellSize, 0f);
            SpriteRenderer renderer = cell.GetComponent<SpriteRenderer>();
            renderer.sprite = source.TerrainSprite;
            renderer.color = source.TerrainColor;
            renderer.sortingOrder = 1;
            BoxCollider2D collider = cell.GetComponent<BoxCollider2D>();
            collider.size = Vector2.one * source.CellSize;
            collider.compositeOperation = Collider2D.CompositeOperation.Merge;
        }

        private void Update()
        {
            if (!rebuilding && activeChunks.Count > 0 &&
                activeChunks.TrueForAll(chunk => chunk != null && chunk.IsSettled))
                StartCoroutine(RebuildCycle());
        }

        private IEnumerator RebuildCycle()
        {
            rebuilding = true;
            Debug.Log("[Terrain Collapse] Sampling Started");
            terrainSampler.RebuildFromChunks(activeChunks);
            Debug.Log("[Terrain Collapse] Sampling Finished");
            foreach (FallingChunk chunk in activeChunks)
                if (chunk != null) Destroy(chunk.gameObject);
            activeChunks.Clear();
            yield return new WaitForFixedUpdate();
            Physics2D.SyncTransforms();
            Debug.Log("[Terrain Collapse] Custom Grid Rebuilt");
            rebuilding = false;
            terrainManager.FindAndReleaseFloatingGroups();
        }
    }
}
