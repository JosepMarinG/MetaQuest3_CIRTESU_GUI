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
    private bool warnedInitException;

    [Header("Debug")]
    [SerializeField] private bool verboseDebugLogs = true;
    [SerializeField] private int messageSummaryEveryN = 50;

    private readonly object tfLock = new object();
    private readonly Dictionary<string, TFData> transformsByLink = new Dictionary<string, TFData>();
    private readonly Dictionary<string, TFData> latestTransformByChild = new Dictionary<string, TFData>();

    private int totalTfMessages;
    private int totalTransformUpdates;

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
        string linkKey = BuildLinkKey(normalizedParent, normalizedChild);
        lock (tfLock)
        {
            bool found = transformsByLink.TryGetValue(linkKey, out tfData);
            if (!found && verboseDebugLogs)
            {
                string availableLinks = transformsByLink.Count > 0 ? string.Join(", ", transformsByLink.Keys) : "NINGUNO";
                LogInfo($"TryGetTransform miss: '{normalizedParent}' -> '{normalizedChild}'. Links disponibles: {availableLinks} (total={transformsByLink.Count}).");
            }
            return found;
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
