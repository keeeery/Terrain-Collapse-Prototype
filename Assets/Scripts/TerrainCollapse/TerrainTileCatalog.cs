using System;
using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapsePrototype
{
    [Serializable]
    public sealed class TerrainTileDefinition
    {
        public TerrainTileType Type;
        public Sprite Sprite;
        public Color Color = Color.white;
        public bool CreatesCollider = true;
    }

    [CreateAssetMenu(menuName = "Terrain Collapse/Tile Catalog", fileName = "TerrainTileCatalog")]
    public sealed class TerrainTileCatalog : ScriptableObject
    {
        [SerializeField] private List<TerrainTileDefinition> definitions = new();
        private readonly Dictionary<TerrainTileType, TerrainTileDefinition> lookup = new();

        public void Configure(IEnumerable<TerrainTileDefinition> tileDefinitions)
        {
            definitions.Clear();
            definitions.AddRange(tileDefinitions);
            lookup.Clear();
        }

        public TerrainTileDefinition Get(TerrainTileType type)
        {
            EnsureLookup();
            return lookup.TryGetValue(type, out TerrainTileDefinition definition) ? definition : null;
        }

        private void EnsureLookup()
        {
            if (lookup.Count == definitions.Count) return;
            lookup.Clear();
            foreach (TerrainTileDefinition definition in definitions)
                lookup[definition.Type] = definition;
        }

        private void OnValidate()
        {
            lookup.Clear();
            Texture2D atlasTexture = null;
            foreach (TerrainTileDefinition definition in definitions)
            {
                if (definition.Sprite == null) continue;
                atlasTexture ??= definition.Sprite.texture;
                if (definition.Sprite.texture != atlasTexture)
                    Debug.LogWarning("[Terrain Tile Catalog] Chunk Mesh용 Sprite는 같은 Texture Atlas를 사용해야 합니다.", this);
            }
        }
    }
}
