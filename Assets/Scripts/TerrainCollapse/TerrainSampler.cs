using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace TerrainCollapsePrototype
{
    /// <summary>
    /// Rigidbody 위치를 반올림하지 않고 Grid Cell Center를 Chunk Collider에 샘플링한다.
    /// 검사 범위는 각 Chunk Bounds와 겹치는 셀로 제한한다.
    /// </summary>
    public sealed class TerrainSampler : MonoBehaviour
    {
        [SerializeField] private Tilemap targetTilemap;
        [SerializeField] private TileBase terrainTile;
        [SerializeField] private bool drawSamplingGizmos = true;
        private readonly List<Vector3> lastCenters = new();
        private readonly List<Bounds> lastBounds = new();

        /// <summary>Scene Builder가 복원 대상 Tilemap과 기본 Tile을 연결한다.</summary>
        public void Configure(Tilemap tilemap, TileBase tile)
        {
            targetTilemap = tilemap;
            terrainTile = tile;
        }

        /// <summary>안정화된 모든 Chunk를 셀 중심 샘플링하여 Tilemap 데이터로 복원한다.</summary>
        public void RebuildFromChunks(IReadOnlyList<FallingChunk> chunks)
        {
            lastCenters.Clear();
            lastBounds.Clear();
            foreach (FallingChunk chunk in chunks)
            {
                Bounds bounds = chunk.WorldBounds;
                lastBounds.Add(bounds);
                Vector3Int min = targetTilemap.WorldToCell(bounds.min);
                Vector3Int max = targetTilemap.WorldToCell(bounds.max);
                // Bounds 안의 각 셀 중심이 실제 Chunk 형상 내부에 있을 때만 타일을 생성한다.
                for (int y = min.y; y <= max.y; y++)
                for (int x = min.x; x <= max.x; x++)
                {
                    var cell = new Vector3Int(x, y, 0);
                    Vector3 center = targetTilemap.GetCellCenterWorld(cell);
                    lastCenters.Add(center);
                    // 기존 고정 지형은 덮어쓰지 않아 Chunk와 지형이 겹쳐도 데이터가 손실되지 않는다.
                    if (!targetTilemap.HasTile(cell) && chunk.ShapeCollider.OverlapPoint(center))
                        targetTilemap.SetTile(cell, terrainTile);
                }
            }
            targetTilemap.RefreshAllTiles();
        }

        private void OnDrawGizmos()
        {
            if (!drawSamplingGizmos || targetTilemap == null) return;
            // 노랑: 검사 Bounds, 청록: 실제로 검사한 Grid Cell.
            Gizmos.color = Color.yellow;
            foreach (Bounds bounds in lastBounds) Gizmos.DrawWireCube(bounds.center, bounds.size);
            Gizmos.color = new Color(0f, 1f, 1f, 0.8f);
            Vector3 size = Vector3.Scale(targetTilemap.layoutGrid.cellSize, new Vector3(.85f, .85f, .1f));
            foreach (Vector3 center in lastCenters) Gizmos.DrawWireCube(center, size);
        }
    }
}
