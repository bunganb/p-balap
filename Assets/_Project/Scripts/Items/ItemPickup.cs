using UnityEngine;

namespace PBalap.Items
{
    public class PalmOilPickup : MonoBehaviour
    {
        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                Debug.Log("Minyak Sawit berhasil diambil!");

                gameObject.SetActive(false);
            }
        }
    }
}