using UnityEngine;

namespace ValorantTrainer.Objectives
{
    public class SpikeSite : MonoBehaviour
    {
        public string siteName;
        private bool isPlayerInSite;

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                isPlayerInSite = true;
                Debug.Log("Entered Site " + siteName);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                isPlayerInSite = false;
                Debug.Log("Exited Site " + siteName);
            }
        }

        public bool IsPlayerInSite() => isPlayerInSite;
    }
}
