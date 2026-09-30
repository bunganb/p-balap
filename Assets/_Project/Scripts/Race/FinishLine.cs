using UnityEngine;
using Unity.Netcode;

public class FinishLine : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        // Yang bertugas mendeteksi tabrakan garis finish HANYALAH Server (mencegah cheat)
        if (!NetworkManager.Singleton.IsServer) return;

        // Cari script PlayerLap pada objek yang menabrak (atau parent-nya)
        PlayerLap playerLap = other.GetComponentInParent<PlayerLap>();
        
        if (playerLap != null)
        {
            playerLap.OnTriggerFinishLine();
        }
    }
}