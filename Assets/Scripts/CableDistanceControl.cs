using UnityEngine;
using ROS2;
using std_msgs.msg;
using TMPro;
using System.Collections;

public class CableDistanceControl : MonoBehaviour
{
    [Header("Estado")]
    public bool isActivated = false;

    [Header("ROS2 Configuration")]
    [SerializeField] private string cableTopic = "/cable_distance";
    [SerializeField] private string nodeName = "cable_distance_controller";
    [SerializeField] private float maxDistanceM = 190f;
    [SerializeField] private float minDistanceM = 0f;
    [SerializeField] private float stepMeters = 1f;
    [SerializeField] private float keepAliveRateHz = 5.0f;

    [Header("Input Mapping (configurable)")]
    [SerializeField] private OVRInput.Button increaseButton = OVRInput.Button.One;
    [SerializeField] private OVRInput.Controller increaseController = OVRInput.Controller.RTouch;
    [SerializeField] private OVRInput.Button decreaseButton = OVRInput.Button.Two;
    [SerializeField] private OVRInput.Controller decreaseController = OVRInput.Controller.LTouch;
    [SerializeField] private OVRInput.Button toggleButton = OVRInput.Button.Start;

    [Header("Debug")]
    [SerializeField] private bool verboseDebugLogs = true;

    [Header("UI Feedback")]
    public ToggleIconFeedback iconFeedback;

    [Header("Feedback de valor")]
    public TMP_Text publishedValueText;
    [SerializeField] private float valueFeedbackDuration = 2.0f;
    [SerializeField] private float valueFeedbackFadeTime = 0.25f;

    // Internal
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private IPublisher<Float32> distancePublisher;
    private float currentDistanceM = 1f; // start at 1 meter
    private float keepAliveTimer = 0f;
    private bool ros2Initialized = false;
    private Coroutine valueFeedbackRoutine;
    private CanvasGroup valueFeedbackCanvasGroup;

    private void Start()
    {
        InitializeROS2();
        if (iconFeedback != null)
            iconFeedback.UpdateIcon(isActivated);

        PrepareValueFeedback();
    }

    private void PrepareValueFeedback()
    {
        if (publishedValueText == null)
        {
            return;
        }

        valueFeedbackCanvasGroup = publishedValueText.GetComponent<CanvasGroup>();
        if (valueFeedbackCanvasGroup == null)
        {
            valueFeedbackCanvasGroup = publishedValueText.gameObject.AddComponent<CanvasGroup>();
        }

        valueFeedbackCanvasGroup.alpha = 0f;
        publishedValueText.gameObject.SetActive(false);
    }

    private void InitializeROS2()
    {
        ros2Unity = GetComponentInParent<ROS2UnityComponent>();
        if (ros2Unity == null)
        {
            Debug.LogError("[CableDistanceControl] ROS2UnityComponent not found in parent");
            return;
        }

        if (!ros2Unity.Ok())
        {
            Debug.LogError("[CableDistanceControl] ROS2 is not ready");
            return;
        }

        ros2Node = ros2Unity.CreateNode(nodeName);
        if (ros2Node == null)
        {
            Debug.LogError("[CableDistanceControl] Failed to create ROS2 node");
            return;
        }

        QualityOfServiceProfile qos = new QualityOfServiceProfile(QosPresetProfile.SENSOR_DATA);
        distancePublisher = ros2Node.CreatePublisher<Float32>(cableTopic, qos);

        ros2Initialized = true;
        if (verboseDebugLogs)
            Debug.Log($"[CableDistanceControl] Initialized and publishing to {cableTopic}");
    }

    private void Update()
    {
        if (!ros2Initialized)
            return;

        // Toggle activation
        if (OVRInput.GetDown(toggleButton))
        {
            ToggleActivation();
        }

        if (!isActivated)
        {
            keepAliveTimer = 0f;
            return;
        }

        bool changed = false;

        if (OVRInput.GetDown(increaseButton, increaseController))
        {
            IncreaseDistance();
            changed = true;
        }

        if (OVRInput.GetDown(decreaseButton, decreaseController))
        {
            DecreaseDistance();
            changed = true;
        }

        keepAliveTimer += Time.deltaTime;
        float keepAliveInterval = 1f / Mathf.Max(1f, keepAliveRateHz);

        if (changed || keepAliveTimer >= keepAliveInterval)
        {
            PublishDistance();
            keepAliveTimer = 0f;
        }
    }

    private void PublishDistance()
    {
        var msg = new Float32();
        msg.Data = currentDistanceM;
        distancePublisher.Publish(msg);

        ShowValueFeedback($"CABLE: {currentDistanceM:F1} m");

        if (verboseDebugLogs)
            Debug.Log($"[CableDistanceControl] Published distance: {currentDistanceM:F3} m");
    }

    private void ShowValueFeedback(string message)
    {
        if (publishedValueText == null)
        {
            return;
        }

        publishedValueText.text = message;
        publishedValueText.gameObject.SetActive(true);
        valueFeedbackCanvasGroup.alpha = 1f;

        if (valueFeedbackRoutine != null)
        {
            StopCoroutine(valueFeedbackRoutine);
        }

        valueFeedbackRoutine = StartCoroutine(HideValueFeedbackAfterDelay());
    }

    private IEnumerator HideValueFeedbackAfterDelay()
    {
        yield return new WaitForSeconds(valueFeedbackDuration);

        if (publishedValueText == null || valueFeedbackCanvasGroup == null)
        {
            yield break;
        }

        float elapsed = 0f;
        float fadeTime = Mathf.Max(0.01f, valueFeedbackFadeTime);

        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            valueFeedbackCanvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsed / fadeTime);
            yield return null;
        }

        valueFeedbackCanvasGroup.alpha = 0f;
        publishedValueText.gameObject.SetActive(false);
        valueFeedbackRoutine = null;
    }

    public void SetDistanceMeters(float meters)
    {
        currentDistanceM = Mathf.Clamp(meters, minDistanceM, maxDistanceM);
        if (verboseDebugLogs)
            Debug.Log($"[CableDistanceControl] Set distance to {currentDistanceM:F3} m");
    }

    public void IncreaseDistance()
    {
        SetDistanceMeters(currentDistanceM + stepMeters);
    }

    public void DecreaseDistance()
    {
        SetDistanceMeters(currentDistanceM - stepMeters);
    }

    public void SetStepMeters(float newStep)
    {
        stepMeters = Mathf.Max(0.01f, newStep);
    }

    public void ToggleActivation()
    {
        SetActivation(!isActivated);
    }

    public void SetActivation(bool active)
    {
        if (isActivated == active)
            return;

        isActivated = active;

        if (!isActivated)
        {
            keepAliveTimer = 0f;
        }

        if (iconFeedback != null)
            iconFeedback.UpdateIcon(isActivated);

        if (verboseDebugLogs)
            Debug.Log($"[CableDistanceControl] Control SPOOL {(isActivated ? "ACTIVO" : "INACTIVO")}");
    }

    private void OnDestroy()
    {
        if (valueFeedbackRoutine != null)
        {
            StopCoroutine(valueFeedbackRoutine);
            valueFeedbackRoutine = null;
        }

        distancePublisher = null;
    }
}
