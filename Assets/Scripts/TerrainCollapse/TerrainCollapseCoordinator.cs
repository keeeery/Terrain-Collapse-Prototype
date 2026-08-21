using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapsePrototype
{
    public sealed class TerrainCollapseCoordinator : MonoBehaviour
    {
        [SerializeField] private TerrainManager terrainManager;
        [SerializeField] private CollapseManager collapseManager;
        [SerializeField] private int maximumChainCount = 32;

        private bool isCheckingGroups;
        private int chainCount;

        public void Configure(TerrainManager terrain, CollapseManager collapse)
        {
            terrainManager = terrain;
            collapseManager = collapse;
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            UnSubscribe();
        }

        private void Subscribe()
        {
            terrainManager.TerrainChanged += HandleTerrainChanged;
            collapseManager.RebuildCompleted += HandleRebuildCompleted;
        }

        private void UnSubscribe()
        {
            terrainManager.TerrainChanged -= HandleTerrainChanged;
            collapseManager.RebuildCompleted -= HandleRebuildCompleted;
        }

        private void HandleTerrainChanged()
        {
            chainCount = 0;
            ReleaseFloatingGroups();
        }

        private void HandleRebuildCompleted()
        {
            chainCount++;
            if (chainCount > maximumChainCount)
            {
                Debug.LogError($"[Terrain Collapse] 연쇄 붕괴가 {maximumChainCount}회를 초과해 중단합니다.");
                return;
            }

            ReleaseFloatingGroups();
        }

        private void ReleaseFloatingGroups()
        {
            if (isCheckingGroups) return;

            isCheckingGroups = true;
            try
            {
                List<List<Vector2Int>> groups = terrainManager.FindFloatingGroups();
                foreach (List<Vector2Int> group in groups)
                    collapseManager.CreateChunk(group);
            }
            finally
            {
                isCheckingGroups = false;
            }
        }
    }
}
