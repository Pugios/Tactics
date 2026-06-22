using UnityEngine;
using System;

namespace Tactics.Sound
{
    public class SoundEmitter : MonoBehaviour
    {
        [SerializeField] private float soundRadius = 35f;

        public event Action<Vector3, float> OnSoundEmitted;
        
        private float lastMoveEmitTime;
        private float lastShootTime;
        private const float soundPersistence = 0.1f; // Short persistence to handle execution order and flickering
        private const float shootSoundPersistence = 0.3f; // Keep circle visible briefly after a shot

        public float SoundRadius => soundRadius;
        public bool IsCurrentlySounding => (Time.time - lastMoveEmitTime < soundPersistence) || (Time.time - lastShootTime < shootSoundPersistence);

        public void EmitMoveSound(bool isRunning)
        {
            if (isRunning)
            {
                lastMoveEmitTime = Time.time;
                OnSoundEmitted?.Invoke(transform.position, soundRadius);
            }
        }

        public void EmitShootSound()
        {
            lastShootTime = Time.time;
            OnSoundEmitted?.Invoke(transform.position, soundRadius);
        }
    }
}
