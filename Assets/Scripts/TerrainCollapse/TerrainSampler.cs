using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapsePrototype
{
    /// <summary>안정화된 물리 Chunk를 Cell Center Sampling하여 커스텀 Grid 데이터로 복원한다.</summary>
    public sealed class TerrainSampler : MonoBehaviour
    {
        [SerializeField] private TerrainGridWorld targetWorld;
        [SerializeField] private bool drawSamplingGizmos = true;
        private readonly List<Vector3> lastCenters = new();
        private readonly List<Bounds> lastBounds = new();

        public void Configure(TerrainGridWorld world) => targetWorld = world;

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
        }

        private void RebuildConnectedChunk(FallingChunk chunk, Bounds bounds)
        {
            IReadOnlyList<Vector2Int> localCells = chunk.LocalCells;
            
            if (localCells.Count == 0) return;

            Vector2Int padding = Vector2Int.one;
            Vector2Int worldMin = targetWorld.WorldToCell(bounds.min) - padding;
            Vector2Int worldMax = targetWorld.WorldToCell(bounds.max) + padding;
            Vector2Int localMin = localCells[0];
            Vector2Int localMax = localCells[0];
            
            foreach (Vector2Int cell in localCells)
            {
                localMin = Vector2Int.Min(localMin, cell);
                localMax = Vector2Int.Max(localMax, cell);
            }

            Vector2Int bestOffset = Vector2Int.zero;
            int bestHitCount = -1;
            int bestConnectionCount = -1;
            int bestCollisionCount = int.MaxValue;
            int bestPlacementClass = -1;
            float bestAlignmentError = float.PositiveInfinity;

            for (int offsetY = worldMin.y - localMin.y; offsetY <= worldMax.y - localMax.y; offsetY++)
            {
                for (int offsetX = worldMin.x - localMin.x; offsetX <= worldMax.x - localMax.x; offsetX++)
                {
                    var offset = new Vector2Int(offsetX, offsetY);
                    int hitCount = 0;
                    int connectionCount = 0;
                    int collisionCount = 0;
                    float alignmentError = 0f;

                    foreach (Vector2Int localCell in localCells)
                    {
                        Vector2Int targetCell = localCell + offset;
                        Vector3 center = targetWorld.CellToWorldCenter(targetCell);
                        
                        if (chunk.ShapeCollider.OverlapPoint(center)) hitCount++;
                        if (targetWorld.IsOccupied(targetCell)) collisionCount++;
                        
                        connectionCount += CountExistingNeighbours(targetCell);
                        alignmentError += (chunk.GetLocalCellCenterWorld(localCell) - center).sqrMagnitude;
                    }
                    
                    alignmentError /= localCells.Count;

                    int placementClass = collisionCount == 0 ? 1 : 0;
                    
                    if (IsBetterPlacement(
                            placementClass, hitCount, alignmentError, connectionCount, collisionCount,
                            bestPlacementClass, bestHitCount, bestAlignmentError, bestConnectionCount,
                            bestCollisionCount) == false)
                        continue;

                    bestOffset = offset;
                    bestHitCount = hitCount;
                    bestConnectionCount = connectionCount;
                    bestCollisionCount = collisionCount;
                    bestPlacementClass = placementClass;
                    bestAlignmentError = alignmentError;
                }
            }

            for (int i = 0; i < localCells.Count; i++)
            {
                Vector2Int localCell = localCells[i];
                Vector2Int targetCell = localCell + bestOffset;
                lastCenters.Add(targetWorld.CellToWorldCenter(targetCell));
                if (targetWorld.IsOccupied(targetCell) == false) targetWorld.SetCell(targetCell, chunk.CellTypes[i]);
            }

            Debug.Log($"[Terrain Collapse] Chunk grid placement: hits={bestHitCount}/{localCells.Count}, " +
                      $"connections={bestConnectionCount}, overlaps={bestCollisionCount}, " +
                      $"class={bestPlacementClass}, alignmentError={bestAlignmentError:F4}, offset={bestOffset}");
        }

        private static bool IsBetterPlacement(
            int placementClass, int hitCount, float alignmentError, int connectionCount, int collisionCount,
            int bestPlacementClass, int bestHitCount, float bestAlignmentError, int bestConnectionCount,
            int bestCollisionCount)
        {
            if (placementClass != bestPlacementClass) return placementClass > bestPlacementClass;
            if (hitCount != bestHitCount) return hitCount > bestHitCount;
            if (Mathf.Approximately(alignmentError, bestAlignmentError) == false) return alignmentError < bestAlignmentError;
            if (connectionCount != bestConnectionCount) return connectionCount > bestConnectionCount;
            return collisionCount < bestCollisionCount;
        }

        private int CountExistingNeighbours(Vector2Int cell)
        {
            int count = 0;
            if (targetWorld.IsOccupied(cell + Vector2Int.up)) count++;
            if (targetWorld.IsOccupied(cell + Vector2Int.down)) count++;
            if (targetWorld.IsOccupied(cell + Vector2Int.left)) count++;
            if (targetWorld.IsOccupied(cell + Vector2Int.right)) count++;
            return count;
        }

        private void OnDrawGizmos()
        {
            if (drawSamplingGizmos == false || targetWorld == null) return;
            
            Gizmos.color = Color.yellow;
            
            foreach (Bounds bounds in lastBounds) Gizmos.DrawWireCube(bounds.center, bounds.size);
            
            Gizmos.color = new Color(0f, 1f, 1f, 0.8f);
            Vector3 size = new(targetWorld.CellSize * 0.85f, targetWorld.CellSize * 0.85f, 0.1f);
            
            foreach (Vector3 center in lastCenters) Gizmos.DrawWireCube(center, size);
        }
    }
}
