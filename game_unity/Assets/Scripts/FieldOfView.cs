using UnityEngine;

public class FieldOfView : MonoBehaviour
{
    [SerializeField] private LayerMask layerMask;
    [SerializeField] private float fov = 103.0f;
    [SerializeField] private float viewDistance = 50.0f;
    [SerializeField] private int rayCount = 50;

    private Mesh mesh;
    private Transform attention; 

    private void Start() {
        // Find the Attention object as a child of the player
        attention = transform.parent.Find("Attention").transform;

        // Set up the mesh and mesh filter
        mesh = new Mesh();
        GetComponent<MeshFilter>().mesh = mesh;
    }

    void LateUpdate() {
        DrawFoV();
    }

    void DrawFoV() {
        Vector3 origin = transform.position;                        // Player position
        Vector3 lookDir = (attention.position - origin).normalized;     // Player Rotation

        // Starting angle for the field of view
        float startingAngle = GetAngleFromVectorFloat(lookDir) + fov / 2f;

        // Initialize the vertices and triangles for the mesh
        Vector3[] vertices = new Vector3[rayCount + 2];
        int[] triangles = new int[rayCount * 3];

        // First vertex is the origin (center of the cone)
        vertices[0] = transform.InverseTransformPoint(origin);

        int vertexIndex = 1;
        int triangleIndex = 0;

        for (int i = 0; i <= rayCount; i++) {
            Vector3 vertex;
            Vector3 dir = GetVectorFromAngle(startingAngle);

            RaycastHit2D hit = Physics2D.Raycast(origin, dir, viewDistance, layerMask);

            if (hit.collider == null) {
                // No hit, set the vertex at the max distance
                vertex = dir * viewDistance;
            } else {
                vertex = (Vector3)hit.point - origin;
            }

            // Set the vertex in local space
            vertices[vertexIndex] = transform.InverseTransformPoint(origin + vertex);

            // Create triangles
            if (i > 0) {
                triangles[triangleIndex] = 0;
                triangles[triangleIndex + 1] = vertexIndex - 1;
                triangles[triangleIndex + 2] = vertexIndex;
                triangleIndex += 3;
            }

            vertexIndex++;
            startingAngle -= fov / rayCount;
        }

        // Update the mesh with the new vertices and triangles
        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
    }

    // Convert an angle to a direction vector
    private Vector3 GetVectorFromAngle(float angle) {
        float angleRad = angle * (Mathf.PI / 180f);
        return new Vector3(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
    }

    // Convert a direction vector to an angle
    private float GetAngleFromVectorFloat(Vector3 dir) {
        dir = dir.normalized;
        float n = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        if (n < 0) n += 360;

        return n;
    }
}
