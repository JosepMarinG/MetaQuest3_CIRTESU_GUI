using UnityEngine;
using UnityEngine.InputSystem;
using ROS2;
using geometry_msgs.msg;
using builtin_interfaces.msg;
using TMPro;
using UnityEngine.Rendering;

public class PositionControlSimplified : MonoBehaviour
{
    private static PositionControlSimplified activeInstance;

    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private IPublisher<geometry_msgs.msg.PoseStamped> posePublisher;

    [Header("Estado de Control")]
    public bool isActivated = false;

    [Header("Configuracion ROS 2")]
    public string topicName = "/tp_controller/tasks/bravo_ee_configuration_feedforward/target";
    public string frameId = "base_link";
    public float publishRate = 10f;

    [Header("TF Goal en Mundo")]
    public string tfWorldFrame = "world_ned";
    public string tfToolFrame = "girona500/bravo/gripper/camera";
    public bool requireTfAtStart = true;

    [Header("Referencias Visuales")]
    public GameObject originVisualPrefab;
    private GameObject activeVisual;
    public GameObject gripperVisualPrefab;
    private GameObject activeGripperVisual;
    public GripperVisualController gripperVisualController;
    public LineRenderer controlLine;
    public float lineWidth = 0.005f;
    public Color lineColor = Color.cyan;

    [Header("Debug Publicacion")]
    public TMP_Text publishedValuesText;

    private bool isControlling = false;
    private UnityEngine.Vector3 anchorPosition;
    private UnityEngine.Quaternion anchorRotation;
    private float publishTimer = 0f;
    private Material runtimeLineMaterial;
    private bool hasWorldTfReference = false;
    private UnityEngine.Vector3 worldReferencePosition;
    private UnityEngine.Quaternion worldReferenceRotation;

    public ToggleIconFeedback iconFeedback;
    public TF_Suscriber tfSubscriber;

    [Header("XR Tracking")]
    [Tooltip("Transform del espacio de tracking XR. Si no se asigna, se usa el padre de la Main Camera como fallback.")]
    public UnityEngine.Transform trackingSpace;

    void Awake()
    {
        if (activeInstance != null && activeInstance != this)
        {
            Debug.LogWarning("[PositionControlSimplified] Hay mas de una instancia activa. Se desactiva la duplicada para evitar artefactos en XR.");
            enabled = false;
            return;
        }

        activeInstance = this;
    }

    void Start()
    {
        ros2Unity = Object.FindAnyObjectByType<ROS2UnityComponent>();
        topicName = topicName.Trim();

        if (originVisualPrefab != null)
        {
            activeVisual = Instantiate(originVisualPrefab);
            activeVisual.SetActive(false);
        }

        if (gripperVisualPrefab != null)
        {
            activeGripperVisual = Instantiate(gripperVisualPrefab);
            activeGripperVisual.SetActive(false);

            if (gripperVisualController == null)
            {
                gripperVisualController = activeGripperVisual.GetComponent<GripperVisualController>();
            }
        }

        EnsureLineRenderer();
        SetPublishedValuesText(string.Empty);

        if (iconFeedback != null)
        {
            iconFeedback.UpdateIcon(isActivated);
        }
    }

    private void OnDestroy()
    {
        if (activeInstance == this)
        {
            activeInstance = null;
        }

        if (runtimeLineMaterial != null)
        {
            Destroy(runtimeLineMaterial);
            runtimeLineMaterial = null;
        }

        if (activeGripperVisual != null)
        {
            Destroy(activeGripperVisual);
            activeGripperVisual = null;
        }
    }

    void Update()
    {
        if (!isActivated)
        {
            SetGripperVisualActive(false);
            return;
        }

        if (ros2Node == null && ros2Unity != null && ros2Unity.Ok())
        {
            ros2Node = ros2Unity.CreateNode("PositionControlSimplifiedNode");
            posePublisher = ros2Node.CreatePublisher<geometry_msgs.msg.PoseStamped>(topicName);
        }

        var rightHand = UnityEngine.InputSystem.XR.XRController.rightHand;
        if (rightHand == null)
        {
            SetGripperVisualActive(false);
            return;
        }

        UpdateGripperVisual(rightHand);
        UpdateGripperClosing(rightHand);

        var gripAction = rightHand["gripPressed"] as UnityEngine.InputSystem.Controls.ButtonControl;

        if (gripAction != null)
        {
            if (gripAction.wasPressedThisFrame) StartPositionControl(rightHand);

            if (gripAction.isPressed && isControlling)
            {
                UpdateControlLine(rightHand);
                publishTimer += UnityEngine.Time.deltaTime;

                if (publishRate <= 0f || publishTimer >= 1f / publishRate)
                {
                    PublishRelativeTransform(rightHand);
                    publishTimer = 0f;
                }
            }

            if (gripAction.wasReleasedThisFrame) StopPositionControl();
        }
    }

    public void SetActivation()
    {
        isActivated = !isActivated;
        if (iconFeedback != null)
        {
            iconFeedback.UpdateIcon(isActivated);
        }

        if (!isActivated)
        {
            isControlling = false;
            if (activeVisual != null) activeVisual.SetActive(false);
            if (controlLine != null) controlLine.enabled = false;
            SetGripperVisualActive(false);
            StopVibration();
            SetPublishedValuesText(string.Empty);
        }
    }

    private void StartPositionControl(UnityEngine.InputSystem.XR.XRController hand)
    {
        GetHandWorldPose(hand, out anchorPosition, out anchorRotation);

        hasWorldTfReference = TryCaptureWorldReferenceFromTf();
        if (requireTfAtStart && !hasWorldTfReference)
        {
            Debug.LogWarning("[PositionControlSimplified] TF no disponible al iniciar. Se mantiene feedback local mientras se espera TF de mundo.");
            SetPublishedValuesText($"Esperando TF: {tfWorldFrame} -> {tfToolFrame}");
        }

        isControlling = true;
        publishTimer = 0f;

        OVRInput.SetControllerVibration(1f, 0.5f, OVRInput.Controller.RTouch);
        Invoke("StopVibration", 0.1f);

        if (activeVisual != null)
        {
            activeVisual.transform.position = anchorPosition;
            activeVisual.transform.rotation = anchorRotation;
            activeVisual.SetActive(true);
        }

        if (controlLine != null)
        {
            controlLine.enabled = true;
            UpdateControlLine(hand);
        }
    }

    private void PublishRelativeTransform(UnityEngine.InputSystem.XR.XRController hand)
    {
        if (posePublisher == null) return;

        if (!hasWorldTfReference)
        {
            hasWorldTfReference = TryCaptureWorldReferenceFromTf();
        }

        if (requireTfAtStart && !hasWorldTfReference)
        {
            SetPublishedValuesText($"Esperando TF: {tfWorldFrame} -> {tfToolFrame}");
            return;
        }

        GetHandWorldPose(hand, out UnityEngine.Vector3 currentPosition, out UnityEngine.Quaternion currentRotation);

        UnityEngine.Vector3 worldDeltaPos = currentPosition - anchorPosition;
        UnityEngine.Vector3 localDeltaPos = UnityEngine.Quaternion.Inverse(anchorRotation) * worldDeltaPos;
        UnityEngine.Quaternion deltaRot = UnityEngine.Quaternion.Inverse(anchorRotation) * currentRotation;
        deltaRot = UnityEngine.Quaternion.Normalize(deltaRot);

        UnityEngine.Vector3 targetPosition = localDeltaPos;
        UnityEngine.Quaternion targetRotation = deltaRot;
        string outputFrameId = frameId;

        if (hasWorldTfReference)
        {
            // Modo depuracion: usar TF cruda tal cual llega y sumar el delta del mando.
            targetPosition = worldReferencePosition + localDeltaPos;
            targetRotation = UnityEngine.Quaternion.Normalize(worldReferenceRotation * deltaRot);
            outputFrameId = tfWorldFrame;
        }

        PoseStamped msg = new PoseStamped();
        msg.Header.Frame_id = outputFrameId;
        msg.Header.Stamp = GetRosTimeManual();

        msg.Pose.Position.X = targetPosition.x;
        msg.Pose.Position.Y = targetPosition.y;
        msg.Pose.Position.Z = targetPosition.z;

        msg.Pose.Orientation.X = targetRotation.x;
        msg.Pose.Orientation.Y = targetRotation.y;
        msg.Pose.Orientation.Z = targetRotation.z;
        msg.Pose.Orientation.W = targetRotation.w;

        posePublisher.Publish(msg);

        SetPublishedValuesText(
            $"Frame: {msg.Header.Frame_id}\n" +
            $"Pos [x y z]: {msg.Pose.Position.X:F3}  {msg.Pose.Position.Y:F3}  {msg.Pose.Position.Z:F3}\n" +
            $"Rot [x y z w]: {msg.Pose.Orientation.X:F3}  {msg.Pose.Orientation.Y:F3}  {msg.Pose.Orientation.Z:F3}  {msg.Pose.Orientation.W:F3}"
        );
    }

    private void StopPositionControl()
    {
        isControlling = false;
        if (activeVisual != null) activeVisual.SetActive(false);
        if (controlLine != null) controlLine.enabled = false;
        StopVibration();
    }

    private void UpdateGripperVisual(UnityEngine.InputSystem.XR.XRController hand)
    {
        if (activeGripperVisual == null) return;

        GetHandWorldPose(hand, out UnityEngine.Vector3 worldPosition, out UnityEngine.Quaternion worldRotation);
        activeGripperVisual.transform.position = worldPosition;
        activeGripperVisual.transform.rotation = worldRotation;

        if (!activeGripperVisual.activeSelf)
        {
            activeGripperVisual.SetActive(true);
        }
    }

    private void SetGripperVisualActive(bool active)
    {
        if (activeGripperVisual == null) return;
        if (activeGripperVisual.activeSelf != active)
        {
            activeGripperVisual.SetActive(active);
        }

        if (!active && gripperVisualController != null)
        {
            gripperVisualController.SetClosed(false);
        }
    }

    private void UpdateGripperClosing(UnityEngine.InputSystem.XR.XRController hand)
    {
        if (gripperVisualController == null || hand == null) return;

        var triggerAction = hand["triggerPressed"] as UnityEngine.InputSystem.Controls.ButtonControl;
        bool shouldClose = triggerAction != null && triggerAction.isPressed;
        gripperVisualController.SetClosed(shouldClose);
    }

    private void EnsureLineRenderer()
    {
        if (controlLine == null)
        {
            GameObject lineObject = new GameObject("PositionControlSimplifiedLine");
            lineObject.transform.SetParent(transform, false);
            controlLine = lineObject.AddComponent<LineRenderer>();
        }

        controlLine.useWorldSpace = true;
        controlLine.positionCount = 2;
        controlLine.startWidth = lineWidth;
        controlLine.endWidth = lineWidth;
        controlLine.startColor = lineColor;
        controlLine.endColor = lineColor;
        controlLine.numCapVertices = 8;
        controlLine.numCornerVertices = 8;
        controlLine.textureMode = LineTextureMode.Stretch;
        controlLine.alignment = LineAlignment.View;
        controlLine.shadowCastingMode = ShadowCastingMode.Off;
        controlLine.receiveShadows = false;
        controlLine.lightProbeUsage = LightProbeUsage.Off;
        controlLine.reflectionProbeUsage = ReflectionProbeUsage.Off;
        controlLine.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        controlLine.allowOcclusionWhenDynamic = false;
        controlLine.material = GetOrCreateXrSafeLineMaterial();
        controlLine.enabled = false;
    }

    private Material GetOrCreateXrSafeLineMaterial()
    {
        if (runtimeLineMaterial != null)
        {
            ApplyLineColor(runtimeLineMaterial);
            return runtimeLineMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        if (shader == null)
        {
            Debug.LogWarning("[PositionControlSimplified] No se encontro shader para la linea. Se usara el material actual del LineRenderer.");
            return controlLine != null ? controlLine.material : null;
        }

        runtimeLineMaterial = new Material(shader)
        {
            name = "PositionControlSimplifiedLineMaterial"
        };

        runtimeLineMaterial.enableInstancing = true;
        ApplyLineColor(runtimeLineMaterial);
        return runtimeLineMaterial;
    }

    private void ApplyLineColor(Material material)
    {
        if (material == null) return;

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", lineColor);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", lineColor);
        }
    }

    private bool TryCaptureWorldReferenceFromTf()
    {
        TF_Suscriber subscriber = tfSubscriber != null ? tfSubscriber : TF_Suscriber.Instance;
        if (subscriber == null)
        {
            Debug.LogWarning("[PositionControlSimplified] TF_Suscriber instance es null (ni asignado ni singleton).");
            return false;
        }

        Debug.Log($"[PositionControlSimplified] TF_Suscriber encontrado. IsReady={subscriber.IsReady}, Mensajes={subscriber.TotalTfMessages}, Updates={subscriber.TotalTransformUpdates}, Links={subscriber.UniqueTransformCount}.");

        if (!subscriber.IsReady)
        {
            Debug.LogWarning($"[PositionControlSimplified] TF_Suscriber no esta listo (IsReady=false). Mensajes recibidos={subscriber.TotalTfMessages}, Updates={subscriber.TotalTransformUpdates}, Links={subscriber.UniqueTransformCount}.");
            return false;
        }

        if (!subscriber.TryGetTransform(tfWorldFrame, tfToolFrame, out TF_Suscriber.TFData tfData))
        {
            if (!subscriber.TryGetTransform(tfToolFrame, tfWorldFrame, out TF_Suscriber.TFData inverseTfData))
            {
                Debug.LogWarning($"[PositionControlSimplified] No se encontro TF ni directa ni inversa entre '{tfWorldFrame}' y '{tfToolFrame}'.");
                return false;
            }

            UnityEngine.Vector3 inversePosition = inverseTfData.Translation;
            UnityEngine.Quaternion inverseRotation = UnityEngine.Quaternion.Normalize(inverseTfData.Rotation);

            worldReferenceRotation = UnityEngine.Quaternion.Inverse(inverseRotation);
            worldReferencePosition = -(worldReferenceRotation * inversePosition);
            return true;
        }

        worldReferencePosition = tfData.Translation;
        worldReferenceRotation = UnityEngine.Quaternion.Normalize(tfData.Rotation);
        return true;
    }

    private void UpdateControlLine(UnityEngine.InputSystem.XR.XRController hand)
    {
        if (controlLine == null) return;

        controlLine.SetPosition(0, anchorPosition);
        GetHandWorldPose(hand, out UnityEngine.Vector3 worldPosition, out _);
        controlLine.SetPosition(1, worldPosition);
    }

    private void GetHandWorldPose(UnityEngine.InputSystem.XR.XRController hand, out UnityEngine.Vector3 worldPosition, out UnityEngine.Quaternion worldRotation)
    {
        UnityEngine.Vector3 trackingPosition = hand != null ? hand.devicePosition.ReadValue() : UnityEngine.Vector3.zero;
        UnityEngine.Quaternion trackingRotation = hand != null ? hand.deviceRotation.ReadValue() : UnityEngine.Quaternion.identity;

        UnityEngine.Transform reference = trackingSpace;
        if (reference == null)
        {
            UnityEngine.Transform mainCamera = Camera.main != null ? Camera.main.transform : null;
            reference = mainCamera != null ? mainCamera.parent : null;
        }

        if (reference == null)
        {
            worldPosition = trackingPosition;
            worldRotation = trackingRotation;
            return;
        }

        worldPosition = reference.TransformPoint(trackingPosition);
        worldRotation = reference.rotation * trackingRotation;
    }

    private void SetPublishedValuesText(string text)
    {
        if (publishedValuesText == null) return;
        publishedValuesText.text = text;
    }

    private void StopVibration() => OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch);

    private builtin_interfaces.msg.Time GetRosTimeManual()
    {
        builtin_interfaces.msg.Time time = new builtin_interfaces.msg.Time();
        float unityTime = UnityEngine.Time.realtimeSinceStartup;
        time.Sec = (int)unityTime;
        time.Nanosec = (uint)((unityTime - time.Sec) * 1e9f);
        return time;
    }
}
