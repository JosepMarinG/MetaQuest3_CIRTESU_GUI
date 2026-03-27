using System;
using System.Collections.Generic;
using System.Text;
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
    private bool warnedInitException;

    [Header("Debug")]
    [SerializeField] private bool verboseDebugLogs = true;
    [SerializeField] private int messageSummaryEveryN = 50;

    [Header("Debug Query Transform")]
    [SerializeField] private bool debugSpecificTransformQuery = false;
    [SerializeField] private string debugQuerySourceFrame = "world_ned";
    [SerializeField] private string debugQueryTargetFrame = "/tp_controller/tasks/bravo_ee_configuration_feedforward/target";
    [SerializeField] private float debugQueryIntervalSeconds = 1f;
    [SerializeField] private bool debugQueryShowResolvedPath = true;

    private readonly object tfLock = new object();
    private readonly Dictionary<string, TFData> transformsByLink = new Dictionary<string, TFData>();
    private readonly Dictionary<string, TFData> latestTransformByChild = new Dictionary<string, TFData>();

    private struct GraphEdge
    {
        public string TargetFrame;
        public Vector3 Translation;
        public Quaternion Rotation;
        public double StampSeconds;
    }

    private struct GraphState
    {
        public string Frame;
        public Vector3 Translation;
        public Quaternion Rotation;
        public double StampSeconds;
    }

    private int totalTfMessages;
    private int totalTransformUpdates;
    private float nextDebugQueryTime;
    private int debugQueryCounter;

        private int updateCounter = 0;

    private void LogInfo(string message)
    {
        if (verboseDebugLogs)
        {
            Debug.Log($"[TF_Subscriber] {message}");
        }
    }

    private void LogWarn(string message)
    {
        Debug.LogWarning($"[TF_Subscriber] {message}");
    }

    private void LogError(string message)
    {
        Debug.LogError($"[TF_Subscriber] {message}");
    }

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
            LogWarn($"Instancia duplicada detectada en '{gameObject.name}'. Se destruye el componente duplicado.");
            Destroy(this);
            return;
        }

        Instance = this;
        LogInfo($"Instancia activa asignada en '{gameObject.name}'.");
    }

    private void Start()
    {
        ros2Unity = UnityEngine.Object.FindAnyObjectByType<ROS2UnityComponent>();
        if (ros2Unity != null)
        {
            LogInfo("ROS2UnityComponent encontrado en Start().");
        }
        else
        {
            LogWarn("ROS2UnityComponent no encontrado en Start(). Se reintentara en Update().");
        }
    }

    private void Update()
    {
        if (ros2Node == null)
        {
            TryInitializeSubscription();
        }
        }

        private void LateUpdate()
        {
            updateCounter++;
            if (updateCounter % 300 == 0)
            {
                if (tfSubscription != null && totalTfMessages == 0)
                {
                    LogWarn($"Suscripcion activa desde hace ~{updateCounter / 60}s, pero CERO mensajes recibidos. IsReady={IsReady}, tfTopic={tfTopic}, tfStaticTopic={tfStaticTopic}.");
                }
            }

            DebugSpecificTransformQuery();
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
                    LogWarn("No se encontro ROS2UnityComponent en la escena.");
                    warnedMissingRos2Unity = true;
                }
                return;
            }

            if (verboseDebugLogs)
            {
                LogInfo("ROS2UnityComponent encontrado en TryInitializeSubscription().");
            }
            warnedMissingRos2Unity = false;
        }

        if (!ros2Unity.Ok())
        {
            if (!warnedRos2NotReady)
            {
                LogWarn("ROS2UnityComponent existe, pero ROS2 aun no esta listo (Ok() == false).");
                warnedRos2NotReady = true;
            }
            return;
        }

        if (verboseDebugLogs && warnedRos2NotReady)
        {
            LogInfo("ROS2 ahora esta listo (Ok() == true).");
        }
        warnedRos2NotReady = false;

        string normalizedTfTopic = NormalizeTopicName(tfTopic);
        if (string.IsNullOrEmpty(normalizedTfTopic))
        {
            normalizedTfTopic = "/tf";
            LogWarn("El topic TF principal estaba vacio. Se usara '/tf'.");
        }

        string normalizedTfStaticTopic = NormalizeTopicName(tfStaticTopic);
        string gameObjectName = gameObject != null ? gameObject.name : "GameObject";
        string resolvedNodeName = BuildResolvedNodeName(nodeName, gameObjectName, GetInstanceID());

        LogInfo($"Intentando inicializar suscripciones: tfTopic='{normalizedTfTopic}', tfStaticTopic='{normalizedTfStaticTopic}', nodo='{resolvedNodeName}'.");

        try
        {
            ros2Node = ros2Unity.CreateNode(resolvedNodeName);

            if (ros2Node == null)
            {
                LogError($"No se pudo crear el nodo ROS2 '{resolvedNodeName}'.");
                return;
            }

            LogInfo($"Nodo ROS2 creado: '{resolvedNodeName}'.");

            tfSubscription = ros2Node.CreateSubscription<TFMessage>(normalizedTfTopic, OnTfMessageReceived);

            if (tfSubscription != null)
            {
                LogInfo($"Suscripción creada para topic {normalizedTfTopic}.");
                    LogInfo($"Esperando mensajes en {normalizedTfTopic}...");
                LogInfo("QoS usado para /tf: default de ROS2ForUnity (sin perfil explicito).");
            }
            else
            {
                LogError($"CreateSubscription devolvio null para topic {normalizedTfTopic}.");
            }

            if (!string.IsNullOrWhiteSpace(normalizedTfStaticTopic))
            {
                tfStaticSubscription = ros2Node.CreateSubscription<TFMessage>(normalizedTfStaticTopic, OnTfMessageReceived);

                if (tfStaticSubscription != null)
                {
                    LogInfo($"Suscripción creada para topic {normalizedTfStaticTopic}.");
                    LogInfo("QoS usado para /tf_static: default de ROS2ForUnity (sin perfil explicito).");
                }
                else
                {
                    LogError($"CreateSubscription devolvio null para topic {normalizedTfStaticTopic}.");
                }
            }
            else
            {
                LogWarn("Topic TF_static esta vacio o null. No se creara suscripcion para TF estaticos.");
            }

            warnedInitException = false;
            LogInfo($"Suscrito a {normalizedTfTopic} y {normalizedTfStaticTopic} con nodo {resolvedNodeName}.");
        }
        catch (Exception ex)
        {
            CleanupRos2Resources();

            if (!warnedInitException)
            {
                LogError($"Fallo al inicializar suscripciones TF. Nodo='{resolvedNodeName}', tfTopic='{normalizedTfTopic}', tfStaticTopic='{normalizedTfStaticTopic}'. Excepcion: {ex.GetType().Name}: {ex.Message}");
                warnedInitException = true;
            }
        }
    }

    private void OnTfMessageReceived(TFMessage message)
    {
            LogInfo($"[CALLBACK] OnTfMessageReceived invocado. Message es {(message == null ? "NULL" : "valido")}, Transforms es {(message?.Transforms == null ? "NULL" : "valido")}.");
        
        if (message == null || message.Transforms == null)
        {
            LogWarn("Mensaje TF recibido pero es null o Transforms es null.");
            return;
        }

        lock (tfLock)
        {
            totalTfMessages++;

            if (totalTfMessages == 1)
            {
                LogInfo($"===== PRIMER MENSAJE TF RECIBIDO ===== Con {message.Transforms.Length} transforms.");
            }

            for (int i = 0; i < message.Transforms.Length; i++)
            {
                geometry_msgs.msg.TransformStamped transformStamped = message.Transforms[i];
                if (transformStamped == null || transformStamped.Transform == null)
                {
                    LogWarn($"Transform {i} es null o su Transform es null.");
                    continue;
                }

                string parentFrame = NormalizeFrameId(transformStamped.Header != null ? transformStamped.Header.Frame_id : string.Empty);
                string childFrame = NormalizeFrameId(transformStamped.Child_frame_id);
                if (string.IsNullOrEmpty(childFrame))
                {
                    LogWarn($"Transform {i} tiene childFrame vacio.");
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

                string linkKey = BuildLinkKey(parentFrame, childFrame);
                transformsByLink[linkKey] = tfData;
                latestTransformByChild[childFrame] = tfData;
                totalTransformUpdates++;

                LogInfo($"Transform agregado: '{parentFrame}' -> '{childFrame}'.");
            }

            int summaryEvery = Mathf.Max(1, messageSummaryEveryN);
            if (verboseDebugLogs && totalTfMessages % summaryEvery == 0)
            {
                LogInfo($"Resumen TF: mensajes={totalTfMessages}, updates={totalTransformUpdates}, links={transformsByLink.Count}.");
            }
        }
    }

    public bool TryGetTransform(string parentFrame, string childFrame, out TFData tfData)
    {
        string normalizedParent = NormalizeFrameId(parentFrame);
        string normalizedChild = NormalizeFrameId(childFrame);

        if (string.IsNullOrEmpty(normalizedParent) || string.IsNullOrEmpty(normalizedChild))
        {
            tfData = default;
            return false;
        }

        if (normalizedParent == normalizedChild)
        {
            tfData = new TFData
            {
                ParentFrame = normalizedParent,
                ChildFrame = normalizedChild,
                Translation = Vector3.zero,
                Rotation = Quaternion.identity,
                StampSeconds = 0d
            };
            return true;
        }

        lock (tfLock)
        {
            string linkKey = BuildLinkKey(normalizedParent, normalizedChild);
            if (transformsByLink.TryGetValue(linkKey, out tfData))
            {
                return true;
            }

            if (TryResolveTransformThroughGraph(normalizedParent, normalizedChild, out tfData))
            {
                return true;
            }

            if (verboseDebugLogs)
            {
                string availableLinks = transformsByLink.Count > 0 ? string.Join(", ", transformsByLink.Keys) : "NINGUNO";
                LogInfo($"TryGetTransform miss: '{normalizedParent}' -> '{normalizedChild}'. Links disponibles: {availableLinks} (total={transformsByLink.Count}).");
            }

            return false;
        }
    }

    private bool TryResolveTransformThroughGraph(string sourceFrame, string targetFrame, out TFData tfData)
    {
        Dictionary<string, List<GraphEdge>> graph = BuildTransformGraph();
        if (!graph.ContainsKey(sourceFrame))
        {
            tfData = default;
            return false;
        }

        Queue<GraphState> queue = new Queue<GraphState>();
        HashSet<string> visited = new HashSet<string>();

        queue.Enqueue(new GraphState
        {
            Frame = sourceFrame,
            Translation = Vector3.zero,
            Rotation = Quaternion.identity,
            StampSeconds = 0d
        });
        visited.Add(sourceFrame);

        while (queue.Count > 0)
        {
            GraphState current = queue.Dequeue();
            if (current.Frame == targetFrame)
            {
                tfData = new TFData
                {
                    ParentFrame = sourceFrame,
                    ChildFrame = targetFrame,
                    Translation = current.Translation,
                    Rotation = Quaternion.Normalize(current.Rotation),
                    StampSeconds = current.StampSeconds
                };
                return true;
            }

            if (!graph.TryGetValue(current.Frame, out List<GraphEdge> neighbors))
            {
                continue;
            }

            for (int i = 0; i < neighbors.Count; i++)
            {
                GraphEdge edge = neighbors[i];
                if (visited.Contains(edge.TargetFrame))
                {
                    continue;
                }

                ComposeTransform(
                    current.Translation,
                    current.Rotation,
                    edge.Translation,
                    edge.Rotation,
                    out Vector3 composedTranslation,
                    out Quaternion composedRotation);

                queue.Enqueue(new GraphState
                {
                    Frame = edge.TargetFrame,
                    Translation = composedTranslation,
                    Rotation = Quaternion.Normalize(composedRotation),
                    StampSeconds = Math.Max(current.StampSeconds, edge.StampSeconds)
                });
                visited.Add(edge.TargetFrame);
            }
        }

        tfData = default;
        return false;
    }

    private Dictionary<string, List<GraphEdge>> BuildTransformGraph()
    {
        Dictionary<string, List<GraphEdge>> graph = new Dictionary<string, List<GraphEdge>>();

        foreach (TFData tf in transformsByLink.Values)
        {
            if (string.IsNullOrEmpty(tf.ParentFrame) || string.IsNullOrEmpty(tf.ChildFrame))
            {
                continue;
            }

            AddGraphEdge(graph, tf.ParentFrame, new GraphEdge
            {
                TargetFrame = tf.ChildFrame,
                Translation = tf.Translation,
                Rotation = Quaternion.Normalize(tf.Rotation),
                StampSeconds = tf.StampSeconds
            });

            InvertTransform(tf.Translation, tf.Rotation, out Vector3 invTranslation, out Quaternion invRotation);
            AddGraphEdge(graph, tf.ChildFrame, new GraphEdge
            {
                TargetFrame = tf.ParentFrame,
                Translation = invTranslation,
                Rotation = Quaternion.Normalize(invRotation),
                StampSeconds = tf.StampSeconds
            });
        }

        return graph;
    }

    private static void AddGraphEdge(Dictionary<string, List<GraphEdge>> graph, string sourceFrame, GraphEdge edge)
    {
        if (!graph.TryGetValue(sourceFrame, out List<GraphEdge> edges))
        {
            edges = new List<GraphEdge>();
            graph[sourceFrame] = edges;
        }

        edges.Add(edge);
    }

    private static void InvertTransform(Vector3 translation, Quaternion rotation, out Vector3 inverseTranslation, out Quaternion inverseRotation)
    {
        // Guarda contra quaternion degenerado
        Quaternion normalized = rotation == default ? Quaternion.identity : Quaternion.Normalize(rotation);
        inverseRotation = Quaternion.Inverse(normalized);
        inverseTranslation = -(inverseRotation * translation);
    }

    private static void ComposeTransform(
        Vector3 parentToCurrentTranslation,
        Quaternion parentToCurrentRotation,
        Vector3 currentToNextTranslation,
        Quaternion currentToNextRotation,
        out Vector3 parentToNextTranslation,
        out Quaternion parentToNextRotation)
    {
        Quaternion normalizedCurrentToNext = Quaternion.Normalize(currentToNextRotation);
        Quaternion normalizedParentToCurrent = Quaternion.Normalize(parentToCurrentRotation);

        // Compose as T_parent_next = T_parent_current * T_current_next.
        parentToNextRotation = normalizedParentToCurrent * normalizedCurrentToNext;
        parentToNextTranslation = parentToCurrentTranslation + (normalizedParentToCurrent * currentToNextTranslation);
    }

    private void DebugSpecificTransformQuery()
    {
        if (!debugSpecificTransformQuery)
        {
            return;
        }

        float interval = debugQueryIntervalSeconds > 0f ? debugQueryIntervalSeconds : 0f;
        if (interval > 0f && Time.unscaledTime < nextDebugQueryTime)
        {
            return;
        }

        nextDebugQueryTime = Time.unscaledTime + interval;

        string sourceFrame = NormalizeFrameId(debugQuerySourceFrame);
        string targetFrame = NormalizeFrameId(debugQueryTargetFrame);

        if (string.IsNullOrEmpty(sourceFrame) || string.IsNullOrEmpty(targetFrame))
        {
            Debug.LogWarning("[TF_Subscriber][DebugQuery] sourceFrame o targetFrame vacios. Revisa los campos de Debug Query Transform.");
            return;
        }

        bool found = TryGetTransform(sourceFrame, targetFrame, out TFData resolvedTf);
        bool isDirect = false;
        int linkCount;
        int tfMsgCount;
        int tfUpdateCount;

        lock (tfLock)
        {
            isDirect = transformsByLink.ContainsKey(BuildLinkKey(sourceFrame, targetFrame));
            linkCount = transformsByLink.Count;
            tfMsgCount = totalTfMessages;
            tfUpdateCount = totalTransformUpdates;
        }

        debugQueryCounter++;

        if (!found)
        {
            Debug.LogWarning(
                $"[TF_Subscriber][DebugQuery #{debugQueryCounter}] No se pudo resolver '{sourceFrame}' -> '{targetFrame}'. IsReady={IsReady}, Mensajes={tfMsgCount}, Updates={tfUpdateCount}, Links={linkCount}.");
            return;
        }

        string resolutionType = isDirect ? "directa" : "compuesta";
        string extraPathInfo = string.Empty;
        if (debugQueryShowResolvedPath && !isDirect && TryBuildFramePath(sourceFrame, targetFrame, out string resolvedPath))
        {
            extraPathInfo = $", Ruta={resolvedPath}";
        }

        Debug.Log(
            $"[TF_Subscriber][DebugQuery #{debugQueryCounter}] '{sourceFrame}' -> '{targetFrame}' ({resolutionType}) " +
            $"Pos=({resolvedTf.Translation.x:F4}, {resolvedTf.Translation.y:F4}, {resolvedTf.Translation.z:F4}) " +
            $"Rot=({resolvedTf.Rotation.x:F4}, {resolvedTf.Rotation.y:F4}, {resolvedTf.Rotation.z:F4}, {resolvedTf.Rotation.w:F4}) " +
            $"Stamp={resolvedTf.StampSeconds:F6}, Links={linkCount}, Mensajes={tfMsgCount}, Updates={tfUpdateCount}{extraPathInfo}");
    }

    private bool TryBuildFramePath(string sourceFrame, string targetFrame, out string pathText)
    {
        pathText = string.Empty;

        lock (tfLock)
        {
            Dictionary<string, List<GraphEdge>> graph = BuildTransformGraph();
            if (!graph.ContainsKey(sourceFrame))
            {
                return false;
            }

            Queue<string> queue = new Queue<string>();
            HashSet<string> visited = new HashSet<string>();
            Dictionary<string, string> previous = new Dictionary<string, string>();

            queue.Enqueue(sourceFrame);
            visited.Add(sourceFrame);

            bool reached = false;
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                if (current == targetFrame)
                {
                    reached = true;
                    break;
                }

                if (!graph.TryGetValue(current, out List<GraphEdge> neighbors))
                {
                    continue;
                }

                for (int i = 0; i < neighbors.Count; i++)
                {
                    string next = neighbors[i].TargetFrame;
                    if (visited.Contains(next))
                    {
                        continue;
                    }

                    visited.Add(next);
                    previous[next] = current;
                    queue.Enqueue(next);
                }
            }

            if (!reached)
            {
                return false;
            }

            List<string> frames = new List<string>();
            string cursor = targetFrame;
            frames.Add(cursor);

            while (previous.TryGetValue(cursor, out string prev))
            {
                frames.Add(prev);
                cursor = prev;
            }

            frames.Reverse();
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < frames.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(" -> ");
                }

                builder.Append(frames[i]);
            }

            pathText = builder.ToString();
            return true;
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

    private static string NormalizeTopicName(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return string.Empty;
        }

        string normalized = topic.Trim();
        if (!normalized.StartsWith("/"))
        {
            normalized = "/" + normalized;
        }

        return normalized;
    }

    private static string BuildResolvedNodeName(string configuredNodeName, string gameObjectName, int instanceId)
    {
        string baseName = string.IsNullOrWhiteSpace(configuredNodeName) ? "tf_subscriber" : configuredNodeName;
        string rawName = $"{baseName}_{gameObjectName}_{Mathf.Abs(instanceId)}";
        return SanitizeRosNodeName(rawName);
    }

    private static string SanitizeRosNodeName(string nodeNameRaw)
    {
        if (string.IsNullOrWhiteSpace(nodeNameRaw))
        {
            return "tf_subscriber";
        }

        char[] chars = nodeNameRaw.ToLowerInvariant().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char current = chars[i];
            if (!char.IsLetterOrDigit(current) && current != '_')
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
            sanitized = "tf_subscriber";
        }

        if (char.IsDigit(sanitized[0]))
        {
            sanitized = "n_" + sanitized;
        }

        return sanitized;
    }

    private static double ToSeconds(builtin_interfaces.msg.Time timeMessage)
    {
        if (timeMessage == null)
        {
            return 0d;
        }

        return timeMessage.Sec + timeMessage.Nanosec * 1e-9;
    }

    private void CleanupRos2Resources()
    {
        if (tfSubscription != null && ros2Node != null)
        {
            try
            {
                ros2Node.RemoveSubscription<TFMessage>(tfSubscription);
            }
            catch
            {
            }
            tfSubscription = null;
        }

        if (tfStaticSubscription != null && ros2Node != null)
        {
            try
            {
                ros2Node.RemoveSubscription<TFMessage>(tfStaticSubscription);
            }
            catch
            {
            }
            tfStaticSubscription = null;
        }

        if (ros2Node != null && ros2Unity != null)
        {
            try
            {
                ros2Unity.RemoveNode(ros2Node);
            }
            catch
            {
            }
            ros2Node = null;
        }
    }

    private void OnDestroy()
    {
        LogInfo("OnDestroy llamado. Liberando recursos ROS2.");
        CleanupRos2Resources();

        if (Instance == this)
        {
            Instance = null;
            LogInfo("Instance limpiada.");
        }
    }
}
