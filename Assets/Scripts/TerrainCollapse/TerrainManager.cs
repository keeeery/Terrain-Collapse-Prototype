using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

namespace TerrainCollapsePrototype
{
    /// <summary>
    /// 평상시 지형의 원본 데이터(Source of Truth)인 Tilemap을 관리한다.
    /// 타일 제거 후 4방향 연결 그룹을 찾고, 기반과 끊긴 그룹을 물리 Chunk로 넘긴다.
    /// </summary>
    public sealed class TerrainManager : MonoBehaviour
    {
        [SerializeField] private Tilemap groundTilemap;
        [SerializeField] private TileBase terrainTile;
        [SerializeField] private CollapseManager collapseManager;
        [SerializeField] private Camera inputCamera;
        [SerializeField] private int supportCellY = 0;
        [SerializeField] private bool logEvents = true;
        private InputAction removeTileAction;

        private static readonly Vector3Int[] Directions =
        {
            Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right
        };

        public Tilemap GroundTilemap => groundTilemap;
        public TileBase TerrainTile => terrainTile;

        /// <summary>Scene Builder가 생성한 참조와 절대 기반으로 사용할 최하단 행을 연결한다.</summary>
        public void Configure(
            Tilemap tilemap,
            TileBase tile,
            CollapseManager manager,
            Camera camera,
            int foundationCellY)
        {
            groundTilemap = tilemap;
            terrainTile = tile;
            collapseManager = manager;
            inputCamera = camera;
            supportCellY = foundationCellY;
        }

        private void OnEnable()
        {
            // 마우스뿐 아니라 펜/터치도 같은 Pointer 입력으로 처리한다.
            removeTileAction ??= new InputAction("Remove Terrain Tile", InputActionType.Button, "<Pointer>/press");
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
            // 물리 Chunk가 낙하 중이어도 원본 Tilemap의 남은 타일은 계속 편집할 수 있다.
            // Sampling이 Tilemap에 데이터를 다시 쓰는 짧은 구간만 동시 수정을 막는다.
            if (collapseManager == null || collapseManager.IsRebuilding || groundTilemap == null ||
                Pointer.current == null)
                return;

            Camera camera = inputCamera != null ? inputCamera : Camera.main;
            if (camera == null) return;
            Vector2 pointer = Pointer.current.position.ReadValue();
            // ScreenToWorldPoint의 z는 월드 z가 아니라 카메라에서 대상 평면까지의 거리이다.
            float planeDistance = Mathf.Abs(groundTilemap.transform.position.z - camera.transform.position.z);
            Vector3 world = camera.ScreenToWorldPoint(new Vector3(pointer.x, pointer.y, planeDistance));
            Vector3Int cell = groundTilemap.WorldToCell(world);
            if (!RemoveTile(cell) && logEvents)
                Debug.Log($"[Terrain Collapse] Tile click missed: screen={pointer}, world={world}, cell={cell}");
        }

        /// <summary>지정 셀의 타일을 제거하고 즉시 구조 안정성을 다시 검사한다.</summary>
        /// <returns>실제로 타일이 제거되었으면 true.</returns>
        public bool RemoveTile(Vector3Int cell)
        {
            if (groundTilemap == null || !groundTilemap.HasTile(cell)) return false;
            groundTilemap.SetTile(cell, null);
            groundTilemap.RefreshAllTiles();
            if (logEvents) Debug.Log($"[Terrain Collapse] Tile Destroy: {cell}");
            FindAndReleaseFloatingGroups();
            return true;
        }

        /// <summary>
        /// 모든 타일을 BFS로 순회해 4방향 연결 그룹을 만든다.
        /// 그룹 중 하나라도 지지 기준 행에 닿으면 고정 지형, 아니면 Floating Group이다.
        /// </summary>
        public int FindAndReleaseFloatingGroups()
        {
            groundTilemap.CompressBounds();
            BoundsInt bounds = groundTilemap.cellBounds;
            var visited = new HashSet<Vector3Int>();
            var floating = new List<List<Vector3Int>>();

            foreach (Vector3Int start in bounds.allPositionsWithin)
            {
                if (!groundTilemap.HasTile(start) || !visited.Add(start)) continue;
                var group = new List<Vector3Int>();
                var queue = new Queue<Vector3Int>();
                bool supported = false;
                queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    Vector3Int cell = queue.Dequeue();
                    group.Add(cell);
                    supported |= IsSupportedCell(cell);
                    foreach (Vector3Int direction in Directions)
                    {
                        Vector3Int next = cell + direction;
                        if (groundTilemap.HasTile(next) && visited.Add(next)) queue.Enqueue(next);
                    }
                }
                if (!supported) floating.Add(group);
            }

            foreach (List<Vector3Int> group in floating)
            {
                if (logEvents) Debug.Log($"[Terrain Collapse] Floating Group Found: {group.Count} tiles");
                collapseManager.CreateChunk(group);
            }
            return floating.Count;
        }

        // 실제 게임에서는 이 메서드만 앵커, 구조 강도, 지지 블록 판정 등으로 교체할 수 있다.
        private bool IsSupportedCell(Vector3Int cell) => cell.y <= supportCellY;
    }
}
