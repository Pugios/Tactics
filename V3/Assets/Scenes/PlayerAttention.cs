using UnityEngine;

public class PlayerAttention : MonoBehaviour
{
    [SerializeField] private GameObject attentionPrefab;
    [HideInInspector] public Transform attention;

    void Awake()
    {
        if(attentionPrefab != null)
        {
            GameObject att = Instantiate(attentionPrefab);
            attention = att.transform;
        }
    }
}
