using ROS2;
using tf2_msgs.msg;
using UnityEngine;

public class TF_TestPublisher : MonoBehaviour
{
    [Header("ROS2")]
    [SerializeField] private string nodeName = "tf_test_publisher";
    [SerializeField] private string topicName = "/tf";
    [SerializeField] private float publishRateHz = 1f;

    [Header("Transform Data")]
    [SerializeField] private string parentFrame = "world_net";
    [SerializeField] private string childFrame = "girona500/bravo/gripper/camera";
    [SerializeField] private Vector3 translation = Vector3.zero;
    [SerializeField] private Vector3 eulerDeg = Vector3.zero;

    [Header("Debug")]
    [SerializeField] private bool verboseLogs = true;

    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private IPublisher<TFMessage> tfPublisher;
    private float publishTimer;
    private int publishCount;

    private void Start()
    {
        ros2Unity = Object.FindAnyObjectByType<ROS2UnityComponent>();
        if (ros2Unity == null)
        {
            Debug.LogError("[TF_TestPublisher] No se encontro ROS2UnityComponent en la escena.");
            return;
        }

        if (string.IsNullOrWhiteSpace(topicName))
        {
            topicName = "/tf";
        }

        if (!topicName.StartsWith("/"))
        {
            topicName = "/" + topicName.Trim();
        }
    }

    private void Update()
    {
        if (ros2Unity == null)
        {
            return;
        }

        if (ros2Node == null)
        {
            TryInitializePublisher();
            return;
        }

        if (tfPublisher == null)
        {
            return;
        }

        publishTimer += Time.deltaTime;
        float period = publishRateHz > 0f ? 1f / publishRateHz : 0f;

        if (publishRateHz <= 0f || publishTimer >= period)
        {
            PublishTransform();
            publishTimer = 0f;
        }
    }

    private void TryInitializePublisher()
    {
        if (!ros2Unity.Ok())
        {
            return;
        }

        string resolvedNode = BuildNodeName(nodeName, gameObject.name, GetInstanceID());
        ros2Node = ros2Unity.CreateNode(resolvedNode);

        if (ros2Node == null)
        {
            Debug.LogError($"[TF_TestPublisher] No se pudo crear nodo '{resolvedNode}'.");
            return;
        }

        tfPublisher = ros2Node.CreatePublisher<TFMessage>(topicName);
        if (tfPublisher == null)
        {
            Debug.LogError($"[TF_TestPublisher] No se pudo crear publisher en '{topicName}'.");
            return;
        }

        Debug.Log($"[TF_TestPublisher] Publisher listo en '{topicName}' con nodo '{resolvedNode}'.");
    }

    private void PublishTransform()
    {
        TFMessage msg = new TFMessage();

        geometry_msgs.msg.TransformStamped ts = new geometry_msgs.msg.TransformStamped();
        ts.Header.Frame_id = NormalizeFrame(parentFrame);
        ts.Child_frame_id = NormalizeFrame(childFrame);
        ts.Header.Stamp = GetRosTimeManual();

        ts.Transform.Translation.X = translation.x;
        ts.Transform.Translation.Y = translation.y;
        ts.Transform.Translation.Z = translation.z;

        Quaternion q = Quaternion.Euler(eulerDeg);
        ts.Transform.Rotation.X = q.x;
        ts.Transform.Rotation.Y = q.y;
        ts.Transform.Rotation.Z = q.z;
        ts.Transform.Rotation.W = q.w;

        msg.Transforms = new geometry_msgs.msg.TransformStamped[1];
        msg.Transforms[0] = ts;

        tfPublisher.Publish(msg);
        publishCount++;

        if (verboseLogs)
        {
            Debug.Log($"[TF_TestPublisher] Publicado #{publishCount}: '{ts.Header.Frame_id}' -> '{ts.Child_frame_id}' en '{topicName}'.");
        }
    }

    private static string NormalizeFrame(string frame)
    {
        if (string.IsNullOrWhiteSpace(frame))
        {
            return string.Empty;
        }

        return frame.Trim().TrimStart('/');
    }

    private static string BuildNodeName(string baseName, string goName, int instanceId)
    {
        string bn = string.IsNullOrWhiteSpace(baseName) ? "tf_test_publisher" : baseName;
        string raw = $"{bn}_{goName}_{Mathf.Abs(instanceId)}".ToLowerInvariant();
        char[] chars = raw.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                chars[i] = '_';
            }
        }

        string sanitized = new string(chars).Trim('_');
        while (sanitized.Contains("__"))
        {
            sanitized = sanitized.Replace("__", "_");
        }

        if (string.IsNullOrEmpty(sanitized))
        {
            sanitized = "tf_test_publisher";
        }

        if (char.IsDigit(sanitized[0]))
        {
            sanitized = "n_" + sanitized;
        }

        return sanitized;
    }

    private static builtin_interfaces.msg.Time GetRosTimeManual()
    {
        builtin_interfaces.msg.Time time = new builtin_interfaces.msg.Time();
        float unityTime = Time.realtimeSinceStartup;
        time.Sec = (int)unityTime;
        time.Nanosec = (uint)((unityTime - time.Sec) * 1e9f);
        return time;
    }

    private void OnDestroy()
    {
        if (ros2Node != null && ros2Unity != null)
        {
            try
            {
                ros2Unity.RemoveNode(ros2Node);
            }
            catch
            {
            }
        }
    }
}
