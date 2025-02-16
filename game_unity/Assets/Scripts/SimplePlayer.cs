using UnityEngine;

public class SimplePlayer : MonoBehaviour
{
    [SerializeField] private Transform attentionCircle;
    [SerializeField] private FieldOfView fieldOfView;


    Rigidbody2D body;
    public float runSpeed = 20.0f;

    float moveLimiter = 0.7f;
    Vector2 movement;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        body = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        HandleMovement();
        MoveFoV();
    }

    void HandleMovement()
    {
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");

        Vector2 directionToAttention = (attentionCircle.position - transform.position).normalized;
        Vector2 perpendicular = new Vector2(-directionToAttention.y, directionToAttention.x);

        movement = directionToAttention * vertical + perpendicular * horizontal;

        // Normalize diagonal movement
        if (horizontal != 0 && vertical != 0)
        {
            movement *= moveLimiter;
        }
    }

    void MoveFoV()
    {
        fieldOfView.SetOrigin(transform.position);
        fieldOfView.SetAimDirection((attentionCircle.position - transform.position).normalized);
    }

    void FixedUpdate()
    {
        body.linearVelocity = movement * runSpeed;
    }
}
