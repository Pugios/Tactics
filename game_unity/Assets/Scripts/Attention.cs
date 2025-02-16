using UnityEngine;

public class Attention : MonoBehaviour
{
    public Camera mainCamera;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    void Update()
    {
        Vector3 mousePosition = Input.mousePosition;
        Vector3 worldPosition = mainCamera.ScreenToWorldPoint(new Vector3(mousePosition.x, mousePosition.y, mainCamera.nearClipPlane));

        transform.position = new Vector3(worldPosition.x, worldPosition.y, transform.position.z);
    }
}
