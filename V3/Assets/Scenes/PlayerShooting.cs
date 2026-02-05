using UnityEngine;

public class PlayerShooting : MonoBehaviour
{
    [SerializeField] private Transform head;
    [SerializeField] private Transform attention;
    [SerializeField] private float maxDistance = 50f;
    [SerializeField] private bool Player2;

    void Update()
    {
        if (!Player2 && Input.GetMouseButtonDown(0))
        {
            Fire();
        }

        if (Player2 && Input.GetKeyDown(KeyCode.RightControl))
        {
            Fire();
        }


    }

    void Fire()
    {
        Vector3 origin = head.position;
        Vector3 headAttentionPos = attention.position;
        headAttentionPos.y = 2.6f;

        Vector3 direction = (headAttentionPos - origin).normalized;

        Ray ray = new Ray(origin, direction);
        Debug.DrawLine(origin, origin + direction * maxDistance, Color.red, 0.5f);

        Health[] possibleTargets = FindObjectsOfType<Health>();
        foreach (Health targetHealth in possibleTargets)
        {
            if (targetHealth.gameObject == gameObject) continue;

            if (targetHealth == null)
            return;

            Transform targetHead = targetHealth.transform.Find("Head");
            if (targetHead == null)
                return;

            float distanceToHead = DistancePointToRay(targetHead.position, ray);

            if (distanceToHead <= 0.15f)
            {
                targetHealth.ApplyDamage(targetHealth.currentHP); // instant kill
            }
            else if (distanceToHead <= 0.4f)
            {
                targetHealth.ApplyDamage(40f);
            }
        }
    }

    float DistancePointToRay(Vector3 point, Ray ray)
    {
        Vector3 originToPoint = point - ray.origin;
        float projection = Vector3.Dot(originToPoint, ray.direction);

        Vector3 closestPoint = ray.origin + ray.direction * Mathf.Max(projection, 0f);
        return Vector3.Distance(point, closestPoint);
    }

}
