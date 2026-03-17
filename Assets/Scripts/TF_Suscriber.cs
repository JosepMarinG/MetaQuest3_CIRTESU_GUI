using System;
using System.Collections.Generic;
using ROS2;
using tf2_msgs.msg;
using UnityEngine;

public class TF_Suscriber : MonoBehaviour
{
    [Serializable]
    public struct TFData
    {
        public string ParentFrame;
        public string ChildFrame;
        public Vector3 Translation;
        public Quaternion Rotation;
        public double StampSeconds;
    }

    public static TF_Suscriber Instance { get; private set; }

    [Header("ROS2")]
    [SerializeField] private string tfTopic = "/tf";
    [SerializeField] private string tfStaticTopic = "/tf_static";
    [SerializeField] private string nodeName = "tf_subscriber";

    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private ISubscription<TFMessage> tfSubscription;
    private ISubscription<TFMessage> tfStaticSubscription;
    private bool warnedMissingRos2Unity;
    private bool warnedRos2NotReady;

    private readonly object tfLock = new object();
    private readonly Dictionary<string, TFData> transformsByLink = new Dictionary<string, TFData>();
    private readonly Dictionary<string, TFData> latestTransformByChild = new Dictionary<string, TFData>();

    private int totalTfMessages;
    private int totalTransformUpdates;

    public bool IsReady => ros2Node != null && tfSubscription != null;
    public int UniqueTransformCount
    {
        get
        {
            lock (tfLock)
            {
                return transformsByLink.Count;
            }
        }
    }

    public int TotalTfMessages
    {
        get
        {
            lock (tfLock)
            {
                return totalTfMessages;
            }
        }
    }

    public int TotalTransformUpdates
    {
        get
        {
            lock (tfLock)
            {
                return totalTransformUpdates;
            }
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        ros2Unity = UnityEngine.Object.FindAnyObjectByType<ROS2UnityComponent>();
    }

    private void Update()
    {
        if (ros2Node == null)
        {
            TryInitializeSubscription();
        }
    }

    private void TryInitializeSubscription()
    {
        if (ros2Unity == null)
        {
            ros2Unity = UnityEngine.Object.FindAnyObjectByType<ROS2UnityComponent>();
            if (ros2Unity == null)
            {
                if (!warnedMissingRos2Unity)
                {
                    Debug.LogWarning("[TF_Suscriber] No se encontro ROS2UnityComponent en la escena.");
                    warnedMissingRos2Unity = true;
                }
                return;
            }

            warnedMissingRos2Unity = false;
        }

        if (!ros2Unity.Ok())
        {
            if (!warnedRos2NotReady)
            {
                Debug.LogWarning("[TF_Suscriber] ROS2UnityComponent existe, pero ROS2 aun no esta listo (Ok() == false).");
                warnedRos2NotReady = true;
            }
            return;
        }

        warnedRos2NotReady = false;

        string resolvedNodeName = $"{nodeName}_{gameObject.name.Replace(" ", "_")}_{Mathf.Abs(GetInstanceID())}";
        ros2Node = ros2Unity.CreateNode(resolvedNodeName);

        if (ros2Node == null)
        {
            Debug.LogError($"[TF_Suscriber] No se pudo crear el nodo ROS2 '{resolvedNodeName}'.");
            return;
        }

        QualityOfServiceProfile tfQos = new QualityOfServiceProfile(QosPresetProfile.SENSOR_DATA);
        tfSubscription = ros2Node.CreateSubscription<TFMessage>(tfTopic, OnTfMessageReceived, tfQos);

        if (!string.IsNullOrWhiteSpace(tfStaticTopic))
        {
            QualityOfServiceProfile tfStaticQos = new QualityOfServiceProfile(QosPresetProfile.DEFAULT);
            tfStaticSubscription = ros2Node.CreateSubscription<TFMessage>(tfStaticTopic, OnTfMessageReceived, tfStaticQos);
        }

        Debug.Log($"[TF_Suscriber] Suscrito a {tfTopic} y {tfStaticTopic} con nodo {resolvedNodeName}");
    }

    private void OnTfMessageReceived(TFMessage message)
    {
        if (message == null || message.Transforms == null)
        {
            return;
        }

        lock (tfLock)
        {
            totalTfMessages++;

            for (int i = 0; i < message.Transforms.Length; i++)
            {
                geometry_msgs.msg.TransformStamped transformStamped = message.Transforms[i];
                if (transformStamped == null)
                {
                    continue;
                }

                string parentFrame = NormalizeFrameId(transformStamped.Header != null ? transformStamped.Header.Frame_id : string.Empty);
                string childFrame = NormalizeFrameId(transformStamped.Child_frame_id);
                if (string.IsNullOrEmpty(childFrame))
                {
                    continue;
                }

                geometry_msgs.msg.Vector3 translation = transformStamped.Transform.Translation;
                geometry_msgs.msg.Quaternion rotation = transformStamped.Transform.Rotation;

                TFData tfData = new TFData
                {
                    ParentFrame = parentFrame,
                    ChildFrame = childFrame,
                    Translation = new Vector3((float)translation.X, (float)translation.Y, (float)translation.Z),
                    Rotation = new Quaternion((float)rotation.X, (float)rotation.Y, (float)rotation.Z, (float)rotation.W),
                    StampSeconds = ToSeconds(transformStamped.Header != null ? transformStamped.Header.Stamp : null)
                };

                transformsByLink[BuildLinkKey(parentFrame, childFrame)] = tfData;
                latestTransformByChild[childFrame] = tfData;
                totalTransformUpdates++;
            }
        }
    }

    public bool TryGetTransform(string parentFrame, string childFrame, out TFData tfData)
    {
        string linkKey = BuildLinkKey(NormalizeFrameId(parentFrame), NormalizeFrameId(childFrame));
        lock (tfLock)
        {
            return transformsByLink.TryGetValue(linkKey, out tfData);
        }
    }

    public bool TryGetLatestTransform(string childFrame, out TFData tfData)
    {
        string normalizedChild = NormalizeFrameId(childFrame);
        lock (tfLock)
        {
            return latestTransformByChild.TryGetValue(normalizedChild, out tfData);
        }
    }

    public List<TFData> GetAllTransformsSnapshot()
    {
        lock (tfLock)
        {
            return new List<TFData>(transformsByLink.Values);
        }
    }

    private static string NormalizeFrameId(string frameId)
    {
        if (string.IsNullOrWhiteSpace(frameId))
        {
            return string.Empty;
        }

        return frameId.Trim().TrimStart('/');
    }

    private static string BuildLinkKey(string parentFrame, string childFrame)
    {
        return parentFrame + "->" + childFrame;
    }

    private static double ToSeconds(builtin_interfaces.msg.Time timeMessage)
    {
        if (timeMessage == null)
        {
            return 0d;
        }

        return timeMessage.Sec + timeMessage.Nanosec * 1e-9;
    }

    private void OnDestroy()
    {
        if (tfSubscription != null && ros2Node != null)
        {
            ros2Node.RemoveSubscription<TFMessage>(tfSubscription);
            tfSubscription = null;
        }

        if (tfStaticSubscription != null && ros2Node != null)
        {
            ros2Node.RemoveSubscription<TFMessage>(tfStaticSubscription);
            tfStaticSubscription = null;
        }

        if (ros2Node != null && ros2Unity != null)
        {
            ros2Unity.RemoveNode(ros2Node);
            ros2Node = null;
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }
}
