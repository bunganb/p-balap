using UnityEngine;

namespace PBalap.Race
{
    /// <summary>Server-authoritative race state: countdown, checkpoints, laps, and results.</summary>
    public sealed class RaceManager : MonoBehaviour
    {
        [SerializeField] private int totalLaps = 3;
        [SerializeField] private int maxPlayers = 4;

        public int TotalLaps => totalLaps;
        public int MaxPlayers => maxPlayers;
    }
}
