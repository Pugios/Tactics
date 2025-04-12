using UnityEngine;

public class SimplePlayer : MonoBehaviour
{
    [SerializeField] private Transform attentionCircle;
    [SerializeField] private FieldOfView fieldOfView;
    public float runSpeed = 20.0f;
    private float moveLimiter = 0.7f;
    private Vector2 movement;

    private Rigidbody2D body;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        body = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        HandleMovement();
    }

    void HandleMovement()
    {
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");

        Vector2 directionToAttention = (attentionCircle.position - transform.position).normalized;
        Vector2 perpendicular = new Vector2(-directionToAttention.y, directionToAttention.x);

        //Rotation
        transform.right = directionToAttention;

        // Movment
        movement = directionToAttention * vertical + perpendicular * horizontal;

        // Normalize diagonal movement
        if (horizontal != 0 && vertical != 0)
        {
            movement *= moveLimiter;
        }
    }

    void FixedUpdate()
    {
        body.linearVelocity = movement * runSpeed;
    }
}
