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

        private Rigidbody2D body;
        private CompositeCollider2D compositeCollider;
        private float stableTime;
        private float timeSinceFirstGroundContact;
        private float lastGroundContactTime = float.NegativeInfinity;
        private readonly HashSet<Collider2D> groundContacts = new();
        private bool hasTouchedGround;
        private bool settled;

        public bool IsSettled => settled;
        public Collider2D ShapeCollider => compositeCollider;
        public Bounds WorldBounds => compositeCollider.bounds;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            compositeCollider = GetComponent<CompositeCollider2D>();
        }

        private void FixedUpdate()
        {
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

        private void OnCollisionEnter2D(Collision2D collision)
        {
            // Player나 다른 동적 Chunk와의 접촉은 '지면 접촉'으로 계산하지 않는다.
            Rigidbody2D otherBody = collision.rigidbody;
            if (otherBody == null || otherBody.bodyType == RigidbodyType2D.Static)
            {
                groundContacts.Add(collision.collider);
                hasTouchedGround = true;
                lastGroundContactTime = Time.time;
            }
        }

        private void OnCollisionStay2D(Collision2D collision)
        {
            if (groundContacts.Contains(collision.collider)) lastGroundContactTime = Time.time;
        }

        private void OnCollisionExit2D(Collision2D collision)
        {
            if (groundContacts.Remove(collision.collider)) lastGroundContactTime = Time.time;
        }
    }
}
