using UnityEngine;
using ROS2;
using std_msgs.msg;
using TMPro;
using System.Collections;

public class LarsHeightControl : MonoBehaviour
{
    [Header("Estado")]
    public bool isActivated = false;

    [Header("ROS2 Configuration")]
    [SerializeField] private string larsTopic = "/lars_height";
    [SerializeField] private string nodeName = "lars_height_controller";
    [SerializeField] private float maxHeightCm = 24.0f;
    [SerializeField] private float minHeightCm = 0.0f;
    [SerializeField] private float stepCm = 0.5f;
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
    private IPublisher<Float32> heightPublisher;
    private float currentHeightCm = 24f;
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
            Debug.LogError("[LarsHeightControl] ROS2UnityComponent not found in parent");
            return;
        }

        if (!ros2Unity.Ok())
        {
            Debug.LogError("[LarsHeightControl] ROS2 is not ready");
            return;
        }

        ros2Node = ros2Unity.CreateNode(nodeName);
        if (ros2Node == null)
        {
            Debug.LogError("[LarsHeightControl] Failed to create ROS2 node");
            return;
        }

        QualityOfServiceProfile qos = new QualityOfServiceProfile(QosPresetProfile.SENSOR_DATA);
        heightPublisher = ros2Node.CreatePublisher<Float32>(larsTopic, qos);

        ros2Initialized = true;
        if (verboseDebugLogs)
            Debug.Log($"[LarsHeightControl] Initialized and publishing to {larsTopic}");
    }

    private void Update()
    {
        if (!ros2Initialized)
            return;

        // Toggle activation (any controller, configurable button)
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
            IncreaseHeight();
            changed = true;
        }

        if (OVRInput.GetDown(decreaseButton, decreaseController))
        {
            DecreaseHeight();
            changed = true;
        }

        keepAliveTimer += Time.deltaTime;
        float keepAliveInterval = 1f / Mathf.Max(1f, keepAliveRateHz);

        if (changed || keepAliveTimer >= keepAliveInterval)
        {
            PublishHeight();
            keepAliveTimer = 0f;
        }
    }

    private void PublishHeight()
    {
        var msg = new Float32();
        msg.Data = currentHeightCm;
        heightPublisher.Publish(msg);

        ShowValueFeedback($"LARS: {currentHeightCm:F1} cm");

        if (verboseDebugLogs)
            Debug.Log($"[LarsHeightControl] Published height: {currentHeightCm:F3} cm");
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

    public void SetHeightCm(float cm)
    {
        currentHeightCm = Mathf.Clamp(cm, minHeightCm, maxHeightCm);
        if (verboseDebugLogs)
            Debug.Log($"[LarsHeightControl] Set height to {currentHeightCm:F3} cm");
    }

    public void IncreaseHeight()
    {
        SetHeightCm(currentHeightCm + stepCm);
    }

    public void DecreaseHeight()
    {
        SetHeightCm(currentHeightCm - stepCm);
    }

    public void SetStepCm(float newStep)
    {
        stepCm = Mathf.Max(0.01f, newStep);
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
            Debug.Log($"[LarsHeightControl] Control LARS {(isActivated ? "ACTIVO" : "INACTIVO")}");
    }

    private void OnDestroy()
    {
        // Stop publishing. Node lifecycle managed by ROS2UnityComponent.
        if (valueFeedbackRoutine != null)
        {
            StopCoroutine(valueFeedbackRoutine);
            valueFeedbackRoutine = null;
        }

        heightPublisher = null;
    }
}
