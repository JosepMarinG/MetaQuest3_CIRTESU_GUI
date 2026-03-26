using UnityEngine;
using ROS2;
using sensor_msgs.msg;

public class JointStateSubscriber : MonoBehaviour
{
    [Header("Configuración ROS 2")]
    public string topicName = "/joint_states";
    
    [Header("Configuración de Joints")]
    public string robotNamespace = "girona500_UJI";
    
    [Header("Mapeo de Joints")]
    [SerializeField] private ArticulationBody[] jointArticulations = new ArticulationBody[0];
    [SerializeField] private string[] jointNames = new string[0];
    
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private ISubscription<sensor_msgs.msg.JointState> jointStateSubscription;
    
    private bool initialized = false;
    private bool loggedMissingRos2Unity = false;
    private bool loggedRos2NotReady = false;

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
        if (initialized)
            return;

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

        // Procesar cada joint del mensaje
        for (int i = 0; i < message.Name.Length; i++)
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

            if (jointIndex >= 0 && jointIndex < jointArticulations.Length)
            {
                ArticulationBody articulationBody = jointArticulations[jointIndex];
                if (articulationBody != null)
                {
                    UpdateJointPosition(articulationBody, (float)position, jointName);
                }
            }
        }
    }

    private void UpdateJointPosition(ArticulationBody body, float position, string jointName)
    {
        if (body == null)
            return;

        try
        {
            // Convertir de radianes a grados si es necesario
            float positionDegrees = position * Mathf.Rad2Deg;

            // Actualizar la posición del joint
            var drive = body.xDrive;
            drive.target = position; // Usar radianes directamente
            body.xDrive = drive;

            // Log para depuración (comentar si causa problemas de rendimiento)
            // Debug.Log($"[JointStateSubscriber] Actualizado '{jointName}': {position:F3} rad ({positionDegrees:F1}°)");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[JointStateSubscriber] Error actualizando joint '{jointName}': {ex}");
        }
    }

    // Método público para asignar manualmente los joints (útil si lo llamas desde el inspector)
    public void SetupJoints(ArticulationBody[] articles, string[] names)
    {
        if (articles.Length != names.Length)
        {
            Debug.LogError("[JointStateSubscriber] El número de ArticulationBody no coincide con el de nombres.");
            return;
        }

        jointArticulations = articles;
        jointNames = names;
        Debug.Log($"[JointStateSubscriber] {jointArticulations.Length} joints configurados.");
    }

    // Método helper para encontrar automáticamente los joints
    public void AutodetectJoints()
    {
        Debug.Log("[JointStateSubscriber] Buscando ArticulationBody automáticamente...");
        
        var allArticulations = GetComponentsInChildren<ArticulationBody>();
        System.Collections.Generic.List<ArticulationBody> bodies = new();
        System.Collections.Generic.List<string> names = new();

        foreach (var article in allArticulations)
        {
            bodies.Add(article);
            // El nombre se puede extraer del GameObject
            names.Add(article.gameObject.name);
            Debug.Log($"[JointStateSubscriber] Encontrado: {article.gameObject.name}");
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
