using UnityEngine;

public class Health : MonoBehaviour
{
    public float maxHP = 150f;
    public float currentHP;

    void Awake()
    {
        currentHP = maxHP;
    }

    public void ApplyDamage(float amount)
    {
        currentHP -= amount;
        Debug.Log($"{gameObject.name} HP: {currentHP}");

        if (currentHP <= 0f)
        {
            Die();
        }
    }

    void Die()
    {
        Debug.Log($"{gameObject.name} died");
        gameObject.SetActive(false);
    }
}
