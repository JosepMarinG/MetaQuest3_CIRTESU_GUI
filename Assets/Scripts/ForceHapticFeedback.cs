using UnityEngine;
using ROS2;
using geometry_msgs.msg;

public class ForceHapticFeedback : MonoBehaviour
{
    [Header("Estado")]
    public bool isActivated = true;

    // ===== ROS2 CONFIGURATION =====
    [Header("ROS2 Configuration")]
    [SerializeField] private string forceWrenchTopic = "/girona500/force_filtered/wrench";
    [SerializeField] private string nodeName = "force_haptic_feedback";

    // ===== HAPTIC CONFIGURATION =====
    [Header("Haptic Feedback")]
    [SerializeField] private float maxForceValue = 30f; // Máxima fuerza en unidades de ROS
    [SerializeField] private float minVibrationIntensity = 0.0f; // Mínima intensidad (0)
    [SerializeField] private float maxVibrationIntensity = 1.0f; // Máxima intensidad (1)
    [SerializeField] private float vibrationFrequency = 1.0f; // Frecuencia constante para vibración
    [SerializeField] private float keepAliveRateHz = 30.0f; // Refresco periódico para mantener vibración activa
    [SerializeField] private bool enableBothControllers = true; // Vibrar ambos mandos
    [SerializeField] private bool invertForceSign = false; // Invertir signo si es necesario

    // ===== DEBUG =====
    [Header("Debug")]
    [SerializeField] private bool verboseDebugLogs = true;

    [Header("UI Feedback")]
    public ToggleIconFeedback iconFeedback;

    // ===== INTERNAL STATE =====
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private ISubscription<WrenchStamped> forceSubscription;
    private float currentForceZ = 0f;
    private float lastVibrationIntensity = 0f;
    private float keepAliveTimer = 0f;
    private bool ros2Initialized = false;
    private int messageCount = 0;

    private void Start()
    {
        InitializeROS2();
        if (iconFeedback != null)
        {
            iconFeedback.UpdateIcon(isActivated);
        }
    }

    private void InitializeROS2()
    {
        ros2Unity = GetComponentInParent<ROS2UnityComponent>();
        if (ros2Unity == null)
        {
            Debug.LogError("[ForceHapticFeedback] ROS2UnityComponent not found in parent");
            return;
        }

        if (!ros2Unity.Ok())
        {
            Debug.LogError("[ForceHapticFeedback] ROS2 is not ready");
            return;
        }

        ros2Node = ros2Unity.CreateNode(nodeName);
        if (ros2Node == null)
        {
            Debug.LogError("[ForceHapticFeedback] Failed to create ROS2 node");
            return;
        }

        // Use SENSOR_DATA QoS for continuous force feedback
        QualityOfServiceProfile qos = new QualityOfServiceProfile(QosPresetProfile.SENSOR_DATA);

        // Subscribe to wrench topic
        forceSubscription = ros2Node.CreateSubscription<WrenchStamped>(
            forceWrenchTopic,
            msg =>
            {
                // Extract force.z from WrenchStamped message
                currentForceZ = (float)msg.Wrench.Force.Z;
                
                // Invert sign if configured
                if (invertForceSign)
                {
                    currentForceZ = -currentForceZ;
                }

                messageCount++;
                if (verboseDebugLogs && messageCount % 10 == 0)
                {
                    Debug.Log($"[ForceHapticFeedback] Message #{messageCount} - Force Z: {currentForceZ:F3}");
                }
            },
            qos);

        ros2Initialized = true;
        Debug.Log($"[ForceHapticFeedback] Initialized and subscribed to {forceWrenchTopic}");
    }

    private void Update()
    {
        if (!ros2Initialized)
            return;

        if (!isActivated)
        {
            if (lastVibrationIntensity > 0.001f)
            {
                StopAllVibration();
                lastVibrationIntensity = 0f;
            }

            keepAliveTimer = 0f;
            return;
        }

        // Map force value (0 to maxForceValue) to vibration intensity (minVibrationIntensity to maxVibrationIntensity)
        float vibrationIntensity = MapForceToVibration(currentForceZ);

        keepAliveTimer += Time.deltaTime;
        float keepAliveInterval = 1f / Mathf.Max(1f, keepAliveRateHz);
        bool intensityChanged = Mathf.Abs(vibrationIntensity - lastVibrationIntensity) > 0.01f;
        bool keepAliveDue = vibrationIntensity > 0.001f && keepAliveTimer >= keepAliveInterval;

        // En Quest, la vibración debe refrescarse periódicamente aunque la intensidad no cambie.
        if (intensityChanged || keepAliveDue)
        {
            ApplyHapticFeedback(vibrationIntensity);
            lastVibrationIntensity = vibrationIntensity;
            keepAliveTimer = 0f;
        }
    }

    private float MapForceToVibration(float forceZ)
    {
        // Clamp force value between 0 and maxForceValue
        float clampedForce = Mathf.Clamp(forceZ, 0f, maxForceValue);

        // Map from [0, maxForceValue] to [minVibrationIntensity, maxVibrationIntensity]
        float mappedIntensity = minVibrationIntensity + (clampedForce / maxForceValue) * (maxVibrationIntensity - minVibrationIntensity);

        return mappedIntensity;
    }

    private void ApplyHapticFeedback(float intensity)
    {
        if (enableBothControllers)
        {
            // Right controller
            OVRInput.SetControllerVibration(vibrationFrequency, intensity, OVRInput.Controller.RTouch);
            // Left controller
            OVRInput.SetControllerVibration(vibrationFrequency, intensity, OVRInput.Controller.LTouch);
        }
        else
        {
            // Only right controller
            OVRInput.SetControllerVibration(vibrationFrequency, intensity, OVRInput.Controller.RTouch);
        }
    }

    private void OnDestroy()
    {
        // Stop vibration when script is destroyed
        StopAllVibration();
    }

    private void StopAllVibration()
    {
        OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
        OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
    }

    // ===== PUBLIC METHODS =====
    public void SetMaxForce(float newMaxForce)
    {
        maxForceValue = Mathf.Max(0.1f, newMaxForce);
    }

    public void SetVibrationRange(float minIntensity, float maxIntensity)
    {
        minVibrationIntensity = Mathf.Clamp01(minIntensity);
        maxVibrationIntensity = Mathf.Clamp01(maxIntensity);
    }

    public void SetVibrationFrequency(float frequency)
    {
        vibrationFrequency = Mathf.Max(0f, frequency);
    }

    public float GetCurrentForceZ()
    {
        return currentForceZ;
    }

    public float GetCurrentVibrationIntensity()
    {
        return lastVibrationIntensity;
    }

    public void EnableHapticFeedback(bool enable)
    {
        SetActivation(enable);

        if (enable)
        {
            if (!ros2Initialized)
            {
                InitializeROS2();
            }
        }
    }

    public void SetActivation()
    {
        isActivated = !isActivated;

        if (!isActivated)
        {
            StopAllVibration();
            lastVibrationIntensity = 0f;
            keepAliveTimer = 0f;
        }

        if (iconFeedback != null)
        {
            iconFeedback.UpdateIcon(isActivated);
        }

        if (verboseDebugLogs)
        {
            Debug.Log($"[ForceHapticFeedback] Modo haptico {(isActivated ? "ACTIVO" : "INACTIVO")}");
        }
    }

    public void SetActivation(bool active)
    {
        if (isActivated == active)
        {
            return;
        }

        SetActivation();
    }
}
