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
        [SerializeField] private float driftFriction = 0.75f;
        [SerializeField] private float driftSteeringMultiplier = 1.35f;
        [SerializeField] private float driftBoostMultiplier = 2f;
        [SerializeField] private float driftBoostDuration = 1.5f;

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
        [SerializeField] private Transform cameraRig;
        [SerializeField] private Vector3 cameraOffset = new Vector3(0f, 4f, -7f);
        [SerializeField] private float cameraPositionSmoothing = 8f;
        [SerializeField] private float cameraRotationSmoothing = 10f;
        [SerializeField] private float cameraRotationDelay = 0.25f;
        [SerializeField] private float cameraLookHeight = 1f;

        private float steeringInput;
        private bool brakeInput;
        private float wheelSpinAngle;
        private Quaternion frontLeftInitialRotation;
        private Quaternion frontRightInitialRotation;
        private Quaternion rearLeftInitialRotation;
        private Quaternion rearRightInitialRotation;
        private Vector3 cameraPositionVelocity;
        private float cameraYaw;
        private float cameraYawVelocity;
        private bool driftArmed;
        private bool isDrifting;
        private float driftBoostTimer;

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

            if (cameraRig == null && vehicleCamera != null)
            {
                cameraRig = vehicleCamera.transform.parent;
            }

            if (cameraRig != null && cameraRig != transform && cameraRig.IsChildOf(transform))
            {
                cameraRig.SetParent(null, true);
            }

            cameraYaw = transform.eulerAngles.y;

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

            if (Mathf.Abs(steeringInput) > 0.01f && !brakeInput)
            {
                driftArmed = true;
            }

            bool wasDrifting = isDrifting;
            isDrifting = driftArmed && brakeInput && Mathf.Abs(steeringInput) > 0.01f;
            if (Mathf.Abs(steeringInput) <= 0.01f)
            {
                driftArmed = false;
                isDrifting = false;
            }

            if (wasDrifting && !isDrifting)
            {
                driftBoostTimer = driftBoostDuration;
            }

            driftBoostTimer = Mathf.Max(0f, driftBoostTimer - Time.deltaTime);

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
            float accelerationMultiplier = driftBoostTimer > 0f ? driftBoostMultiplier : 1f;
            float currentAcceleration = acceleration * accelerationMultiplier;
            float forceLimit = brakeInput ? brakeStrength : currentAcceleration;
            float accelerationForce = Mathf.Clamp(
                speedDifference * currentAcceleration,
                -forceLimit,
                forceLimit);

            vehicleRigidbody.AddForce(transform.forward * accelerationForce, ForceMode.Acceleration);

            Vector3 sidewaysVelocity = transform.right * localVelocity.x;
            float currentFriction = isDrifting ? driftFriction : friction;
            vehicleRigidbody.AddForce(-sidewaysVelocity * currentFriction, ForceMode.Acceleration);

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
            float steeringMultiplier = isDrifting ? driftSteeringMultiplier : 1f;
            float turnAmount = steeringInput * steering * steeringMultiplier
                * speedFactor * reverseFactor * Time.fixedDeltaTime;

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

            Transform followTransform = cameraRig != null
                ? cameraRig
                : vehicleCamera.transform;

            float cameraYawSmoothTime = Mathf.Max(cameraRotationDelay, 0.01f);
            cameraYaw = Mathf.SmoothDampAngle(
                cameraYaw,
                transform.eulerAngles.y,
                ref cameraYawVelocity,
                cameraYawSmoothTime);

            Quaternion cameraYawRotation = Quaternion.Euler(0f, cameraYaw, 0f);
            Vector3 desiredPosition = transform.position + cameraYawRotation * cameraOffset;
            float positionSmoothTime = 1f / Mathf.Max(cameraPositionSmoothing, 0.01f);
            float rotationBlend = 1f - Mathf.Exp(-cameraRotationSmoothing * Time.deltaTime);
            followTransform.position = Vector3.SmoothDamp(
                followTransform.position,
                desiredPosition,
                ref cameraPositionVelocity,
                positionSmoothTime);

            Vector3 cameraDisplacement = followTransform.position - transform.position;
            if (cameraDisplacement.sqrMagnitude > 0.001f)
            {
                followTransform.position = transform.position
                    + cameraDisplacement.normalized * cameraOffset.magnitude;
            }

            Vector3 lookDirection = transform.position + Vector3.up * cameraLookHeight - followTransform.position;
            if (lookDirection.sqrMagnitude > 0.001f)
            {
                Quaternion desiredRotation = Quaternion.LookRotation(lookDirection, Vector3.up);
                followTransform.rotation = Quaternion.Slerp(followTransform.rotation, desiredRotation, rotationBlend);
            }
        }
    }
}
