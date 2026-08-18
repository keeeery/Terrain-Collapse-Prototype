using UnityEngine;
using UnityEngine.InputSystem;

namespace TerrainCollapsePrototype
{
    /// <summary>낙하 지형과의 충돌을 확인하기 위한 최소 기능 테스트 플레이어.</summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(CapsuleCollider2D), typeof(SpriteRenderer))]
    public sealed class TestPlayerController : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 6f;
        [SerializeField] private float jumpImpulse = 8f;
        [SerializeField] private LayerMask groundMask = ~0;
        private Rigidbody2D body;
        private CapsuleCollider2D capsule;

        private void Awake() { body = GetComponent<Rigidbody2D>(); capsule = GetComponent<CapsuleCollider2D>(); }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            float move = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            body.linearVelocity = new Vector2(move * moveSpeed, body.linearVelocity.y);
            if (keyboard.spaceKey.wasPressedThisFrame && IsGrounded()) body.AddForce(Vector2.up * jumpImpulse, ForceMode2D.Impulse);
        }

        private bool IsGrounded()
        {
            // CapsuleCast가 자기 Collider를 감지하는 일을 피하기 위해 발 바로 아래의 작은 영역만 검사한다.
            Bounds bounds = capsule.bounds;
            Vector2 probeCenter = new(bounds.center.x, bounds.min.y - .04f);
            Vector2 probeSize = new(bounds.size.x * .75f, .06f);
            return Physics2D.OverlapBox(probeCenter, probeSize, 0f, groundMask) != null;
        }
    }
}
