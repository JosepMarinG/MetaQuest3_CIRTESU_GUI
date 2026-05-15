using System;
using System.Collections.Generic;
using ROS2;
using std_msgs.msg;
using UnityEngine;
using UnityEngine.XR;

public class CameraCongestionControl : MonoBehaviour
{
    [Serializable]
    public class CameraControlTarget
    {
        [Tooltip("Panel de Unity que representa esta camara en la GUI.")]
        public Transform panelTransform;

        [Tooltip("Topic ROS2 de control: Int32MultiArray [fps, quality].")]
        public string controlTopic = "/camera_1/control";

        [Tooltip("FPS pedidos cuando esta camara es la prioritaria.")]
        [Min(1)]
        public int activeFps = 10;

        [Tooltip("Calidad/resolucion pedida cuando esta camara es la prioritaria.")]
        [Range(1, 100)]
        public int activeQuality = 80;

        [NonSerialized] public IPublisher<Int32MultiArray> publisher;
        [NonSerialized] public int lastPublishedFps = int.MinValue;
        [NonSerialized] public int lastPublishedQuality = int.MinValue;
        [NonSerialized] public bool testScaleApplied;
    }

    [Serializable]
    public class PointCloudControlTarget
    {
        [Tooltip("Transform del mapa o anchor donde se visualiza este pointcloud.")]
        public Transform mapTransform;

        [Tooltip("Subscriber GPU asociado. Si esta apagado, se publica [0, 0].")]
        public PointCloudSubscriberGPU pointCloudSubscriber;

        [Tooltip("Topic ROS2 de control: Int32MultiArray [fps, quality].")]
        public string controlTopic = "/points/control";

        [Tooltip("FPS pedidos cuando este pointcloud es prioritario.")]
        [Min(1)]
        public int activeFps = 5;

        [Tooltip("Calidad/resolucion pedida cuando este pointcloud es prioritario.")]
        [Range(1, 100)]
        public int activeQuality = 80;

        [NonSerialized] public IPublisher<Int32MultiArray> publisher;
        [NonSerialized] public int lastPublishedFps = int.MinValue;
        [NonSerialized] public int lastPublishedQuality = int.MinValue;
    }

    [Header("ROS2")]
    [SerializeField] private string nodeName = "camera_congestion_control";

    [Header("Mirada / FOV")]
    [Tooltip("Si esta activo, intenta usar eye tracking por Unity XR. Si falla, usa la cabeza.")]
    [SerializeField] private bool useEyeTracking = false;

    [Tooltip("Origen XR para convertir datos locales de eye tracking a mundo. Normalmente el XR Origin/OVRCameraRig. Puede quedar vacio si coincide con mundo.")]
    [SerializeField] private Transform eyeTrackingReferenceSpace;

    [Tooltip("Transform que representa la mirada. Si esta vacio se usa la camara.")]
    [SerializeField] private Transform gazeOrigin;

    [Tooltip("Camara de la cabeza/CenterEyeAnchor. Si esta vacia se usa Camera.main.")]
    [SerializeField] private Camera headCamera;

    [SerializeField] private bool requireViewportVisibility = true;
    [SerializeField, Range(1f, 120f)] private float maxAngleFromGazeDegrees = 55f;
    [SerializeField] private float viewportMargin = 0.02f;
    [SerializeField] private float minUsefulDistance = 0.25f;
    [SerializeField] private float maxUsefulDistance = 8f;

    [Header("Control")]
    [Tooltip("Veces por segundo que se reenvia el estado de control.")]
    [SerializeField, Min(0.2f)] private float publishRateHz = 5f;

    [Tooltip("Si no hay ningun panel visible, todas las camaras se apagan.")]
    [SerializeField] private bool turnOffWhenNoPanelVisible = true;

    [SerializeField] private bool verboseDebugLogs = true;

    [Header("Test Visual")]
    [Tooltip("Si esta activo, el panel detectado como principal aumenta su escala para depurar la mirada.")]
    [SerializeField] private bool testScaleSelectedPanel = false;

    [SerializeField, Min(1f)] private float selectedPanelScaleMultiplier = 1.08f;

    [Header("Camaras")]
    public CameraControlTarget[] cameras =
    {
        new CameraControlTarget { controlTopic = "/camera_1/control" },
        new CameraControlTarget { controlTopic = "/camera_2/control" },
        new CameraControlTarget { controlTopic = "/camera_3/control" },
        new CameraControlTarget { controlTopic = "/camera_4/control" },
        new CameraControlTarget { controlTopic = "/camera_5/control" }
    };

    [Header("Pointclouds")]
    public PointCloudControlTarget[] pointClouds =
    {
        new PointCloudControlTarget { controlTopic = "/points/control" }
    };

    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private bool ros2Initialized;
    private float publishTimer;
    private int lastSelectedIndex = -2;
    private int lastSelectedPointCloudIndex = -2;
    private bool warnedMissingTargets;
    private bool warnedMissingPointCloudTargets;
    private bool lastTestScaleSelectedPanel;
    private readonly List<InputDevice> eyeTrackingDevices = new List<InputDevice>();

    private void Start()
    {
        ros2Unity = GetComponentInParent<ROS2UnityComponent>();
        if (ros2Unity == null)
        {
            ros2Unity = UnityEngine.Object.FindAnyObjectByType<ROS2UnityComponent>();
        }

        if (headCamera == null)
        {
            headCamera = Camera.main;
        }
    }

    private void Update()
    {
        int selectedIndex = FindBestVisibleCameraIndex();
        int selectedPointCloudIndex = FindBestVisiblePointCloudIndex();

        if (testScaleSelectedPanel || lastTestScaleSelectedPanel || lastSelectedIndex != selectedIndex)
        {
            UpdateTestPanelScale(selectedIndex);
            lastTestScaleSelectedPanel = testScaleSelectedPanel;
        }

        if (!ros2Initialized)
        {
            TryInitializeROS2();
            lastSelectedIndex = selectedIndex;
            lastSelectedPointCloudIndex = selectedPointCloudIndex;
            return;
        }

        publishTimer += Time.deltaTime;
        float publishInterval = 1f / Mathf.Max(0.2f, publishRateHz);

        if (publishTimer >= publishInterval || selectedIndex != lastSelectedIndex)
        {
            PublishControlState(selectedIndex);
            PublishPointCloudControlState(selectedPointCloudIndex);
            publishTimer = 0f;
            lastSelectedIndex = selectedIndex;
            lastSelectedPointCloudIndex = selectedPointCloudIndex;
        }
        else if (selectedPointCloudIndex != lastSelectedPointCloudIndex)
        {
            PublishPointCloudControlState(selectedPointCloudIndex);
            lastSelectedPointCloudIndex = selectedPointCloudIndex;
        }
    }

    private void TryInitializeROS2()
    {
        if (ros2Unity == null || !ros2Unity.Ok())
        {
            return;
        }

        ros2Node = ros2Unity.CreateNode(nodeName);
        if (ros2Node == null)
        {
            Debug.LogError("[CameraCongestionControl] Failed to create ROS2 node.");
            return;
        }

        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] == null || string.IsNullOrWhiteSpace(cameras[i].controlTopic))
            {
                continue;
            }

            cameras[i].publisher = ros2Node.CreatePublisher<Int32MultiArray>(cameras[i].controlTopic);
        }

        for (int i = 0; i < pointClouds.Length; i++)
        {
            if (pointClouds[i] == null || string.IsNullOrWhiteSpace(pointClouds[i].controlTopic))
            {
                continue;
            }

            pointClouds[i].publisher = ros2Node.CreatePublisher<Int32MultiArray>(pointClouds[i].controlTopic);
        }

        ros2Initialized = true;
        PublishControlState(FindBestVisibleCameraIndex(), force: true);
        PublishPointCloudControlState(FindBestVisiblePointCloudIndex(), force: true);
        UpdateTestPanelScale(FindBestVisibleCameraIndex());

        if (verboseDebugLogs)
        {
            Debug.Log($"[CameraCongestionControl] Initialized with {cameras.Length} camera control topics and {pointClouds.Length} pointcloud control topics.");
        }
    }

    private int FindBestVisibleCameraIndex()
    {
        int bestIndex = -1;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] != null && TryGetVisualScore(cameras[i].panelTransform, out float score) && score > bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        if (bestIndex < 0 && !warnedMissingTargets)
        {
            warnedMissingTargets = true;
            Debug.LogWarning("[CameraCongestionControl] No visible camera panel found. Assign panel transforms in the cameras list.");
        }

        if (bestIndex >= 0)
        {
            warnedMissingTargets = false;
        }

        return bestIndex;
    }

    private int FindBestVisiblePointCloudIndex()
    {
        int bestIndex = -1;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < pointClouds.Length; i++)
        {
            PointCloudControlTarget target = pointClouds[i];
            if (target == null || !IsPointCloudVisualizationActive(target))
            {
                continue;
            }

            if (TryGetVisualScore(target.mapTransform, out float score) && score > bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        if (bestIndex < 0 && pointClouds.Length > 0 && !warnedMissingPointCloudTargets)
        {
            warnedMissingPointCloudTargets = true;
            Debug.LogWarning("[CameraCongestionControl] No visible active pointcloud target found. Assign map transforms and enabled PointCloudSubscriberGPU references.");
        }

        if (bestIndex >= 0)
        {
            warnedMissingPointCloudTargets = false;
        }

        return bestIndex;
    }

    private bool TryGetVisualScore(Transform targetTransform, out float score)
    {
        score = 0f;

        if (targetTransform == null)
        {
            return false;
        }

        if (!TryGetGazeRay(out Vector3 gazePosition, out Vector3 gazeForward))
        {
            return false;
        }

        Vector3 toPanel = targetTransform.position - gazePosition;
        if (toPanel.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        float angle = Vector3.Angle(gazeForward, toPanel);
        if (angle > maxAngleFromGazeDegrees)
        {
            return false;
        }

        if (requireViewportVisibility && headCamera != null)
        {
            Vector3 viewportPoint = headCamera.WorldToViewportPoint(targetTransform.position);
            bool isInViewport =
                viewportPoint.z > 0f &&
                viewportPoint.x >= -viewportMargin &&
                viewportPoint.x <= 1f + viewportMargin &&
                viewportPoint.y >= -viewportMargin &&
                viewportPoint.y <= 1f + viewportMargin;

            if (!isInViewport)
            {
                return false;
            }
        }

        float angleScore = 1f - Mathf.Clamp01(angle / Mathf.Max(1f, maxAngleFromGazeDegrees));
        float distance = toPanel.magnitude;
        float distanceScore = Mathf.InverseLerp(maxUsefulDistance, minUsefulDistance, distance);
        score = angleScore * 0.85f + distanceScore * 0.15f;
        return true;
    }

    private bool TryGetGazeRay(out Vector3 position, out Vector3 forward)
    {
        if (useEyeTracking && TryGetEyeTrackingRay(out position, out forward))
        {
            return true;
        }

        Transform origin = GetHeadGazeOrigin();
        if (origin == null)
        {
            position = Vector3.zero;
            forward = Vector3.forward;
            return false;
        }

        position = origin.position;
        forward = origin.forward;
        return true;
    }

    private bool TryGetEyeTrackingRay(out Vector3 position, out Vector3 forward)
    {
        Transform headOrigin = GetHeadGazeOrigin();
        position = headOrigin != null ? headOrigin.position : Vector3.zero;
        forward = Vector3.forward;

        eyeTrackingDevices.Clear();
        InputDevices.GetDevicesAtXRNode(XRNode.CenterEye, eyeTrackingDevices);
        if (eyeTrackingDevices.Count == 0)
        {
            InputDevices.GetDevicesAtXRNode(XRNode.Head, eyeTrackingDevices);
        }
        if (eyeTrackingDevices.Count == 0)
        {
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.EyeTracking, eyeTrackingDevices);
        }

        for (int i = 0; i < eyeTrackingDevices.Count; i++)
        {
            InputDevice device = eyeTrackingDevices[i];
            if (!device.isValid || !device.TryGetFeatureValue(CommonUsages.eyesData, out Eyes eyes))
            {
                continue;
            }

            if (eyes.TryGetFixationPoint(out Vector3 fixationPoint))
            {
                Vector3 worldFixationPoint = ToWorldPointFromEyeTracking(fixationPoint);
                Vector3 direction = worldFixationPoint - position;
                if (direction.sqrMagnitude > 0.0001f)
                {
                    forward = direction.normalized;
                    return true;
                }
            }

            if (TryGetEyeForwardFromRotations(eyes, out forward))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetEyeForwardFromRotations(Eyes eyes, out Vector3 forward)
    {
        bool hasLeftRotation = eyes.TryGetLeftEyeRotation(out Quaternion leftRotation);
        bool hasRightRotation = eyes.TryGetRightEyeRotation(out Quaternion rightRotation);

        if (hasLeftRotation && hasRightRotation)
        {
            Quaternion averageRotation = Quaternion.Slerp(leftRotation, rightRotation, 0.5f);
            forward = ToWorldDirectionFromEyeTracking(averageRotation * Vector3.forward);
            return forward.sqrMagnitude > 0.0001f;
        }

        if (hasLeftRotation)
        {
            forward = ToWorldDirectionFromEyeTracking(leftRotation * Vector3.forward);
            return forward.sqrMagnitude > 0.0001f;
        }

        if (hasRightRotation)
        {
            forward = ToWorldDirectionFromEyeTracking(rightRotation * Vector3.forward);
            return forward.sqrMagnitude > 0.0001f;
        }

        forward = Vector3.forward;
        return false;
    }

    private Vector3 ToWorldPointFromEyeTracking(Vector3 point)
    {
        return eyeTrackingReferenceSpace != null
            ? eyeTrackingReferenceSpace.TransformPoint(point)
            : point;
    }

    private Vector3 ToWorldDirectionFromEyeTracking(Vector3 direction)
    {
        return eyeTrackingReferenceSpace != null
            ? eyeTrackingReferenceSpace.TransformDirection(direction).normalized
            : direction.normalized;
    }

    private Transform GetHeadGazeOrigin()
    {
        if (gazeOrigin != null)
        {
            return gazeOrigin;
        }

        if (headCamera != null)
        {
            return headCamera.transform;
        }

        if (Camera.main != null)
        {
            return Camera.main.transform;
        }

        return null;
    }

    private void PublishControlState(int selectedIndex, bool force = false)
    {
        if (!turnOffWhenNoPanelVisible && selectedIndex < 0 && cameras.Length > 0)
        {
            selectedIndex = 0;
        }

        for (int i = 0; i < cameras.Length; i++)
        {
            CameraControlTarget target = cameras[i];
            if (target == null || target.publisher == null)
            {
                continue;
            }

            int fps = i == selectedIndex ? Mathf.Max(1, target.activeFps) : 0;
            int quality = i == selectedIndex ? Mathf.Clamp(target.activeQuality, 1, 100) : 0;

            if (!force && fps == target.lastPublishedFps && quality == target.lastPublishedQuality)
            {
                continue;
            }

            Int32MultiArray msg = new Int32MultiArray();
            msg.Data = new[] { fps, quality };
            target.publisher.Publish(msg);

            target.lastPublishedFps = fps;
            target.lastPublishedQuality = quality;
        }

        if (verboseDebugLogs)
        {
            string selected = selectedIndex >= 0 ? (selectedIndex + 1).ToString() : "none";
            Debug.Log($"[CameraCongestionControl] Active camera: {selected}");
        }
    }

    private void PublishPointCloudControlState(int selectedIndex, bool force = false)
    {
        for (int i = 0; i < pointClouds.Length; i++)
        {
            PointCloudControlTarget target = pointClouds[i];
            if (target == null || target.publisher == null)
            {
                continue;
            }

            bool shouldPublishPointCloud = i == selectedIndex && IsPointCloudVisualizationActive(target);
            int fps = shouldPublishPointCloud ? Mathf.Max(1, target.activeFps) : 0;
            int quality = shouldPublishPointCloud ? Mathf.Clamp(target.activeQuality, 1, 100) : 0;

            if (!force && fps == target.lastPublishedFps && quality == target.lastPublishedQuality)
            {
                continue;
            }

            Int32MultiArray msg = new Int32MultiArray();
            msg.Data = new[] { fps, quality };
            target.publisher.Publish(msg);

            target.lastPublishedFps = fps;
            target.lastPublishedQuality = quality;
        }

        if (verboseDebugLogs)
        {
            string selected = selectedIndex >= 0 ? (selectedIndex + 1).ToString() : "none";
            Debug.Log($"[CameraCongestionControl] Active pointcloud: {selected}");
        }
    }

    private bool IsPointCloudVisualizationActive(PointCloudControlTarget target)
    {
        return target.pointCloudSubscriber != null && target.pointCloudSubscriber.IsVisualizationActive;
    }

    private void UpdateTestPanelScale(int selectedIndex)
    {
        float multiplier = Mathf.Max(1f, selectedPanelScaleMultiplier);

        for (int i = 0; i < cameras.Length; i++)
        {
            CameraControlTarget target = cameras[i];
            if (target?.panelTransform == null)
            {
                continue;
            }

            if (target.testScaleApplied)
            {
                target.panelTransform.localScale /= multiplier;
                target.testScaleApplied = false;
            }

            if (testScaleSelectedPanel && i == selectedIndex)
            {
                target.panelTransform.localScale *= multiplier;
                target.testScaleApplied = true;
            }
        }
    }

    private void OnDestroy()
    {
        UpdateTestPanelScale(-1);

        if (ros2Node == null)
        {
            return;
        }

        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i]?.publisher != null)
            {
                ros2Node.RemovePublisher<Int32MultiArray>(cameras[i].publisher);
                cameras[i].publisher = null;
            }
        }

        for (int i = 0; i < pointClouds.Length; i++)
        {
            if (pointClouds[i]?.publisher != null)
            {
                ros2Node.RemovePublisher<Int32MultiArray>(pointClouds[i].publisher);
                pointClouds[i].publisher = null;
            }
        }

        if (ros2Unity != null)
        {
            ros2Unity.RemoveNode(ros2Node);
        }

        ros2Node = null;
    }
}
