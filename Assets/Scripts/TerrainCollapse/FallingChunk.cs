using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace TerrainCollapsePrototype
{
    /// <summary>
    /// 물리 Chunk의 지면 접촉과 움직임을 추적해 붕괴 완료 시점을 판정한다.
    /// 충돌 이벤트 하나만으로 멈췄다고 보지 않고 저속 상태가 일정 시간 지속되어야 한다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(CompositeCollider2D), typeof(TilemapCollider2D))]
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
        [SerializeField] private float initialCollisionIgnoreDropDistance = 0.05f;
        [SerializeField] private float maximumInitialCollisionIgnoreTime = 0.25f;

        private Rigidbody2D body;
        private Tilemap chunkTilemap;
        private CompositeCollider2D compositeCollider;
        private PhysicsMaterial2D frictionlessMaterial;
        private Collider2D initiallyIgnoredGround;
        private float initialBodyY;
        private float initialIgnoreStartTime;
        private bool ignoringInitialGroundCollision;
        private float stableTime;
        private float timeSinceFirstGroundContact;
        private float lastGroundContactTime = float.NegativeInfinity;
        private readonly HashSet<Collider2D> groundContacts = new();
        private bool hasTouchedGround;
        private bool settled;

        public bool IsSettled => settled;
        public Tilemap ChunkTilemap => chunkTilemap;
        public Collider2D ShapeCollider => compositeCollider;
        public Bounds WorldBounds => compositeCollider.bounds;

        /// <summary>
        /// 생성 위치에서 기존 지형과 꼭짓점만 맞닿은 경우 Box2D 접촉 제약이 낙하를 막을 수 있다.
        /// 아주 짧은 하강 거리 동안 원본 지형과 충돌을 무시해 점 접촉에서 확실히 분리한다.
        /// </summary>
        public void BeginInitialGroundSeparation(Collider2D groundCollider)
        {
            if (groundCollider == null) return;
            initiallyIgnoredGround = groundCollider;
            initialBodyY = body.position.y;
            initialIgnoreStartTime = Time.time;
            ignoringInitialGroundCollision = true;
            Physics2D.IgnoreCollision(compositeCollider, initiallyIgnoredGround, true);
            body.WakeUp();
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            chunkTilemap = GetComponent<Tilemap>();
            compositeCollider = GetComponent<CompositeCollider2D>();

            // 마찰이 있으면 꼭짓점 접촉의 대각선 반력이 중력과 균형을 이루어 모서리에 걸릴 수 있다.
            // 회전은 이미 고정되어 있으므로 마찰 없이 두면 면 지지는 유지되고 꼭짓점에서는 미끄러진다.
            frictionlessMaterial = new PhysicsMaterial2D("Falling Chunk Frictionless")
            {
                friction = 0f,
                bounciness = 0f
            };
            compositeCollider.sharedMaterial = frictionlessMaterial;
        }

        private void OnDestroy()
        {
            if (ignoringInitialGroundCollision && initiallyIgnoredGround != null)
                Physics2D.IgnoreCollision(compositeCollider, initiallyIgnoredGround, false);
            if (frictionlessMaterial != null) Destroy(frictionlessMaterial);
        }

        private void FixedUpdate()
        {
            UpdateInitialGroundSeparation();
            if (settled) return;

            // 모서리 접촉은 FixedUpdate 한두 번 동안 끊길 수 있어 마지막 접촉에도 짧은 유예를 둔다.
            bool hasRecentGroundContact = groundContacts.Count > 0 ||
                                          Time.time - lastGroundContactTime <= groundContactGraceDuration;
            bool slow = body.IsSleeping() ||
                        (body.linearVelocity.sqrMagnitude <= linearVelocityThreshold * linearVelocityThreshold &&
                         Mathf.Abs(body.angularVelocity) <= angularVelocityThreshold);

            // 한 번의 미세 반동으로 누적 시간이 모두 사라지지 않도록 즉시 초기화 대신 감소시킨다.
            stableTime = hasRecentGroundContact && slow
                ? stableTime + Time.fixedDeltaTime
                : Mathf.Max(0f, stableTime - Time.fixedDeltaTime);

            if (hasTouchedGround) timeSinceFirstGroundContact += Time.fixedDeltaTime;
            // 셀 경계에서 미세 진동이 영구 지속되는 경우를 막는 저속 전용 안전장치이다.
            bool forceSettled = hasTouchedGround && hasRecentGroundContact &&
                                timeSinceFirstGroundContact >= maximumSettleWait &&
                                body.linearVelocity.sqrMagnitude <=
                                forcedSettleVelocityThreshold * forcedSettleVelocityThreshold;

            if (stableTime >= settleDuration || forceSettled)
            {
                // Sampling 도중 다시 움직이지 않도록 물리 시뮬레이션에서 고정한다.
                settled = true;
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
                body.bodyType = RigidbodyType2D.Kinematic;
                Debug.Log($"[Terrain Collapse] Chunk Settled: {name}" +
                          (forceSettled ? " (contact timeout fallback)" : string.Empty));
            }
        }

        private void UpdateInitialGroundSeparation()
        {
            if (!ignoringInitialGroundCollision) return;
            float droppedDistance = initialBodyY - body.position.y;
            float ignoredTime = Time.time - initialIgnoreStartTime;
            if (droppedDistance < initialCollisionIgnoreDropDistance &&
                ignoredTime < maximumInitialCollisionIgnoreTime)
                return;

            Physics2D.IgnoreCollision(compositeCollider, initiallyIgnoredGround, false);
            ignoringInitialGroundCollision = false;
            initiallyIgnoredGround = null;
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            UpdateGroundContact(collision);
        }

        private void OnCollisionStay2D(Collision2D collision)
        {
            UpdateGroundContact(collision);
        }

        /// <summary>
        /// 정적 Collider이면서 접촉 법선이 충분히 위를 향할 때만 지지 접촉으로 인정한다.
        /// 측면 및 꼭짓점의 대각선 접촉은 안정화 조건이 아니므로 Chunk가 계속 하강한다.
        /// </summary>
        private void UpdateGroundContact(Collision2D collision)
        {
            // Player나 다른 동적 Chunk와의 접촉은 '지면 접촉'으로 계산하지 않는다.
            Rigidbody2D otherBody = collision.rigidbody;
            bool staticSurface = otherBody == null || otherBody.bodyType == RigidbodyType2D.Static;
            int supportingContactCount = 0;
            float minimumContactX = float.PositiveInfinity;
            float maximumContactX = float.NegativeInfinity;
            for (int i = 0; staticSurface && i < collision.contactCount; i++)
            {
                ContactPoint2D contact = collision.GetContact(i);
                if (contact.normal.y < minimumSupportingNormalY) continue;
                supportingContactCount++;
                minimumContactX = Mathf.Min(minimumContactX, contact.point.x);
                maximumContactX = Mathf.Max(maximumContactX, contact.point.x);
            }

            // 꼭짓점 하나만 닿은 상태는 지지가 아니다. 위쪽 법선 접점이 둘 이상이며
            // 접점 사이에 실제 수평 폭이 있을 때만 바닥 면에 놓인 것으로 판단한다.
            bool supportingSurface = supportingContactCount >= 2 &&
                                     maximumContactX - minimumContactX >= minimumSupportContactWidth;

            if (staticSurface && supportingSurface)
            {
                groundContacts.Add(collision.collider);
                hasTouchedGround = true;
                lastGroundContactTime = Time.time;
            }
            else
            {
                groundContacts.Remove(collision.collider);
            }
        }

        private void OnCollisionExit2D(Collision2D collision)
        {
            if (groundContacts.Remove(collision.collider)) lastGroundContactTime = Time.time;
        }
    }
}
