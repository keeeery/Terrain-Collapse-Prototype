using UnityEngine;
using UnityEngine.InputSystem;

namespace TerrainCollapsePrototype
{
    /// <summary>Kinematic Rigidbody를 직접 이동시키는 붕괴 테스트용 플레이어.</summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(SpriteRenderer))]
    public sealed class TestPlayerController : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 6f;
        [SerializeField] private float jumpSpeed = 8f;
        [SerializeField] private float gravity = 24f;
        [SerializeField] private float collisionSkin = 0.02f;
        [SerializeField] private float escapeProbeStep = 0.05f;
        [SerializeField] private LayerMask collisionMask = ~0;

        private readonly RaycastHit2D[] castHits = new RaycastHit2D[16];
        private readonly Collider2D[] overlapHits = new Collider2D[16];
        private Rigidbody2D body;
        private BoxCollider2D box;
        private Vector2 velocity;
        private bool grounded;
        private float lastEscapeTime = float.NegativeInfinity;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            box = GetComponent<BoxCollider2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.useFullKinematicContacts = true;
            body.gravityScale = 0f;
            body.freezeRotation = true;
        }

        private void FixedUpdate()
        {
            Keyboard keyboard = Keyboard.current;
            float input = keyboard == null
                ? 0f
                : (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            velocity.x = input * moveSpeed;
            if (keyboard != null && keyboard.spaceKey.isPressed && grounded) velocity.y = jumpSpeed;
            velocity.y -= gravity * Time.fixedDeltaTime;

            Vector2 position = body.position;
            position += ResolveMovement(Vector2.right, velocity.x * Time.fixedDeltaTime, ref velocity.x);
            grounded = false;
            position += ResolveMovement(Vector2.up, velocity.y * Time.fixedDeltaTime, ref velocity.y);
            body.MovePosition(position);
        }

        private Vector2 ResolveMovement(Vector2 axis, float distance, ref float axisVelocity)
        {
            if (Mathf.Approximately(distance, 0f)) return Vector2.zero;
            Vector2 direction = axis * Mathf.Sign(distance);
            float requestedDistance = Mathf.Abs(distance);
            var filter = new ContactFilter2D
            {
                useLayerMask = true,
                layerMask = collisionMask,
                useTriggers = false
            };
            int hitCount = box.Cast(direction, filter, castHits, requestedDistance + collisionSkin);
            float allowedDistance = requestedDistance;
            for (int i = 0; i < hitCount; i++)
            {
                if (castHits[i].collider == box) continue;
                allowedDistance = Mathf.Min(allowedDistance,
                    Mathf.Max(0f, castHits[i].distance - collisionSkin));
            }

            if (allowedDistance + Mathf.Epsilon < requestedDistance)
            {
                if (direction.y < 0f) grounded = true;
                axisVelocity = 0f;
            }
            return direction * allowedDistance;
        }

        /// <summary>위에서 내려오는 덩어리와 겹치지 않는 가장 가까운 좌우 위치로 밀어낸다.</summary>
        public bool TryEscapeFromFallingChunk(FallingChunk chunk)
        {
            if (chunk == null || Time.time - lastEscapeTime < Time.fixedDeltaTime) return false;
            Bounds playerBounds = box.bounds;
            Bounds chunkBounds = chunk.WorldBounds;
            bool struckFromAbove = chunkBounds.center.y > playerBounds.center.y &&
                                   chunkBounds.min.y <= playerBounds.max.y + collisionSkin;
            if (!struckFromAbove) return false;

            float maximumDistance = chunk.CellSize * 0.5f;
            int probeCount = Mathf.Max(1, Mathf.CeilToInt(maximumDistance / escapeProbeStep));
            for (int probe = 1; probe <= probeCount; probe++)
            {
                float distance = Mathf.Min(probe * escapeProbeStep, maximumDistance);
                int preferredDirection = transform.position.x <= chunkBounds.center.x ? -1 : 1;
                if (TryMoveToEmptySpace(preferredDirection * distance, chunk) ||
                    TryMoveToEmptySpace(-preferredDirection * distance, chunk))
                {
                    lastEscapeTime = Time.time;
                    velocity.x = 0f;
                    return true;
                }
            }
            return false;
        }

        private bool TryMoveToEmptySpace(float offsetX, FallingChunk sourceChunk)
        {
            Vector2 candidateCenter = (Vector2)box.bounds.center + Vector2.right * offsetX;
            var filter = new ContactFilter2D
            {
                useLayerMask = true,
                layerMask = collisionMask,
                useTriggers = false
            };
            Vector2 probeSize = (Vector2)box.bounds.size - Vector2.one * collisionSkin;
            int count = Physics2D.OverlapBox(candidateCenter, probeSize,
                0f, filter, overlapHits);
            for (int i = 0; i < count; i++)
            {
                Collider2D hit = overlapHits[i];
                if (hit == null || hit == box) continue;
                return false;
            }

            body.position += Vector2.right * offsetX;
            Physics2D.SyncTransforms();
            return true;
        }
    }
}
