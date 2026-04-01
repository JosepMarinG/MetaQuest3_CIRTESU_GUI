using UnityEngine;
using ROS2;
using sensor_msgs.msg;
using System.Collections.Generic;

public class JointStateSubscriber : MonoBehaviour
{
    [Header("Configuración ROS 2")]
    public string topicName = "/joint_states";
    
    [Header("Configuración de Joints")]
    public string robotNamespace = "girona500_UJI";
    
    [Header("Mapeo de Joints")]
    [SerializeField] private Transform[] jointTransforms = new Transform[0];
    [SerializeField] private string[] jointNames = new string[0];

    [Header("Diagnóstico")]
    [SerializeField] private bool enableJointDiagnostics = false;
    [SerializeField, Min(0.1f)] private float diagnosticLogIntervalSeconds = 0.5f;
    
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private ISubscription<sensor_msgs.msg.JointState> jointStateSubscription;
    
    private bool initialized = false;
    private bool loggedMissingRos2Unity = false;
    private bool loggedRos2NotReady = false;

    private readonly object pendingLock = new object();
    private readonly Dictionary<int, float> pendingTargetsDeg = new Dictionary<int, float>();
    private readonly Dictionary<int, float> lastAppliedTargetsDeg = new Dictionary<int, float>();
    private float nextDiagnosticLogTime = 0f;

    void Start()
    {
        Debug.Log($"[JointStateSubscriber] Iniciando en '{gameObject.name}'...");
        ros2Unity = Object.FindAnyObjectByType<ROS2UnityComponent>();
        
        if (ros2Unity == null)
        {
            Debug.LogError("[JointStateSubscriber] No se encontró ROS2UnityComponent en la escena.");
            return;
        }
        
        Debug.Log($"[JointStateSubscriber] ROS2UnityComponent encontrado.");
    }

    void Update()
    {
        if (!initialized)
        {
            if (ros2Unity == null)
            {
                if (!loggedMissingRos2Unity)
                {
                    Debug.LogError("[JointStateSubscriber] ros2Unity sigue siendo null.");
                    loggedMissingRos2Unity = true;
                }
                return;
            }

            if (!ros2Unity.Ok())
            {
                if (!loggedRos2NotReady)
                {
                    Debug.LogWarning("[JointStateSubscriber] ROS2 no está listo. Esperando...");
                    loggedRos2NotReady = true;
                }
                return;
            }

            if (loggedRos2NotReady)
            {
                Debug.Log("[JointStateSubscriber] ROS2 está listo. Inicializando...");
                loggedRos2NotReady = false;
            }

            InitializeROS2();
            return;
        }

        Dictionary<int, float> targetsSnapshot;
        lock (pendingLock)
        {
            if (pendingTargetsDeg.Count == 0)
            {
                if (enableJointDiagnostics && Time.time >= nextDiagnosticLogTime)
                {
                    nextDiagnosticLogTime = Time.time + diagnosticLogIntervalSeconds;
                    LogJointDiagnostics();
                }
                return;
            }

            targetsSnapshot = new Dictionary<int, float>(pendingTargetsDeg);
            pendingTargetsDeg.Clear();
        }

        foreach (var kvp in targetsSnapshot)
        {
            int jointIndex = kvp.Key;
            float targetDeg = kvp.Value;

            if (jointIndex < 0 || jointIndex >= jointTransforms.Length)
                continue;

            Transform jointTransform = jointTransforms[jointIndex];
            if (jointTransform == null)
                continue;

            ApplyJointRotationPreset(jointIndex, jointTransform, targetDeg);

            if (enableJointDiagnostics)
            {
                lastAppliedTargetsDeg[jointIndex] = targetDeg;
            }
        }

        if (enableJointDiagnostics && Time.time >= nextDiagnosticLogTime)
        {
            nextDiagnosticLogTime = Time.time + diagnosticLogIntervalSeconds;
            LogJointDiagnostics();
        }
    }

    private void InitializeROS2()
    {
        string nodeName = "joint_state_sub_" + Random.Range(0, 1000);
        Debug.Log($"[JointStateSubscriber] Creando nodo '{nodeName}'...");

        try
        {
            ros2Node = ros2Unity.CreateNode(nodeName);

            if (ros2Node == null)
            {
                Debug.LogError("[JointStateSubscriber] No se pudo crear el nodo ROS2.");
                return;
            }

            Debug.Log($"[JointStateSubscriber] Nodo creado. Suscribiéndose a '{topicName}'...");
            jointStateSubscription = ros2Node.CreateSubscription<sensor_msgs.msg.JointState>(
                topicName,
                OnJointStateReceived
            );

            initialized = true;
            Debug.Log("[JointStateSubscriber] Inicialización completada.");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[JointStateSubscriber] Error al inicializar: {ex}");
        }
    }

    private void OnJointStateReceived(sensor_msgs.msg.JointState message)
    {
        if (message == null || message.Name == null || message.Position == null)
        {
            Debug.LogWarning("[JointStateSubscriber] Mensaje de joint_states vacío o inválido.");
            return;
        }

        int count = Mathf.Min(message.Name.Length, message.Position.Length);
        if (count == 0)
            return;

        // Procesar cada joint del mensaje
        for (int i = 0; i < count; i++)
        {
            string jointName = message.Name[i];
            double position = message.Position[i];

            // Buscar el índice del joint en nuestro array
            int jointIndex = System.Array.IndexOf(jointNames, jointName);

            if (jointIndex == -1)
            {
                // Joint no encontrado en nuestro mapeo
                continue;
            }

            if (jointIndex >= 0 && jointIndex < jointTransforms.Length)
            {
                Transform jointTransform = jointTransforms[jointIndex];
                if (jointTransform != null)
                {
                    UpdateJointPosition((float)position, jointName, jointIndex);
                }
            }
        }
    }

    private void UpdateJointPosition(float position, string jointName, int jointIndex)
    {
        try
        {
            // Convertir de radianes a grados si es necesario
            float positionDegrees = position * Mathf.Rad2Deg;

            lock (pendingLock)
            {
                pendingTargetsDeg[jointIndex] = positionDegrees;
            }

            // Log para depuración (comentar si causa problemas de rendimiento)
            Debug.Log($"[JointStateSubscriber] Actualizado '{jointName}': {position:F3} rad ({positionDegrees:F1}°)");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[JointStateSubscriber] Error actualizando joint '{jointName}': {ex}");
        }
    }

    private void ApplyJointRotationPreset(int jointIndex, Transform jointTransform, float positionDegrees)
    {
        switch (jointIndex)
        {
            case 1:
            {
                jointTransform.localEulerAngles = new Vector3(
                    -70f + positionDegrees * 100f,
                    180f,
                    0f
                );

                if (jointTransforms.Length > 0 && jointTransforms[0] != null)
                {
                    jointTransforms[0].localEulerAngles = new Vector3(
                        70f - positionDegrees * 100f,
                        180f,
                        0f
                    );

                    if (enableJointDiagnostics)
                    {
                        lastAppliedTargetsDeg[0] = 70f - positionDegrees * 100f;
                    }
                }
                break;
            }
            case 2:
                jointTransform.localEulerAngles = new Vector3(0f, -positionDegrees, 180f);
                break;
            case 3:
                jointTransform.localEulerAngles = new Vector3(-positionDegrees, 0f, -90f);
                break;
            case 4:
                jointTransform.localEulerAngles = new Vector3(0f, -positionDegrees, 0f);
                break;
            case 5:
                jointTransform.localEulerAngles = new Vector3(-positionDegrees, 180f, 90f);
                break;
            case 6:
                jointTransform.localEulerAngles = new Vector3(positionDegrees, 0f, 90f);
                break;
            case 7:
                jointTransform.localEulerAngles = new Vector3(90f + positionDegrees, 0f, -90f);
                break;
            default:
                jointTransform.localEulerAngles = new Vector3(positionDegrees, 0f, 0f);
                break;
        }
    }

    private void LogJointDiagnostics()
    {
        foreach (var kvp in lastAppliedTargetsDeg)
        {
            int jointIndex = kvp.Key;
            float targetDeg = kvp.Value;

            if (jointIndex < 0 || jointIndex >= jointTransforms.Length)
                continue;

            Transform jointTransform = jointTransforms[jointIndex];
            if (jointTransform == null)
                continue;

            Vector3 currentEuler = jointTransform.localEulerAngles;
            string jointLabel = jointIndex < jointNames.Length ? jointNames[jointIndex] : jointTransform.name;

            Debug.Log($"[JointStateSubscriber][Diag] '{jointLabel}' target={targetDeg:F2} deg | euler=({currentEuler.x:F2}, {currentEuler.y:F2}, {currentEuler.z:F2})");
        }
    }

    // Método público para asignar manualmente los joints (útil si lo llamas desde el inspector)
    public void SetupJoints(Transform[] joints, string[] names)
    {
        if (joints.Length != names.Length)
        {
            Debug.LogError("[JointStateSubscriber] El número de Transform no coincide con el de nombres.");
            return;
        }

        jointTransforms = joints;
        jointNames = names;
        Debug.Log($"[JointStateSubscriber] {jointTransforms.Length} joints configurados.");
    }

    // Método helper para encontrar automáticamente los transforms
    public void AutodetectJoints()
    {
        Debug.Log("[JointStateSubscriber] Buscando transforms automáticamente...");
        
        var allTransforms = GetComponentsInChildren<Transform>();
        System.Collections.Generic.List<Transform> bodies = new();
        System.Collections.Generic.List<string> names = new();

        foreach (var t in allTransforms)
        {
            if (t == transform)
                continue;

            bodies.Add(t);
            // El nombre se puede extraer del GameObject
            names.Add(t.gameObject.name);
            Debug.Log($"[JointStateSubscriber] Encontrado: {t.gameObject.name}");
        }

        SetupJoints(bodies.ToArray(), names.ToArray());
    }

    void OnDestroy()
    {
        // Limpiar la suscripción si es necesario
        if (jointStateSubscription != null)
        {
            Debug.Log("[JointStateSubscriber] Destruyendo suscripción.");
        }
    }
}
