using UnityEngine;
using UnityEngine.InputSystem;
using ROS2;
using geometry_msgs.msg;
using builtin_interfaces.msg;
using UnityEngine.Rendering;

public class QuestVelocityControl : MonoBehaviour
{
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private IPublisher<geometry_msgs.msg.TwistStamped> twistPublisher;
    private IPublisher<geometry_msgs.msg.PoseStamped> posePublisher;
    private bool rosInitLogged;

    [Header("Estado de Control")]
    public bool isActivated = false;

    [Header("Configuraci�n")]
    public string topicName = "/tp_controller/tasks/bravo_ee_configuration_feedforward/feedforward";
    public string holdPoseTopicName = "/tp_controller/tasks/bravo_ee_configuration_feedforward/target";
    public string frameId = "base_link";
    public float maxSpeed = 1.0f;
    public float deadZone = 0.02f;
    public bool publishAngularVelocity = true;
    public float angularGain = 2f;
    public float maxAngularSpeed = 1.5f;
    public float angularDeadZoneDeg = 1.5f;
    public float publishRate = 10f; // Hz - Limitar a 10 publicaciones por segundo

    [Header("TF Pose al Soltar")]
    public string tfWorldFrame = "world_ned";
    public string tfToolFrame = "girona500/bravo/gripper/camera";
    public TF_Suscriber tfSubscriber;

    [Header("Referencias Visuales")]
    public GameObject originVisualPrefab;
    private GameObject activeVisual;
    public LineRenderer controlLine;
    public float lineWidth = 0.005f;
    public Color lineColor = Color.cyan;
    private UnityEngine.Vector3 anchorPosition;
    private UnityEngine.Quaternion anchorRotation;
    private bool isControlling = false;
    private float publishTimer = 0f;
    private Material runtimeLineMaterial;

    public ToggleIconFeedback iconFeedback;

    void Start()
    {
        ros2Unity = Object.FindAnyObjectByType<ROS2UnityComponent>();
        topicName = topicName.Trim();
        holdPoseTopicName = holdPoseTopicName.Trim();

        EnsureLineRenderer();

        if (ros2Unity == null)
        {
            Debug.LogError("[QuestVelocityControl] No se encontró ROS2UnityComponent en la escena.");
        }

        if (originVisualPrefab != null)
        {
            activeVisual = Instantiate(originVisualPrefab);
            activeVisual.SetActive(false);
        }
        // Llamamos al cambio de icono
        if (iconFeedback != null)
        {
            iconFeedback.UpdateIcon(isActivated);
        }
    }

    private void OnDestroy()
    {
        if (runtimeLineMaterial != null)
        {
            Destroy(runtimeLineMaterial);
            runtimeLineMaterial = null;
        }
    }

    void Update()
    {
        EnsureRosPublisherInitialized();

        // Bloqueo de seguridad
        if (!isActivated)
        {
            if (controlLine != null) controlLine.enabled = false;
            return;
        }

        var rightHand = UnityEngine.InputSystem.XR.XRController.rightHand;
        if (rightHand == null)
        {
            if (controlLine != null) controlLine.enabled = false;
            return;
        }

        var gripAction = rightHand["gripPressed"] as UnityEngine.InputSystem.Controls.ButtonControl;

        if (gripAction != null)
        {
            if (gripAction.wasPressedThisFrame) StartVelocityControl(rightHand);
            
            if (gripAction.isPressed && isControlling)
            {
                UpdateControlLine(rightHand);
                publishTimer += UnityEngine.Time.deltaTime;
                
                // Solo publicar a la frecuencia especificada (10 Hz por defecto)
                if (publishRate <= 0f || publishTimer >= 1f / publishRate)
                {
                    PublishVelocity(rightHand);
                    publishTimer = 0f;
                }
            }
            
            if (gripAction.wasReleasedThisFrame) StopVelocityControl();
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

        if (!isActivated)
        {
            isControlling = false;
            if (activeVisual != null) activeVisual.SetActive(false);
            if (controlLine != null) controlLine.enabled = false;
            StopVibration();

            // SEGURIDAD: Enviar parada al desactivar el control
            SendStopMessage();
            SendHoldPoseMessage();
        }
    }

    private void StartVelocityControl(UnityEngine.InputSystem.XR.XRController hand)
    {
        isControlling = true;
        publishTimer = 0f; // Resetear timer para publicar inmediatamente
        anchorPosition = hand.devicePosition.ReadValue();
        anchorRotation = hand.deviceRotation.ReadValue();
        OVRInput.SetControllerVibration(1f, 0.5f, OVRInput.Controller.RTouch);
        Invoke("StopVibration", 0.1f);
        if (activeVisual != null) { activeVisual.transform.position = anchorPosition; activeVisual.SetActive(true); }

        if (controlLine != null)
        {
            controlLine.enabled = true;
            UpdateControlLine(hand);
        }
    }

    private void PublishVelocity(UnityEngine.InputSystem.XR.XRController hand)
    {
        if (twistPublisher == null) return;

        UnityEngine.Vector3 delta = hand.devicePosition.ReadValue() - anchorPosition;
        UnityEngine.Vector3 localDeltaPosXr = UnityEngine.Quaternion.Inverse(anchorRotation) * delta;
        if (localDeltaPosXr.magnitude < deadZone) localDeltaPosXr = UnityEngine.Vector3.zero;

        geometry_msgs.msg.TwistStamped msg = new geometry_msgs.msg.TwistStamped();
        msg.Header.Frame_id = frameId;
        msg.Header.Stamp = GetRosTimeManual();

        msg.Twist.Linear.X = Mathf.Clamp(delta.z * 2f, -maxSpeed, maxSpeed);
        msg.Twist.Linear.Y = Mathf.Clamp(delta.x * 2f, -maxSpeed, maxSpeed);
        msg.Twist.Linear.Z = Mathf.Clamp(-delta.y * 2f, -maxSpeed, maxSpeed);

        if (publishAngularVelocity)
        {
            UnityEngine.Quaternion currentRotation = hand.deviceRotation.ReadValue();
            UnityEngine.Quaternion deltaRotation = UnityEngine.Quaternion.Inverse(anchorRotation) * currentRotation;
            deltaRotation = UnityEngine.Quaternion.Normalize(deltaRotation);
            deltaRotation.ToAngleAxis(out float angleDeg, out UnityEngine.Vector3 axis);

            if (angleDeg > 180f)
            {
                angleDeg -= 360f;
            }

            UnityEngine.Vector3 angularUnity = UnityEngine.Vector3.zero;
            if (!float.IsNaN(axis.x) && !float.IsNaN(axis.y) && !float.IsNaN(axis.z) && Mathf.Abs(angleDeg) >= angularDeadZoneDeg)
            {
                float angleRad = angleDeg * Mathf.Deg2Rad;
                angularUnity = axis.normalized * (angleRad * angularGain);
            }

            msg.Twist.Angular.X = Mathf.Clamp(angularUnity.z, -maxAngularSpeed, maxAngularSpeed);
            msg.Twist.Angular.Y = Mathf.Clamp(angularUnity.x, -maxAngularSpeed, maxAngularSpeed);
            msg.Twist.Angular.Z = Mathf.Clamp(-angularUnity.y, -maxAngularSpeed, maxAngularSpeed);
        }
        else
        {
            msg.Twist.Angular.X = 0.0;
            msg.Twist.Angular.Y = 0.0;
            msg.Twist.Angular.Z = 0.0;
        }

        twistPublisher.Publish(msg);
    }

    private void StopVelocityControl()
    {
        isControlling = false;
        if (activeVisual != null) activeVisual.SetActive(false);
        if (controlLine != null) controlLine.enabled = false;
        SendStopMessage();
        SendHoldPoseMessage();
        StopVibration();
    }

    private void EnsureLineRenderer()
    {
        if (controlLine == null)
        {
            GameObject lineObject = new GameObject("QuestVelocityControlLine");
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
            Debug.LogWarning("[QuestVelocityControl] No se encontro shader para la linea. Se usara el material actual del LineRenderer.");
            return controlLine != null ? controlLine.material : null;
        }

        runtimeLineMaterial = new Material(shader)
        {
            name = "QuestVelocityControlLineMaterial"
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

    private void UpdateControlLine(UnityEngine.InputSystem.XR.XRController hand)
    {
        if (controlLine == null) return;

        controlLine.SetPosition(0, anchorPosition);
        controlLine.SetPosition(1, hand.devicePosition.ReadValue());
    }

    private void SendStopMessage()
    {
        if (twistPublisher != null)
        {
            geometry_msgs.msg.TwistStamped stopMsg = new geometry_msgs.msg.TwistStamped();
            stopMsg.Header.Frame_id = frameId;
            stopMsg.Header.Stamp = GetRosTimeManual();
            twistPublisher.Publish(stopMsg);
        }
    }

    private void SendHoldPoseMessage()
    {
        if (posePublisher == null) return;

        TF_Suscriber subscriber = tfSubscriber != null ? tfSubscriber : TF_Suscriber.Instance;
        if (subscriber == null)
        {
            Debug.LogWarning("[QuestVelocityControl] No hay TF_Suscriber para publicar PoseStamped de retencion.");
            return;
        }

        if (!subscriber.TryGetTransform(tfWorldFrame, tfToolFrame, out TF_Suscriber.TFData tfData))
        {
            Debug.LogWarning($"[QuestVelocityControl] TF no disponible para pose de retencion: '{tfWorldFrame}' -> '{tfToolFrame}'.");
            return;
        }

        geometry_msgs.msg.PoseStamped holdPose = new geometry_msgs.msg.PoseStamped();
        holdPose.Header.Frame_id = tfData.ParentFrame;
        holdPose.Header.Stamp = GetRosTimeManual();

        holdPose.Pose.Position.X = tfData.Translation.x;
        holdPose.Pose.Position.Y = tfData.Translation.y;
        holdPose.Pose.Position.Z = tfData.Translation.z;

        holdPose.Pose.Orientation.X = tfData.Rotation.x;
        holdPose.Pose.Orientation.Y = tfData.Rotation.y;
        holdPose.Pose.Orientation.Z = tfData.Rotation.z;
        holdPose.Pose.Orientation.W = tfData.Rotation.w;

        posePublisher.Publish(holdPose);
    }

    private void StopVibration() => OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch);

    private void EnsureRosPublisherInitialized()
    {
        if ((twistPublisher != null && posePublisher != null) || ros2Unity == null) return;
        if (!ros2Unity.Ok()) return;

        topicName = topicName.Trim();
        holdPoseTopicName = holdPoseTopicName.Trim();

        if (ros2Node == null)
        {
            ros2Node = ros2Unity.CreateNode("QuestVelocityNode");
        }

        if (twistPublisher == null)
        {
            twistPublisher = ros2Node.CreatePublisher<geometry_msgs.msg.TwistStamped>(topicName);
        }

        if (posePublisher == null)
        {
            posePublisher = ros2Node.CreatePublisher<geometry_msgs.msg.PoseStamped>(holdPoseTopicName);
        }

        if (!rosInitLogged)
        {
            Debug.Log($"[QuestVelocityControl] Publishers creados: twist='{topicName}', pose='{holdPoseTopicName}'");
            rosInitLogged = true;
        }
    }

    private builtin_interfaces.msg.Time GetRosTimeManual()
    {
        builtin_interfaces.msg.Time time = new builtin_interfaces.msg.Time();
        float unityTime = UnityEngine.Time.realtimeSinceStartup;
        time.Sec = (int)unityTime;
        time.Nanosec = (uint)((unityTime - time.Sec) * 1e9f);
        return time;
    }
}