using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapsePrototype
{
    /// <summary> 지형 원본 데이터, 빈 공간을 저장하지 않는 Sparse Grid이다.</summary>
    public sealed class TerrainGridData
    {
        private readonly Dictionary<Vector2Int, TerrainCell> cells = new();

        public float CellSize { get; }
        public Vector2 Origin { get; }
        public int Count => cells.Count;

        public TerrainGridData(float cellSize, Vector2 origin)
        {
            CellSize = cellSize;
            Origin = origin;
        }

        public bool IsOccupied(Vector2Int coord) => cells.ContainsKey(coord);

        public TerrainCell GetCellOrNull(Vector2Int coord)
            => cells.TryGetValue(coord, out TerrainCell cell) ? cell : null;

        public bool SetCell(Vector2Int coord, TerrainTileType type)
        {
            if (type == TerrainTileType.Empty) return Remove(coord);
            
            if (cells.TryGetValue(coord, out TerrainCell existing))
            {
                if (existing.Type == type) return false;
                
                existing.ChangeType(type);
                return true;
            }
            
            cells.Add(coord, new TerrainCell(coord, type));
            
            return true;
        }

        public bool SetOccupied(Vector2Int coord) => SetCell(coord, TerrainTileType.Ground);

        public bool Remove(Vector2Int coord) => cells.Remove(coord);

        public IEnumerable<Vector2Int> OccupiedCoords => cells.Keys;

        public Vector2Int WorldToCell(Vector3 worldPosition)
        {
            int x = Mathf.FloorToInt((worldPosition.x - Origin.x) / CellSize);
            int y = Mathf.FloorToInt((worldPosition.y - Origin.y) / CellSize);
            
            return new Vector2Int(x, y);
        }

        public Vector3 CellToWorldCenter(Vector2Int coord)
            => new(Origin.x + (coord.x + 0.5f) * CellSize,
                Origin.y + (coord.y + 0.5f) * CellSize, 0f);

        public Vector3 CellToWorldCorner(Vector2Int coord)
            => new(Origin.x + coord.x * CellSize, Origin.y + coord.y * CellSize, 0f);
    }
}
