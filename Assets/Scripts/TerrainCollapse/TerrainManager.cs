using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TerrainCollapsePrototype
{
    /// <summary>
    /// 커스텀 TerrainGridData를 지형의 Source of Truth로 관리한다.
    /// 타일 제거 후 4방향 연결 그룹을 찾고 기반과 끊긴 그룹을 물리 Chunk로 넘긴다.
    /// </summary>
    public sealed class TerrainManager : MonoBehaviour
    {
        [SerializeField] private TerrainGridWorld terrainWorld;
        [SerializeField] private Camera inputCamera;
        [SerializeField] private bool logEvents = true;

        private InputAction removeTileAction;

        private static readonly Vector2Int[] Directions =
        {
            Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
        };

        public TerrainGridWorld TerrainWorld => terrainWorld;
        public event Action TerrainChanged;

        public void Configure(TerrainGridWorld world, Camera camera)
        {
            terrainWorld = world;
            inputCamera = camera;
        }

        private void OnEnable()
        {
            removeTileAction ??= new InputAction("Remove Terrain Cell", InputActionType.Button, "<Pointer>/press");
            removeTileAction.performed += OnRemoveTilePerformed;
            removeTileAction.Enable();
        }

        private void OnDisable()
        {
            removeTileAction.performed -= OnRemoveTilePerformed;
            removeTileAction.Disable();
        }

        private void OnDestroy() => removeTileAction?.Dispose();

        private void OnRemoveTilePerformed(InputAction.CallbackContext _)
        {
            if (Pointer.current == null) return;

            Camera camera = inputCamera != null ? inputCamera : Camera.main;
            Vector2 pointer = Pointer.current.position.ReadValue();
            float planeDistance = Mathf.Abs(terrainWorld.transform.position.z - camera.transform.position.z);
            Vector3 world = camera.ScreenToWorldPoint(new Vector3(pointer.x, pointer.y, planeDistance));
            Vector2Int cell = terrainWorld.WorldToCell(world);

            if (RemoveCell(cell) == false && logEvents)
                Debug.Log($"[Terrain Collapse] Cell click missed: screen={pointer}, world={world}, cell={cell}");
        }

        public bool RemoveCell(Vector2Int cell)
        {
            TerrainCell terrainCell = terrainWorld.Data.GetCellOrNull(cell);
            if (terrainCell != null && terrainCell.Type == TerrainTileType.Bedrock) return false;
            if (terrainWorld.RemoveCell(cell) == false) return false;
            if (logEvents) Debug.Log($"[Terrain Collapse] Cell Destroy: {cell}");

            TerrainChanged?.Invoke();
            return true;
        }

        /// <summary>점유 셀을 BFS로 순회하고 기반암과 연결되지 않은 그룹을 분리한다.</summary>
        public List<List<Vector2Int>> FindFloatingGroups()
        {
            List<Vector2Int> occupiedSnapshot = new(terrainWorld.GetOccupiedCells());
            HashSet<Vector2Int> visited = new();
            List<List<Vector2Int>> floatingGroups = new();

            foreach (Vector2Int start in occupiedSnapshot)
            {
                if (terrainWorld.IsOccupied(start) == false || visited.Add(start) == false) continue;

                List<Vector2Int> group = new();
                Queue<Vector2Int> queue = new();
                bool supported = false;

                queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    Vector2Int cell = queue.Dequeue();
                    group.Add(cell);
                    supported |= terrainWorld.Data.GetCellOrNull(cell).Type == TerrainTileType.Bedrock;

                    foreach (Vector2Int direction in Directions)
                    {
                        Vector2Int next = cell + direction;
                        if (terrainWorld.IsOccupied(next) && visited.Add(next)) queue.Enqueue(next);
                    }
                }

                if (supported == false) floatingGroups.Add(group);
            }

            return floatingGroups;
        }
    }
}
