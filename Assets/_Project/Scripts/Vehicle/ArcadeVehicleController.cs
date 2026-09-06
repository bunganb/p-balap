using UnityEngine;

namespace PBalap.Vehicle
{
    /// <summary>Owner-input and server-movement work starts here.</summary>
    public sealed class ArcadeVehicleController : MonoBehaviour
    {
        [Header("GDD arcade movement")]
        [SerializeField] private float acceleration = 12f;
        [SerializeField] private float maxSpeed = 18f;
        [SerializeField] private float steering = 90f;
        [SerializeField] private float friction = 4f;

        public float Acceleration => acceleration;
        public float MaxSpeed => maxSpeed;
        public float Steering => steering;
        public float Friction => friction;
    }
}
