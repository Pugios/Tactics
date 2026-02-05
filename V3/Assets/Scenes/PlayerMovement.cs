using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float rotationSpeed = 720f;
    [SerializeField] private string horizontalAxis = "Horizontal";
    [SerializeField] private string verticalAxis = "Vertical";
    [SerializeField] private Transform attention;

    void Update()
    {
        // if (!IsOwner) return;

        Vector3 toAttention = attention.position - transform.position;
        toAttention.y = 0f;

        if (toAttention.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(toAttention);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );
        }

        Vector3 forward = toAttention.normalized;
        Vector3 right = new Vector3(forward.z, 0f, -forward.x);

        float h = Input.GetAxisRaw(horizontalAxis);
        float v = Input.GetAxisRaw(verticalAxis);

        Vector3 moveDir = (forward * v + right * h).normalized;
        transform.position += moveDir * moveSpeed * Time.deltaTime;
    }
}
