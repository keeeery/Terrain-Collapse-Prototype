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
        [SerializeField] private CollapseManager collapseManager;
        [SerializeField] private Camera inputCamera;
        [SerializeField] private int supportCellY;
        [SerializeField] private bool logEvents = true;
        private InputAction removeTileAction;

        private static readonly Vector2Int[] Directions =
        {
            Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
        };

        public TerrainGridWorld TerrainWorld => terrainWorld;

        public void Configure(
            TerrainGridWorld world,
            CollapseManager manager,
            Camera camera,
            int foundationCellY)
        {
            terrainWorld = world;
            collapseManager = manager;
            inputCamera = camera;
            supportCellY = foundationCellY;
        }

        private void OnEnable()
        {
            removeTileAction ??= new InputAction("Remove Terrain Cell", InputActionType.Button, "<Pointer>/press");
            removeTileAction.performed += OnRemoveTilePerformed;
            removeTileAction.Enable();
        }

        private void OnDisable()
        {
            if (removeTileAction == null) return;
            removeTileAction.performed -= OnRemoveTilePerformed;
            removeTileAction.Disable();
        }

        private void OnDestroy() => removeTileAction?.Dispose();

        private void OnRemoveTilePerformed(InputAction.CallbackContext _)
        {
            if (collapseManager == null || collapseManager.IsRebuilding || terrainWorld == null ||
                Pointer.current == null)
                return;

            Camera camera = inputCamera != null ? inputCamera : Camera.main;
            if (camera == null) return;
            Vector2 pointer = Pointer.current.position.ReadValue();
            float planeDistance = Mathf.Abs(terrainWorld.transform.position.z - camera.transform.position.z);
            Vector3 world = camera.ScreenToWorldPoint(new Vector3(pointer.x, pointer.y, planeDistance));
            Vector2Int cell = terrainWorld.WorldToCell(world);
            if (!RemoveCell(cell) && logEvents)
                Debug.Log($"[Terrain Collapse] Cell click missed: screen={pointer}, world={world}, cell={cell}");
        }

        public bool RemoveCell(Vector2Int cell)
        {
            if (terrainWorld == null || !terrainWorld.RemoveCell(cell)) return false;
            if (logEvents) Debug.Log($"[Terrain Collapse] Cell Destroy: {cell}");
            FindAndReleaseFloatingGroups();
            return true;
        }

        /// <summary>점유 셀을 BFS로 순회하고 최하단 기반 행에 닿지 않은 그룹을 분리한다.</summary>
        public int FindAndReleaseFloatingGroups()
        {
            var occupiedSnapshot = new List<Vector2Int>(terrainWorld.GetOccupiedCells());
            var visited = new HashSet<Vector2Int>();
            var floating = new List<List<Vector2Int>>();

            foreach (Vector2Int start in occupiedSnapshot)
            {
                if (!terrainWorld.IsOccupied(start) || !visited.Add(start)) continue;
                var group = new List<Vector2Int>();
                var queue = new Queue<Vector2Int>();
                bool supported = false;
                queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    Vector2Int cell = queue.Dequeue();
                    group.Add(cell);
                    supported |= cell.y <= supportCellY;
                    foreach (Vector2Int direction in Directions)
                    {
                        Vector2Int next = cell + direction;
                        if (terrainWorld.IsOccupied(next) && visited.Add(next)) queue.Enqueue(next);
                    }
                }

                if (!supported) floating.Add(group);
            }

            foreach (List<Vector2Int> group in floating)
            {
                if (logEvents) Debug.Log($"[Terrain Collapse] Floating Group Found: {group.Count} cells");
                collapseManager.CreateChunk(group);
            }
            return floating.Count;
        }
    }
}
