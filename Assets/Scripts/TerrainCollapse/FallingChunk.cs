using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapsePrototype
{
    public interface IFallingTerrainEscapeTarget
    {
        bool TryEscapeFromFallingTerrain(Bounds terrainBounds, float terrainCellSize);
    }

    /// <summary>커스텀 Grid 셀 묶음의 물리 낙하와 안정화 상태를 관리한다.</summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(CompositeCollider2D))]
    public sealed class FallingChunk : MonoBehaviour
    {
        [SerializeField] private float linearVelocityThreshold = 0.1f;
        [SerializeField] private float angularVelocityThreshold = 1f;
        [SerializeField] private float settleDuration = 0.3f;
        [SerializeField] private float groundContactGraceDuration = 0.15f;
        [SerializeField] private float maximumSettleWait = 2f;
        [SerializeField] private float forcedSettleVelocityThreshold = 0.5f;
        [SerializeField, Range(0f, 1f)] private float minimumSupportingNormalY = 0.8f;
        [SerializeField] private float minimumSupportContactWidth = 0.02f;

        private readonly List<Vector2Int> localCells = new();
        private readonly List<TerrainTileType> cellTypes = new();
        private readonly HashSet<Collider2D> groundContacts = new();
        private Rigidbody2D body;
        private CompositeCollider2D compositeCollider;
        private PhysicsMaterial2D frictionlessMaterial;
        private float cellSize;
        private float stableTime;
        private float timeSinceFirstGroundContact;
        private float lastGroundContactTime = float.NegativeInfinity;
        private bool hasTouchedGround;
        private bool settled;

        public bool IsSettled => settled;
        public IReadOnlyList<Vector2Int> LocalCells => localCells;
        public IReadOnlyList<TerrainTileType> CellTypes => cellTypes;
        public Collider2D ShapeCollider => compositeCollider;
        public Bounds WorldBounds => compositeCollider.bounds;
        public float CellSize => cellSize;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            compositeCollider = GetComponent<CompositeCollider2D>();
            frictionlessMaterial = new PhysicsMaterial2D("Falling Chunk Frictionless")
            {
                friction = 0f,
                bounciness = 0f
            };
            compositeCollider.sharedMaterial = frictionlessMaterial;
        }

        public void Configure(
            IReadOnlyList<Vector2Int> cells,
            IReadOnlyList<TerrainTileType> types,
            float size)
        {
            localCells.Clear();
            localCells.AddRange(cells);
            cellTypes.Clear();
            cellTypes.AddRange(types);
            cellSize = size;
        }

        public Vector3 GetLocalCellCenterWorld(Vector2Int localCell)
            => transform.TransformPoint(new Vector3((localCell.x + 0.5f) * cellSize,
                (localCell.y + 0.5f) * cellSize, 0f));

        private void OnDestroy()
        {
            Destroy(frictionlessMaterial);
        }

        private void FixedUpdate()
        {
            if (settled) return;

            bool recentGround = groundContacts.Count > 0 ||
                                Time.time - lastGroundContactTime <= groundContactGraceDuration;
            bool slow = body.IsSleeping() ||
                        (body.linearVelocity.sqrMagnitude <= linearVelocityThreshold * linearVelocityThreshold &&
                         Mathf.Abs(body.angularVelocity) <= angularVelocityThreshold);
            
            stableTime = recentGround && slow
                ? stableTime + Time.fixedDeltaTime
                : Mathf.Max(0f, stableTime - Time.fixedDeltaTime);

            if (hasTouchedGround) timeSinceFirstGroundContact += Time.fixedDeltaTime;
            
            bool forceSettled = hasTouchedGround && recentGround &&
                                timeSinceFirstGroundContact >= maximumSettleWait &&
                                body.linearVelocity.sqrMagnitude <=
                                forcedSettleVelocityThreshold * forcedSettleVelocityThreshold;
            
            if (stableTime < settleDuration && forceSettled == false) return;

            settled = true;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.bodyType = RigidbodyType2D.Kinematic;
            Debug.Log($"[Terrain Collapse] Chunk Settled: {name}" +
                      (forceSettled ? " (contact timeout fallback)" : string.Empty));
        }

        private void OnCollisionEnter2D(Collision2D collision) => HandleCollision(collision);
        private void OnCollisionStay2D(Collision2D collision) => HandleCollision(collision);

        private void HandleCollision(Collision2D collision)
        {
            MonoBehaviour[] behaviours = collision.collider.GetComponents<MonoBehaviour>();
            
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is IFallingTerrainEscapeTarget target)
                {
                    target.TryEscapeFromFallingTerrain(WorldBounds, CellSize);
                    break;
                }
            }
            
            UpdateGroundContact(collision);
        }

        private void UpdateGroundContact(Collision2D collision)
        {
            Rigidbody2D otherBody = collision.rigidbody;
            bool staticSurface = otherBody == null || otherBody.bodyType == RigidbodyType2D.Static;
            int supportingContactCount = 0;
            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            
            for (int i = 0; staticSurface && i < collision.contactCount; i++)
            {
                ContactPoint2D contact = collision.GetContact(i);
                
                if (contact.normal.y < minimumSupportingNormalY) continue;
                
                supportingContactCount++;
                minX = Mathf.Min(minX, contact.point.x);
                maxX = Mathf.Max(maxX, contact.point.x);
            }

            bool supportingSurface = supportingContactCount >= 2 && maxX - minX >= minimumSupportContactWidth;
            
            if (staticSurface && supportingSurface)
            {
                groundContacts.Add(collision.collider);
                hasTouchedGround = true;
                lastGroundContactTime = Time.time;
            }
            else groundContacts.Remove(collision.collider);
        }

        private void OnCollisionExit2D(Collision2D collision)
        {
            if (groundContacts.Remove(collision.collider)) lastGroundContactTime = Time.time;
        }
    }
}
