using UnityEngine;
using UnityEngine.InputSystem;
using ROS2;
using geometry_msgs.msg;
using builtin_interfaces.msg;

public class QuestPositionControl : MonoBehaviour
{
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private IPublisher<geometry_msgs.msg.PoseStamped> posePublisher;

    [Header("Estado de Control")]
    public bool isActivated = false;

    [Header("Configuraci�n ROS 2")]
    public string topicName = "/robot/cmd_pose";
    public string frameId = "base_link";
    public float publishRate = 10f; // Hz - Limitar a 10 publicaciones por segundo

    [Header("Referencias Visuales")]
    public GameObject originVisualPrefab;
    private GameObject activeVisual;

    private bool isControlling = false;
    private UnityEngine.Vector3 anchorPosition;
    private UnityEngine.Quaternion anchorRotation;
    private float publishTimer = 0f;

    public ToggleIconFeedback iconFeedback;

    void Start()
    {
        ros2Unity = Object.FindAnyObjectByType<ROS2UnityComponent>();
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

    void Update()
    {
        // 1. Bloqueo de seguridad: si no est� activado, no hacemos nada
        if (!isActivated) return;

        if (ros2Node == null && ros2Unity.Ok())
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
                publishTimer += UnityEngine.Time.deltaTime;
                
                // Solo publicar a la frecuencia especificada (10 Hz por defecto)
                if (publishTimer >= 1f / publishRate)
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
            StopVibration();
        }
    }

    private void StartPositionControl(UnityEngine.InputSystem.XR.XRController hand)
    {
        isControlling = true;
        publishTimer = 0f; // Resetear timer para publicar inmediatamente
        anchorPosition = hand.devicePosition.ReadValue();
        anchorRotation = hand.deviceRotation.ReadValue();

        OVRInput.SetControllerVibration(1f, 0.5f, OVRInput.Controller.RTouch);
        Invoke("StopVibration", 0.1f);

        if (activeVisual != null)
        {
            activeVisual.transform.position = anchorPosition;
            activeVisual.SetActive(true);
        }
    }

    private void PublishRelativeTransform(UnityEngine.InputSystem.XR.XRController hand)
    {
        if (posePublisher == null) return;

        UnityEngine.Vector3 deltaPos = hand.devicePosition.ReadValue() - anchorPosition;
        UnityEngine.Quaternion deltaRot = hand.deviceRotation.ReadValue() * UnityEngine.Quaternion.Inverse(anchorRotation);

        geometry_msgs.msg.PoseStamped msg = new geometry_msgs.msg.PoseStamped();
        msg.Header.Frame_id = frameId;
        msg.Header.Stamp = GetRosTimeManual();

        msg.Pose.Position.X = deltaPos.z;
        msg.Pose.Position.Y = -deltaPos.x;
        msg.Pose.Position.Z = deltaPos.y;

        msg.Pose.Orientation.X = deltaRot.x;
        msg.Pose.Orientation.Y = deltaRot.y;
        msg.Pose.Orientation.Z = deltaRot.z;
        msg.Pose.Orientation.W = deltaRot.w;

        posePublisher.Publish(msg);
    }

    private void StopPositionControl()
    {
        isControlling = false;
        if (activeVisual != null) activeVisual.SetActive(false);
        StopVibration();
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