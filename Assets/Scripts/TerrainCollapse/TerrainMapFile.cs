using System;
using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapsePrototype
{
    /// <summary>JSON에 저장되는 데이터 기반 지형 맵 형식.</summary>
    [Serializable]
    public sealed class TerrainMapFile
    {
        public int Version = 1;
        public string Name = "TerrainMap";
        public int Width;
        public int Height;
        public float CellSize = 1f;
        public Vector2 Origin;
        public int FoundationCellY;
        public List<TerrainTileType> Tiles = new();

        public TerrainTileType GetTile(Vector2Int coord)
            => Tiles[coord.y * Width + coord.x];

        public void SetTile(Vector2Int coord, TerrainTileType type)
            => Tiles[coord.y * Width + coord.x] = type;

        public bool IsValid(out string error)
        {
            if (Version <= 0) { error = "Version은 1 이상이어야 합니다."; return false; }
            if (Width <= 0 || Height <= 0) { error = $"맵 크기가 잘못되었습니다: {Width}x{Height}"; return false; }
            if (CellSize <= 0f) { error = $"CellSize가 잘못되었습니다: {CellSize}"; return false; }
            if (Tiles == null || Tiles.Count != Width * Height)
            {
                error = $"셀 수가 맞지 않습니다: 기대 {Width * Height}, 실제 {Tiles?.Count ?? 0}";
                return false;
            }
            
            error = string.Empty;
            
            return true;
        }

        public string ToJson(bool prettyPrint = true) => JsonUtility.ToJson(this, prettyPrint);
        public static TerrainMapFile FromJson(string json) => JsonUtility.FromJson<TerrainMapFile>(json);
    }

    /// <summary>에디터 씬 생성과 런타임 폴백이 공유하는 데이터 맵 생성 규칙.</summary>
    public static class TerrainMapFactory
    {
        public const int PerformanceMapWidth = 100;
        public const int PerformanceMapDepth = 100;
        public const int CollapseStructureHeight = 5;

        public static TerrainMapFile CreatePerformanceMap()
        {
            int height = PerformanceMapDepth + CollapseStructureHeight;
            TerrainMapFile map = new  TerrainMapFile
            {
                Name = "TerrainCollapsePerformanceMap",
                Width = PerformanceMapWidth,
                Height = height,
                CellSize = 1f,
                Origin = new Vector2(-PerformanceMapWidth / 2f, -(PerformanceMapDepth - 1)),
                FoundationCellY = 0,
                Tiles = new List<TerrainTileType>(PerformanceMapWidth * height)
            };
            
            for (int i = 0; i < map.Width * map.Height; i++)
                map.Tiles.Add(TerrainTileType.Empty);

            for (int y = 0; y < PerformanceMapDepth; y++)
                for (int x = 0; x < PerformanceMapWidth; x++)
                    map.SetTile(new Vector2Int(x, y), TerrainTileType.Ground);

            int centerOffsetX = PerformanceMapWidth / 2;
            int surfaceY = PerformanceMapDepth - 1;
            
            for (int worldY = 4; worldY <= 5; worldY++)
                for (int worldX = -4; worldX <= 2; worldX++)
                    map.SetTile(new Vector2Int(centerOffsetX + worldX, surfaceY + worldY), TerrainTileType.Ground);
            
            for (int worldY = 1; worldY <= 3; worldY++)
                for (int worldX = -1; worldX <= 0; worldX++)
                    map.SetTile(new Vector2Int(centerOffsetX + worldX, surfaceY + worldY), TerrainTileType.Ground);

            return map;
        }
    }
}
