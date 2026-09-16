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

        [Header("Vehicle prefab types")]
        [SerializeField] private GameObject[] vehiclePrefabs;
        [SerializeField] private int selectedVehiclePrefab;
        [SerializeField] private Transform vehicleVisualParent;
        [SerializeField] private GameObject currentVehicleVisual;

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
        [SerializeField] private float cameraLookHeight = 1f;
        [SerializeField] private float cameraPositionSmoothTime = 0.2f;
        [SerializeField] private float cameraRotationSmoothSpeed = 10f;

        private float steeringInput;
        private float throttleInput;
        private bool brakeInput;
        private float wheelSpinAngle;
        private Quaternion frontLeftInitialRotation;
        private Quaternion frontRightInitialRotation;
        private Quaternion rearLeftInitialRotation;
        private Quaternion rearRightInitialRotation;
        private bool driftArmed;
        private bool isDrifting;
        private float driftBoostTimer;
        private Vector3 cameraPositionVelocity;
        private bool controlEnabled = true;

        public float Acceleration => acceleration;
        public float MaxSpeed => maxSpeed;
        public float Steering => steering;
        public float Friction => friction;

        public GameObject[] VehiclePrefabs => vehiclePrefabs;
        public int SelectedVehiclePrefab => selectedVehiclePrefab;

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
                vehicleRigidbody.constraints |= RigidbodyConstraints.FreezeRotationX
                    | RigidbodyConstraints.FreezeRotationZ;
            }

            if (cameraRig == null && vehicleCamera != null)
            {
                cameraRig = vehicleCamera.transform.parent;
            }

            // Keep the follow rig in world space. If it remains parented to the
            // Rigidbody kart, the kart rotation is inherited before LateUpdate
            // applies its own look rotation and the camera can pitch downward.
            if (cameraRig != null && cameraRig != transform && cameraRig.IsChildOf(transform))
            {
                cameraRig.SetParent(null, true);
            }

            ReplaceVehicleVisual();
            BindWheelVisuals();
            frontLeftInitialRotation = GetInitialWheelRotation(frontLeftVisual);
            frontRightInitialRotation = GetInitialWheelRotation(frontRightVisual);
            rearLeftInitialRotation = GetInitialWheelRotation(rearLeftVisual);
            rearRightInitialRotation = GetInitialWheelRotation(rearRightVisual);
        }

        private void ReplaceVehicleVisual()
        {
            if (vehiclePrefabs == null || vehiclePrefabs.Length == 0)
            {
                return;
            }

            int prefabIndex = Mathf.Clamp(selectedVehiclePrefab, 0, vehiclePrefabs.Length - 1);
            GameObject selectedPrefab = vehiclePrefabs[prefabIndex];
            if (selectedPrefab == null)
            {
                return;
            }

            Transform parent = vehicleVisualParent != null ? vehicleVisualParent : transform;
            if (currentVehicleVisual != null)
            {
                Destroy(currentVehicleVisual);
            }

            currentVehicleVisual = Instantiate(selectedPrefab, parent);
            currentVehicleVisual.name = selectedPrefab.name;

            frontLeftVisual = null;
            frontRightVisual = null;
            rearLeftVisual = null;
            rearRightVisual = null;
        }

        private void BindWheelVisuals()
        {
            frontLeftVisual ??= FindChildByName("wheel-front-left");
            frontRightVisual ??= FindChildByName("wheel-front-right");
            rearLeftVisual ??= FindChildByName("wheel-back-left");
            rearRightVisual ??= FindChildByName("wheel-back-right");
        }

        private GameObject FindChildByName(string childName)
        {
            Transform[] children = GetComponentsInChildren<Transform>(true);
            foreach (Transform child in children)
            {
                if (child.name.Equals(childName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return child.gameObject;
                }
            }

            return null;
        }

        private void Update()
        {
            if (!controlEnabled)
            {
                ResetInputState();
                UpdateWheelVisuals();
                return;
            }

            Keyboard keyboard = Keyboard.current;
            steeringInput = 0f;
            throttleInput = 0f;
            brakeInput = false;

            if (keyboard != null)
            {
                // Match the movement used by the Michael scene: the kart moves
                // forward automatically, while holding S switches to reverse.
                throttleInput = keyboard.sKey.isPressed ? -1f : 1f;
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
            if (vehicleRigidbody == null || !controlEnabled)
            {
                return;
            }

            ApplyArcadeMovement();
            ApplySteering();
        }

        private void ApplyArcadeMovement()
        {
            Vector3 localVelocity = transform.InverseTransformDirection(vehicleRigidbody.linearVelocity);
            bool isBraking = brakeInput && !isDrifting;
            bool isTurning = Mathf.Abs(steeringInput) > 0.01f;
            float targetSpeed = isBraking ? 0f : throttleInput * maxSpeed;
            float speedDifference = targetSpeed - localVelocity.z;
            bool applyTurnOrDriftReduction = isDrifting || (isTurning && !brakeInput);
            if (applyTurnOrDriftReduction && Mathf.Abs(localVelocity.z) > 0.01f)
            {
                speedDifference -= Mathf.Sign(localVelocity.z)
                    / Mathf.Max(brakeStrength, 0.01f);
            }

            float accelerationMultiplier = driftBoostTimer > 0f ? driftBoostMultiplier : 1f;
            float currentAcceleration = acceleration * accelerationMultiplier;
            float forceLimit = isBraking ? brakeStrength : currentAcceleration;
            float maximumSpeedChange = forceLimit * Time.fixedDeltaTime;
            float speedChange = Mathf.Clamp(
                speedDifference,
                -maximumSpeedChange,
                maximumSpeedChange);
            float accelerationForce = speedChange / Time.fixedDeltaTime;

            Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            vehicleRigidbody.AddForce(flatForward * accelerationForce, ForceMode.Acceleration);

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
            if (vehicleCamera == null || !vehicleCamera.isActiveAndEnabled)
            {
                return;
            }

            Transform followTransform = cameraRig != null
                ? cameraRig
                : vehicleCamera.transform;

            Quaternion cameraYawRotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            Vector3 desiredPosition = transform.position + cameraYawRotation * cameraOffset;
            float accelerationFactor = Mathf.Clamp01(acceleration / Mathf.Max(maxSpeed, 0.01f));
            float adjustedSmoothTime = cameraPositionSmoothTime
                / (1f + accelerationFactor);
            if (vehicleRigidbody != null)
            {
                Vector3 planarVelocity = Vector3.ProjectOnPlane(
                    vehicleRigidbody.linearVelocity,
                    Vector3.up);
                desiredPosition += planarVelocity * adjustedSmoothTime;
            }

            followTransform.position = Vector3.SmoothDamp(
                followTransform.position,
                desiredPosition,
                ref cameraPositionVelocity,
                Mathf.Max(adjustedSmoothTime, 0.001f));

            Vector3 lookDirection = transform.position + Vector3.up * cameraLookHeight - followTransform.position;
            if (lookDirection.sqrMagnitude > 0.001f)
            {
                Quaternion desiredRotation = Quaternion.LookRotation(lookDirection, Vector3.up);
                float rotationSmoothing = 1f - Mathf.Exp(
                    -Mathf.Max(cameraRotationSmoothSpeed, 0f) * Time.deltaTime);
                followTransform.rotation = Quaternion.Slerp(
                    followTransform.rotation,
                    desiredRotation,
                    rotationSmoothing);
            }
        }

        public void SetControlEnabled(bool enabled)
        {
            controlEnabled = enabled;
            if (!enabled)
            {
                ResetInputState();
            }
        }

        public void SetCameraEnabled(bool enabled)
        {
            if (vehicleCamera == null)
            {
                return;
            }

            vehicleCamera.enabled = enabled;
            AudioListener listener = vehicleCamera.GetComponent<AudioListener>();
            if (listener != null)
            {
                listener.enabled = enabled;
            }
        }

        private void ResetInputState()
        {
            steeringInput = 0f;
            throttleInput = 0f;
            brakeInput = false;
            driftArmed = false;
            isDrifting = false;
            driftBoostTimer = 0f;
        }
    }
}
