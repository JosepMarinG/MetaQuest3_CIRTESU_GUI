using UnityEngine;
using UnityEngine.InputSystem;
using ROS2;
using geometry_msgs.msg;
using builtin_interfaces.msg;
using TMPro;
using UnityEngine.Rendering;

public class QuestPositionControl : MonoBehaviour
{
    private static QuestPositionControl activeInstance;

    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private IPublisher<geometry_msgs.msg.PoseStamped> posePublisher;

    [Header("Estado de Control")]
    public bool isActivated = false;

    [Header("Configuracion ROS 2")]
    public string topicName = "/tp_controller/tasks/bravo_ee_configuration_feedforward/target";
    public string frameId = "base_link";
    public float publishRate = 10f; // Hz - Limitar a 10 publicaciones por segundo

    [Header("TF Goal en Mundo")]
    public string tfWorldFrame = "world_net";
    public string tfToolFrame = "girona500/bravo/gripper/camera";
    public bool requireTfAtStart = true;

    [Header("Referencias Visuales")]
    public GameObject originVisualPrefab;
    private GameObject activeVisual;
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
    private UnityEngine.Vector3 worldReferencePositionUnity;
    private UnityEngine.Quaternion worldReferenceRotationUnity;

    public ToggleIconFeedback iconFeedback;
    public TF_Suscriber tfSubscriber;

    void Awake()
    {
        if (activeInstance != null && activeInstance != this)
        {
            Debug.LogWarning("[QuestPositionControl] Hay mas de una instancia activa. Se desactiva la duplicada para evitar artefactos en XR.");
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

        EnsureLineRenderer();
        SetPublishedValuesText(string.Empty);

        // Llamamos al cambio de icono
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
    }

    void Update()
    {
        // 1. Bloqueo de seguridad: si no esta activado, no hacemos nada
        if (!isActivated) return;

        if (ros2Node == null && ros2Unity != null && ros2Unity.Ok())
        {
            ros2Node = ros2Unity.CreateNode("QuestPositionNode");
            posePublisher = ros2Node.CreatePublisher<geometry_msgs.msg.PoseStamped>(topicName);
        }

        var rightHand = UnityEngine.InputSystem.XR.XRController.rightHand;
        if (rightHand == null) return;

        var gripAction = rightHand["gripPressed"] as UnityEngine.InputSystem.Controls.ButtonControl;

        if (gripAction != null)
        {
            if (gripAction.wasPressedThisFrame) StartPositionControl(rightHand);

            if (gripAction.isPressed && isControlling)
            {
                UpdateControlLine(rightHand);
                publishTimer += UnityEngine.Time.deltaTime;

                // Solo publicar a la frecuencia especificada (10 Hz por defecto)
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
        // Llamamos al cambio de icono
        if (iconFeedback != null)
        {
            iconFeedback.UpdateIcon(isActivated);
        }

        // Si desactivamos mientras estamos controlando, limpiamos el estado
        if (!isActivated)
        {
            isControlling = false;
            if (activeVisual != null) activeVisual.SetActive(false);
            if (controlLine != null) controlLine.enabled = false;
            StopVibration();
            SetPublishedValuesText(string.Empty);
        }
    }

    private void StartPositionControl(UnityEngine.InputSystem.XR.XRController hand)
    {
        anchorPosition = hand.devicePosition.ReadValue();
        anchorRotation = hand.deviceRotation.ReadValue();

        hasWorldTfReference = TryCaptureWorldReferenceFromTf();
        if (requireTfAtStart && !hasWorldTfReference)
        {
            Debug.LogWarning("[QuestPositionControl] TF no disponible al iniciar. Se mantiene feedback local mientras se espera TF de mundo.");
            SetPublishedValuesText($"Esperando TF: {tfWorldFrame} -> {tfToolFrame}");
        }

        isControlling = true;
        publishTimer = 0f; // Resetear timer para publicar inmediatamente

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

        UnityEngine.Vector3 currentPosition = hand.devicePosition.ReadValue();
        UnityEngine.Quaternion currentRotation = hand.deviceRotation.ReadValue();

        UnityEngine.Vector3 worldDeltaPos = currentPosition - anchorPosition;
        UnityEngine.Vector3 localDeltaPos = UnityEngine.Quaternion.Inverse(anchorRotation) * worldDeltaPos;
        UnityEngine.Quaternion deltaRotUnity = UnityEngine.Quaternion.Inverse(anchorRotation) * currentRotation;
        deltaRotUnity = UnityEngine.Quaternion.Normalize(deltaRotUnity);

        UnityEngine.Vector3 targetPositionUnity = localDeltaPos;
        UnityEngine.Quaternion targetRotationUnity = deltaRotUnity;
        string outputFrameId = frameId;

        if (hasWorldTfReference)
        {
            targetPositionUnity = worldReferencePositionUnity + (worldReferenceRotationUnity * localDeltaPos);
            targetRotationUnity = worldReferenceRotationUnity * deltaRotUnity;
            targetRotationUnity = UnityEngine.Quaternion.Normalize(targetRotationUnity);
            outputFrameId = tfWorldFrame;
        }

        UnityEngine.Vector3 rosDeltaPos = ConvertUnityVectorToRos(targetPositionUnity);
        UnityEngine.Quaternion rosDeltaRot = ConvertUnityQuaternionToRos(targetRotationUnity);

        geometry_msgs.msg.PoseStamped msg = new geometry_msgs.msg.PoseStamped();
        msg.Header.Frame_id = outputFrameId;
        msg.Header.Stamp = GetRosTimeManual();

        msg.Pose.Position.X = rosDeltaPos.x;
        msg.Pose.Position.Y = rosDeltaPos.y;
        msg.Pose.Position.Z = rosDeltaPos.z;

        msg.Pose.Orientation.X = rosDeltaRot.x;
        msg.Pose.Orientation.Y = rosDeltaRot.y;
        msg.Pose.Orientation.Z = rosDeltaRot.z;
        msg.Pose.Orientation.W = rosDeltaRot.w;

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

    private void EnsureLineRenderer()
    {
        if (controlLine == null)
        {
            GameObject lineObject = new GameObject("QuestPositionControlLine");
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
            Debug.LogWarning("[QuestPositionControl] No se encontro shader para la linea. Se usara el material actual del LineRenderer.");
            return controlLine != null ? controlLine.material : null;
        }

        runtimeLineMaterial = new Material(shader)
        {
            name = "QuestPositionControlLineMaterial"
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
            Debug.LogWarning("[QuestPositionControl] TF_Suscriber instance es null (ni asignado ni singleton).");
            return false;
        }

        Debug.Log($"[QuestPositionControl] TF_Suscriber encontrado. IsReady={subscriber.IsReady}, Mensajes={subscriber.TotalTfMessages}, Updates={subscriber.TotalTransformUpdates}, Links={subscriber.UniqueTransformCount}.");

        if (!subscriber.IsReady)
        {
            Debug.LogWarning($"[QuestPositionControl] TF_Suscriber no esta listo (IsReady=false). Mensajes recibidos={subscriber.TotalTfMessages}, Updates={subscriber.TotalTransformUpdates}, Links={subscriber.UniqueTransformCount}.");
            return false;
        }

        if (!subscriber.TryGetTransform(tfWorldFrame, tfToolFrame, out TF_Suscriber.TFData tfData))
        {
            if (!subscriber.TryGetTransform(tfToolFrame, tfWorldFrame, out TF_Suscriber.TFData inverseTfData))
            {
                Debug.LogWarning($"[QuestPositionControl] No se encontro TF ni directa ni inversa entre '{tfWorldFrame}' y '{tfToolFrame}'.");
                return false;
            }

            UnityEngine.Vector3 inversePositionUnity = ConvertRosVectorToUnity(inverseTfData.Translation);
            UnityEngine.Quaternion inverseRotationUnity = UnityEngine.Quaternion.Normalize(ConvertRosQuaternionToUnity(inverseTfData.Rotation));

            worldReferenceRotationUnity = UnityEngine.Quaternion.Inverse(inverseRotationUnity);
            worldReferencePositionUnity = -(worldReferenceRotationUnity * inversePositionUnity);
            return true;
        }

        worldReferencePositionUnity = ConvertRosVectorToUnity(tfData.Translation);
        worldReferenceRotationUnity = UnityEngine.Quaternion.Normalize(ConvertRosQuaternionToUnity(tfData.Rotation));
        return true;
    }

    private UnityEngine.Vector3 ConvertUnityVectorToRos(UnityEngine.Vector3 unityVector)
    {
        return new UnityEngine.Vector3(unityVector.z, -unityVector.x, unityVector.y);
    }

    private UnityEngine.Quaternion ConvertUnityQuaternionToRos(UnityEngine.Quaternion unityQuaternion)
    {
        return new UnityEngine.Quaternion(unityQuaternion.z, -unityQuaternion.x, unityQuaternion.y, unityQuaternion.w);
    }

    private UnityEngine.Vector3 ConvertRosVectorToUnity(UnityEngine.Vector3 rosVector)
    {
        return new UnityEngine.Vector3(-rosVector.y, rosVector.z, rosVector.x);
    }

    private UnityEngine.Quaternion ConvertRosQuaternionToUnity(UnityEngine.Quaternion rosQuaternion)
    {
        return new UnityEngine.Quaternion(-rosQuaternion.y, rosQuaternion.z, rosQuaternion.x, rosQuaternion.w);
    }

    private void UpdateControlLine(UnityEngine.InputSystem.XR.XRController hand)
    {
        if (controlLine == null) return;

        controlLine.SetPosition(0, anchorPosition);
        controlLine.SetPosition(1, hand.devicePosition.ReadValue());
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