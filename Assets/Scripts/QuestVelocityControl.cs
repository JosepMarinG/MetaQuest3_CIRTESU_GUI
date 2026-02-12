using UnityEngine;
using UnityEngine.InputSystem;
using ROS2;
using geometry_msgs.msg;
using builtin_interfaces.msg;

public class QuestVelocityControl : MonoBehaviour
{
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private IPublisher<geometry_msgs.msg.TwistStamped> twistPublisher;

    [Header("Estado de Control")]
    public bool isActivated = false;

    [Header("Configuración")]
    public string topicName = "/cmd_vel";
    public string frameId = "base_link";
    public float maxSpeed = 1.0f;
    public float deadZone = 0.02f;

    [Header("Referencias Visuales")]
    public GameObject originVisualPrefab;
    private GameObject activeVisual;
    private UnityEngine.Vector3 anchorPosition;
    private bool isControlling = false;

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
        // Bloqueo de seguridad
        if (!isActivated) return;

        if (ros2Node == null && ros2Unity.Ok())
        {
            ros2Node = ros2Unity.CreateNode("QuestVelocityNode");
            twistPublisher = ros2Node.CreatePublisher<geometry_msgs.msg.TwistStamped>(topicName);
        }

        var rightHand = UnityEngine.InputSystem.XR.XRController.rightHand;
        if (rightHand == null) return;

        var gripAction = rightHand["gripPressed"] as UnityEngine.InputSystem.Controls.ButtonControl;

        if (gripAction != null)
        {
            if (gripAction.wasPressedThisFrame) StartVelocityControl(rightHand);
            if (gripAction.isPressed && isControlling) PublishVelocity(rightHand);
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
            StopVibration();

            // SEGURIDAD: Enviar parada al desactivar el control
            SendStopMessage();
        }
    }

    private void StartVelocityControl(UnityEngine.InputSystem.XR.XRController hand)
    {
        isControlling = true;
        anchorPosition = hand.devicePosition.ReadValue();
        OVRInput.SetControllerVibration(1f, 0.5f, OVRInput.Controller.RTouch);
        Invoke("StopVibration", 0.1f);
        if (activeVisual != null) { activeVisual.transform.position = anchorPosition; activeVisual.SetActive(true); }
    }

    private void PublishVelocity(UnityEngine.InputSystem.XR.XRController hand)
    {
        if (twistPublisher == null) return;

        UnityEngine.Vector3 delta = hand.devicePosition.ReadValue() - anchorPosition;
        if (delta.magnitude < deadZone) delta = UnityEngine.Vector3.zero;

        geometry_msgs.msg.TwistStamped msg = new geometry_msgs.msg.TwistStamped();
        msg.Header.Frame_id = frameId;
        msg.Header.Stamp = GetRosTimeManual();

        msg.Twist.Linear.X = Mathf.Clamp(delta.z * 2f, -maxSpeed, maxSpeed);
        msg.Twist.Linear.Y = Mathf.Clamp(-delta.x * 2f, -maxSpeed, maxSpeed);
        msg.Twist.Linear.Z = Mathf.Clamp(delta.y * 2f, -maxSpeed, maxSpeed);

        twistPublisher.Publish(msg);
    }

    private void StopVelocityControl()
    {
        isControlling = false;
        if (activeVisual != null) activeVisual.SetActive(false);
        SendStopMessage();
        StopVibration();
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