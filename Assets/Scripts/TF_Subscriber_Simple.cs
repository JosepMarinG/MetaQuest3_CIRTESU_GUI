using UnityEngine;
using ROS2;
using tf2_msgs.msg;

public class TF_Subscriber_Simple : MonoBehaviour
{
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;

    [Header("Configuracion")]
    public string tfTopic = "/tf";
    public string tfStaticTopic = "/tf_static";

    [Header("Depuracion")]
    public bool verboseLogs = true;

    private ISubscription<TFMessage> subTf;
    private ISubscription<TFMessage> subTfStatic;

    // Para depuración en el Inspector
    public int tfMessagesReceived = 0;
    public int staticMessagesReceived = 0;

    private bool loggedMissingRos2Unity = false;
    private bool loggedRos2NotReady = false;

    void Start()
    {
        Debug.Log($"[TF_Subscriber] Start en '{gameObject.name}'. Buscando ROS2UnityComponent...");
        ros2Unity = Object.FindAnyObjectByType<ROS2UnityComponent>();

        if (ros2Unity == null)
        {
            Debug.LogError("[TF_Subscriber] No se encontro ROS2UnityComponent en la escena. Sin este componente no se puede crear el nodo ROS2.");
            return;
        }

        Debug.Log($"[TF_Subscriber] ROS2UnityComponent encontrado en '{ros2Unity.gameObject.name}'.");
        Debug.Log($"[TF_Subscriber] Configuracion: tfTopic='{tfTopic}', tfStaticTopic='{tfStaticTopic}'.");
    }

    void Update()
    {
        if (ros2Node != null)
        {
            return;
        }

        if (ros2Unity == null)
        {
            if (!loggedMissingRos2Unity)
            {
                Debug.LogError("[TF_Subscriber] ros2Unity sigue siendo null en Update. Revisa que el ROS2UnityComponent exista y este activo.");
                loggedMissingRos2Unity = true;
            }
            return;
        }

        if (!ros2Unity.Ok())
        {
            if (!loggedRos2NotReady)
            {
                Debug.LogWarning("[TF_Subscriber] ros2Unity.Ok() es false. Esperando a que ROS2 inicialice correctamente...");
                loggedRos2NotReady = true;
            }
            return;
        }

        if (loggedRos2NotReady)
        {
            Debug.Log("[TF_Subscriber] ros2Unity.Ok() paso a true. Intentando crear nodo y suscripciones...");
            loggedRos2NotReady = false;
        }

        string nodeName = "tf_simple_" + Random.Range(0, 1000);
        Debug.Log($"[TF_Subscriber] Intentando crear nodo '{nodeName}'.");

        try
        {
            ros2Node = ros2Unity.CreateNode(nodeName);

            if (ros2Node == null)
            {
                Debug.LogError("[TF_Subscriber] CreateNode devolvio null. No se pueden crear suscripciones.");
                return;
            }

            Debug.Log($"[TF_Subscriber] Nodo ROS2 creado correctamente: '{nodeName}'.");
            SubscribeToTf();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[TF_Subscriber] Excepcion al crear nodo ROS2: {ex}");
        }
    }

    private void SubscribeToTf()
    {
        if (ros2Node == null)
        {
            Debug.LogError("[TF_Subscriber] SubscribeToTf() llamado con ros2Node null.");
            return;
        }

        Debug.Log("[TF_Subscriber] Iniciando creacion de suscripciones /tf y /tf_static...");

        // --- 1. CONFIGURACION PARA /tf (Dinamico) ---
        QualityOfServiceProfile tfQos = new QualityOfServiceProfile();
        tfQos.SetReliability(ReliabilityPolicy.QOS_POLICY_RELIABILITY_BEST_EFFORT);
        tfQos.SetDurability(DurabilityPolicy.QOS_POLICY_DURABILITY_VOLATILE);
        tfQos.SetHistory(HistoryPolicy.QOS_POLICY_HISTORY_KEEP_LAST, 1);
        Debug.Log("[TF_Subscriber] QoS /tf => Reliability: BEST_EFFORT, Durability: VOLATILE, History: KEEP_LAST(1).");

        try
        {
            subTf = ros2Node.CreateSubscription<TFMessage>(
                tfTopic,
                msg =>
                {
                    tfMessagesReceived++;
                    Debug.Log($"[TF_Subscriber] [/tf] Callback recibido. Mensaje #{tfMessagesReceived}.");
                    ProcessMessage(msg, false);
                },
                tfQos);

            if (subTf == null)
            {
                Debug.LogError("[TF_Subscriber] Fallo al crear suscripcion de /tf: CreateSubscription devolvio null.");
            }
            else
            {
                Debug.Log("[TF_Subscriber] Suscripcion a /tf creada correctamente.");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[TF_Subscriber] Excepcion al crear suscripcion /tf: {ex}");
        }

        // --- 2. CONFIGURACION PARA /tf_static (Estatico) ---
        QualityOfServiceProfile tfStaticQos = new QualityOfServiceProfile();
        tfStaticQos.SetReliability(ReliabilityPolicy.QOS_POLICY_RELIABILITY_RELIABLE);
        tfStaticQos.SetDurability(DurabilityPolicy.QOS_POLICY_DURABILITY_TRANSIENT_LOCAL);
        tfStaticQos.SetHistory(HistoryPolicy.QOS_POLICY_HISTORY_KEEP_LAST, 1);
        Debug.Log("[TF_Subscriber] QoS /tf_static => Reliability: RELIABLE, Durability: TRANSIENT_LOCAL, History: KEEP_LAST(1).");

        try
        {
            subTfStatic = ros2Node.CreateSubscription<TFMessage>(
                tfStaticTopic,
                msg =>
                {
                    staticMessagesReceived++;
                    Debug.Log($"[TF_Subscriber] [/tf_static] Callback recibido. Mensaje #{staticMessagesReceived}.");
                    ProcessMessage(msg, true);
                },
                tfStaticQos);

            if (subTfStatic == null)
            {
                Debug.LogError("[TF_Subscriber] Fallo al crear suscripcion de /tf_static: CreateSubscription devolvio null.");
            }
            else
            {
                Debug.Log("[TF_Subscriber] Suscripcion a /tf_static creada correctamente.");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[TF_Subscriber] Excepcion al crear suscripcion /tf_static: {ex}");
        }

        Debug.Log("[TF_Subscriber] Proceso de suscripcion finalizado.");
    }

    private void ProcessMessage(TFMessage msg, bool isStatic)
    {
        string stream = isStatic ? "/tf_static" : "/tf";

        if (msg == null)
        {
            Debug.LogError($"[TF_Subscriber] [{stream}] Mensaje recibido null.");
            return;
        }

        if (msg.Transforms == null)
        {
            Debug.LogWarning($"[TF_Subscriber] [{stream}] Mensaje recibido con Transforms null.");
            return;
        }

        int transformCount = 0;
        foreach (var transform in msg.Transforms)
        {
            transformCount++;
            if (verboseLogs)
            {
                Debug.Log($"[TF_Subscriber] [{stream}] Transform #{transformCount}: parent='{transform.Header.Frame_id}' -> child='{transform.Child_frame_id}'.");
            }
        }

        if (transformCount == 0)
        {
            Debug.LogWarning($"[TF_Subscriber] [{stream}] Mensaje recibido sin transforms.");
        }
        else
        {
            Debug.Log($"[TF_Subscriber] [{stream}] Mensaje procesado con {transformCount} transforms.");
        }
    }

    private void OnDestroy()
    {
        Debug.Log("[TF_Subscriber] OnDestroy llamado. Liberando suscripciones y nodo...");

        if (ros2Node == null || ros2Unity == null)
        {
            Debug.LogWarning("[TF_Subscriber] No hay nodo o ROS2UnityComponent para liberar (ros2Node o ros2Unity es null).");
            return;
        }

        try
        {
            if (subTf != null)
            {
                ros2Node.RemoveSubscription<TFMessage>(subTf);
                Debug.Log("[TF_Subscriber] Suscripcion /tf eliminada.");
            }
            else
            {
                Debug.LogWarning("[TF_Subscriber] subTf ya era null en OnDestroy.");
            }

            if (subTfStatic != null)
            {
                ros2Node.RemoveSubscription<TFMessage>(subTfStatic);
                Debug.Log("[TF_Subscriber] Suscripcion /tf_static eliminada.");
            }
            else
            {
                Debug.LogWarning("[TF_Subscriber] subTfStatic ya era null en OnDestroy.");
            }

            ros2Unity.RemoveNode(ros2Node);
            Debug.Log("[TF_Subscriber] Nodo ROS2 eliminado correctamente.");
            ros2Node = null;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[TF_Subscriber] Excepcion durante limpieza en OnDestroy: {ex}");
        }
    }
}