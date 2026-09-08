using UnityEngine;
using UnityEngine.InputSystem;

namespace PBalap.Vehicle
{
    /// <summary>Mario Kart-style arcade vehicle movement, wheel visuals, and follow camera.</summary>
    public sealed class ArcadeVehicleController : MonoBehaviour
    {
        [Header("GDD arcade movement")]
        [SerializeField] private float acceleration = 12f;
        [SerializeField] private float maxSpeed = 18f;
        [SerializeField] private float steering = 90f;
        [SerializeField] private float friction = 4f;
        [SerializeField] private float brakeStrength = 30f;

        [Header("References")]
        [SerializeField] private Rigidbody vehicleRigidbody;

        [Header("Wheel visuals")]
        [SerializeField] private GameObject frontLeftVisual;
        [SerializeField] private GameObject frontRightVisual;
        [SerializeField] private GameObject rearLeftVisual;
        [SerializeField] private GameObject rearRightVisual;
        [SerializeField] private float visualSteering = 30f;
        [SerializeField] private float wheelRadius = 0.35f;

        [Header("Camera")]
        [SerializeField] private Camera vehicleCamera;
        [SerializeField] private Vector3 cameraOffset = new Vector3(0f, 4f, -7f);
        [SerializeField] private float cameraPositionSmoothing = 8f;
        [SerializeField] private float cameraRotationSmoothing = 10f;
        [SerializeField] private float cameraLookHeight = 1f;

        private float steeringInput;
        private bool brakeInput;
        private float wheelSpinAngle;
        private Quaternion frontLeftInitialRotation;
        private Quaternion frontRightInitialRotation;
        private Quaternion rearLeftInitialRotation;
        private Quaternion rearRightInitialRotation;

        public float Acceleration => acceleration;
        public float MaxSpeed => maxSpeed;
        public float Steering => steering;
        public float Friction => friction;

        private void Awake()
        {
            if (vehicleRigidbody == null)
            {
                vehicleRigidbody = GetComponent<Rigidbody>();
            }

            if (vehicleRigidbody != null)
            {
                vehicleRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
                vehicleRigidbody.centerOfMass = new Vector3(0f, -0.35f, 0f);
            }

            frontLeftInitialRotation = GetInitialWheelRotation(frontLeftVisual);
            frontRightInitialRotation = GetInitialWheelRotation(frontRightVisual);
            rearLeftInitialRotation = GetInitialWheelRotation(rearLeftVisual);
            rearRightInitialRotation = GetInitialWheelRotation(rearRightVisual);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            steeringInput = 0f;
            brakeInput = false;

            if (keyboard != null)
            {
                steeringInput = (keyboard.dKey.isPressed ? 1f : 0f)
                    - (keyboard.aKey.isPressed ? 1f : 0f);
                brakeInput = keyboard.spaceKey.isPressed;
            }

            UpdateWheelVisuals();
        }

        private void FixedUpdate()
        {
            if (vehicleRigidbody == null)
            {
                return;
            }

            ApplyArcadeMovement();
            ApplySteering();
        }

        private void ApplyArcadeMovement()
        {
            Vector3 localVelocity = transform.InverseTransformDirection(vehicleRigidbody.linearVelocity);
            float targetSpeed = brakeInput ? 0f : maxSpeed;
            float speedDifference = targetSpeed - localVelocity.z;
            float forceLimit = brakeInput ? brakeStrength : acceleration;
            float accelerationForce = Mathf.Clamp(speedDifference * acceleration, -forceLimit, forceLimit);

            vehicleRigidbody.AddForce(transform.forward * accelerationForce, ForceMode.Acceleration);

            Vector3 sidewaysVelocity = transform.right * localVelocity.x;
            vehicleRigidbody.AddForce(-sidewaysVelocity * friction, ForceMode.Acceleration);

            Vector3 planarVelocity = Vector3.ProjectOnPlane(vehicleRigidbody.linearVelocity, Vector3.up);
            if (planarVelocity.magnitude > maxSpeed)
            {
                vehicleRigidbody.linearVelocity = planarVelocity.normalized * maxSpeed
                    + Vector3.up * vehicleRigidbody.linearVelocity.y;
            }
        }

        private void ApplySteering()
        {
            float forwardSpeed = Vector3.Dot(vehicleRigidbody.linearVelocity, transform.forward);
            float speedFactor = Mathf.Clamp01(Mathf.Abs(forwardSpeed) / maxSpeed);
            float reverseFactor = forwardSpeed < 0f ? -1f : 1f;
            float turnAmount = steeringInput * steering * speedFactor * reverseFactor * Time.fixedDeltaTime;

            vehicleRigidbody.MoveRotation(vehicleRigidbody.rotation * Quaternion.Euler(0f, turnAmount, 0f));
        }

        private void UpdateWheelVisuals()
        {
            float forwardSpeed = vehicleRigidbody == null
                ? 0f
                : Vector3.Dot(vehicleRigidbody.linearVelocity, transform.forward);
            wheelSpinAngle += forwardSpeed / Mathf.Max(wheelRadius, 0.01f) * Mathf.Rad2Deg * Time.deltaTime;

            UpdateWheelVisual(frontLeftVisual, frontLeftInitialRotation, true);
            UpdateWheelVisual(frontRightVisual, frontRightInitialRotation, true);
            UpdateWheelVisual(rearLeftVisual, rearLeftInitialRotation, false);
            UpdateWheelVisual(rearRightVisual, rearRightInitialRotation, false);
        }

        private static Quaternion GetInitialWheelRotation(GameObject wheelVisual)
        {
            return wheelVisual == null ? Quaternion.identity : wheelVisual.transform.localRotation;
        }

        private void UpdateWheelVisual(GameObject wheelVisual, Quaternion initialRotation, bool isFrontWheel)
        {
            if (wheelVisual == null)
            {
                return;
            }

            Transform wheelTransform = wheelVisual.transform;
            float steeringAngle = isFrontWheel ? steeringInput * visualSteering : 0f;
            wheelTransform.localRotation = initialRotation * Quaternion.Euler(wheelSpinAngle, steeringAngle, 0f);
        }

        private void LateUpdate()
        {
            if (vehicleCamera == null)
            {
                return;
            }

            Transform cameraTransform = vehicleCamera.transform;
            Vector3 desiredPosition = transform.TransformPoint(cameraOffset);
            float positionBlend = 1f - Mathf.Exp(-cameraPositionSmoothing * Time.deltaTime);
            float rotationBlend = 1f - Mathf.Exp(-cameraRotationSmoothing * Time.deltaTime);
            cameraTransform.position = Vector3.Lerp(cameraTransform.position, desiredPosition, positionBlend);

            Vector3 lookDirection = transform.position + Vector3.up * cameraLookHeight - cameraTransform.position;
            if (lookDirection.sqrMagnitude > 0.001f)
            {
                Quaternion desiredRotation = Quaternion.LookRotation(lookDirection, Vector3.up);
                cameraTransform.rotation = Quaternion.Slerp(cameraTransform.rotation, desiredRotation, rotationBlend);
            }
        }
    }
}
