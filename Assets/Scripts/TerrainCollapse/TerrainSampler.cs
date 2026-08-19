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
                RebuildConnectedChunk(chunk, bounds);
            }
            targetTilemap.RefreshAllTiles();
        }

        /// <summary>
        /// 셀을 각각 복원하면 경계에 걸친 Chunk가 조각난 형태로 양자화될 수 있다.
        /// 원래 로컬 타일 형태를 유지한 채 Cell Center 적중률이 가장 높은 정수 배치를 선택한다.
        /// </summary>
        private void RebuildConnectedChunk(FallingChunk chunk, Bounds bounds)
        {
            List<Vector3Int> localCells = GetOccupiedCells(chunk.ChunkTilemap);
            if (localCells.Count == 0) return;

            var searchPadding = new Vector3Int(1, 1, 0);
            Vector3Int worldMin = targetTilemap.WorldToCell(bounds.min) - searchPadding;
            Vector3Int worldMax = targetTilemap.WorldToCell(bounds.max) + searchPadding;
            Vector3Int localMin = localCells[0];
            Vector3Int localMax = localCells[0];
            foreach (Vector3Int cell in localCells)
            {
                localMin = Vector3Int.Min(localMin, cell);
                localMax = Vector3Int.Max(localMax, cell);
            }

            Vector3Int bestOffset = Vector3Int.zero;
            int bestHitCount = -1;
            int bestConnectionCount = -1;
            int bestCollisionCount = int.MaxValue;
            int bestPlacementClass = -1;
            float bestAlignmentError = float.PositiveInfinity;

            // 가능한 Grid 평행 이동을 모두 Cell Center로 검사한다. Rigidbody 위치 반올림은 사용하지 않는다.
            for (int offsetY = worldMin.y - localMin.y; offsetY <= worldMax.y - localMax.y; offsetY++)
            for (int offsetX = worldMin.x - localMin.x; offsetX <= worldMax.x - localMax.x; offsetX++)
            {
                var offset = new Vector3Int(offsetX, offsetY, 0);
                int hitCount = 0;
                int connectionCount = 0;
                int collisionCount = 0;
                float alignmentError = 0f;

                foreach (Vector3Int localCell in localCells)
                {
                    Vector3Int targetCell = localCell + offset;
                    Vector3 center = targetTilemap.GetCellCenterWorld(targetCell);
                    if (chunk.ShapeCollider.OverlapPoint(center)) hitCount++;
                    if (targetTilemap.HasTile(targetCell)) collisionCount++;
                    connectionCount += CountExistingNeighbours(targetCell);

                    // 현재 다각형 Chunk의 실제 타일 중심과 후보 Grid 중심의 거리 오차이다.
                    // 이 값이 작을수록 물리 낙하 위치를 좌우로 왜곡하지 않은 배치다.
                    Vector3 physicalCenter = chunk.ChunkTilemap.GetCellCenterWorld(localCell);
                    alignmentError += (physicalCenter - center).sqrMagnitude;
                }
                alignmentError /= localCells.Count;

                // 1: 기존 타일과 겹치지 않음, 0: 기존 타일과 겹침.
                // '기존 지형에 연결됨'을 상위 등급으로 두면 꼭짓점 접촉 상태를 옆 칸으로 강제 이동시킨다.
                // 연결 여부는 마지막 동률 판정에서만 사용하고 실제 물리 위치를 우선한다.
                int placementClass = collisionCount == 0 ? 1 : 0;
                if (!IsBetterPlacement(
                        placementClass, hitCount, alignmentError, connectionCount, collisionCount,
                        bestPlacementClass, bestHitCount, bestAlignmentError, bestConnectionCount,
                        bestCollisionCount))
                    continue;
                bestOffset = offset;
                bestHitCount = hitCount;
                bestConnectionCount = connectionCount;
                bestCollisionCount = collisionCount;
                bestPlacementClass = placementClass;
                bestAlignmentError = alignmentError;
            }

            // 선택한 배치의 검사점을 Gizmo에 기록하고, 원래 연결 형태 전체를 복원한다.
            foreach (Vector3Int localCell in localCells)
            {
                Vector3Int targetCell = localCell + bestOffset;
                lastCenters.Add(targetTilemap.GetCellCenterWorld(targetCell));
                if (!targetTilemap.HasTile(targetCell)) targetTilemap.SetTile(targetCell, terrainTile);
            }

            Debug.Log($"[Terrain Collapse] Chunk grid placement: hits={bestHitCount}/{localCells.Count}, " +
                      $"connections={bestConnectionCount}, overlaps={bestCollisionCount}, " +
                      $"class={bestPlacementClass}, alignmentError={bestAlignmentError:F4}, offset={bestOffset}");
        }

        private static bool IsBetterPlacement(
            int placementClass,
            int hitCount,
            float alignmentError,
            int connectionCount,
            int collisionCount,
            int bestPlacementClass,
            int bestHitCount,
            float bestAlignmentError,
            int bestConnectionCount,
            int bestCollisionCount)
        {
            if (placementClass != bestPlacementClass) return placementClass > bestPlacementClass;
            if (hitCount != bestHitCount) return hitCount > bestHitCount;
            if (!Mathf.Approximately(alignmentError, bestAlignmentError))
                return alignmentError < bestAlignmentError;
            if (connectionCount != bestConnectionCount) return connectionCount > bestConnectionCount;
            return collisionCount < bestCollisionCount;
        }

        private static List<Vector3Int> GetOccupiedCells(Tilemap tilemap)
        {
            var cells = new List<Vector3Int>();
            foreach (Vector3Int cell in tilemap.cellBounds.allPositionsWithin)
                if (tilemap.HasTile(cell)) cells.Add(cell);
            return cells;
        }

        private int CountExistingNeighbours(Vector3Int cell)
        {
            int count = 0;
            if (targetTilemap.HasTile(cell + Vector3Int.up)) count++;
            if (targetTilemap.HasTile(cell + Vector3Int.down)) count++;
            if (targetTilemap.HasTile(cell + Vector3Int.left)) count++;
            if (targetTilemap.HasTile(cell + Vector3Int.right)) count++;
            return count;
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
