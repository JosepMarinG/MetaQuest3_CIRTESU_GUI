using UnityEngine;
using TMPro;
using ROS2;
using CesiumForUnity;
using Unity.Mathematics; // Necesario para double3 y math.abs

public class RobotTelemetryController : MonoBehaviour
{
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private ISubscription<sensor_msgs.msg.NavSatFix> gpsSub;
    private ISubscription<geometry_msgs.msg.Quaternion> orientSub;

    [Header("UI - Configuraci�n")]
    public TMP_InputField gpsTopicInput;
    public string defaultOrientationTopic = "/mavlink/orientation";

    [Header("Referencias Cesium")]
    public CesiumGlobeAnchor globeAnchor;
    public MinimapNavigationController mapController;

    [Header("Modo Seguimiento")]
    public bool isFollowActive = false;

    // Variables de estado (datos que llegan de ROS)
    private double rosLat, rosLon, rosHeight;
    private Quaternion rosRot = Quaternion.identity;
    private bool firstDataReceived = false;

    public CesiumCartographicPolygon clippingPolygon;
    public GameObject robotModel; 
    public float margin = 0.05f;

    [Header("Panel")]
    public GameObject rootPanel;

    void Start()
    {
        ros2Unity = GetComponentInParent<ROS2UnityComponent>();
        if (globeAnchor == null) globeAnchor = GetComponent<CesiumGlobeAnchor>();

        if (ros2Unity == null)
        {
            Debug.LogError("[RobotTelemetry] No se encontró ROS2UnityComponent");
            return;
        }

        if (!ros2Unity.Ok())
        {
            Debug.LogError("[RobotTelemetry] ROS2Unity no está listo");
            return;
        }

        ros2Node = ros2Unity.CreateNode("RobotTelemetryNode");
        
        if (ros2Node == null)
        {
            Debug.LogError("[RobotTelemetry] No se pudo crear el nodo ROS2");
            return;
        }

        Debug.Log("[RobotTelemetry] Nodo creado correctamente. Iniciando suscripciones...");
        StartSubscriptions();
    }

    public void StartSubscriptions()
    {
        if (ros2Node == null)
        {
            Debug.LogError("[RobotTelemetry] ros2Node es null, no se pueden crear suscripciones");
            return;
        }

        string gpsTopic = string.IsNullOrEmpty(gpsTopicInput.text) ? "/mavlink/gps" : gpsTopicInput.text;

        try
        {
            gpsSub = ros2Node.CreateSubscription<sensor_msgs.msg.NavSatFix>(
                gpsTopic, msg => {
                    rosLat = msg.Latitude;
                    rosLon = msg.Longitude;
                    rosHeight = msg.Altitude;
                    firstDataReceived = true;
                    Debug.Log($"[RobotTelemetry] GPS recibido: Lat={rosLat}, Lon={rosLon}, Alt={rosHeight}");
                });
            Debug.Log($"[RobotTelemetry] Suscripción GPS creada en topic: {gpsTopic}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[RobotTelemetry] Error al suscribirse a GPS {gpsTopic}: {e.Message}");
        }

        try
        {
            orientSub = ros2Node.CreateSubscription<geometry_msgs.msg.Quaternion>(
                defaultOrientationTopic, msg => {
                    rosRot = new Quaternion((float)msg.X, (float)msg.Y, (float)msg.Z, (float)msg.W);
                    Debug.Log($"[RobotTelemetry] Orientación recibida: {rosRot}");
                });
            Debug.Log($"[RobotTelemetry] Suscripción Orientación creada en topic: {defaultOrientationTopic}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[RobotTelemetry] Error al suscribirse a Orientación {defaultOrientationTopic}: {e.Message}");
        }
    }

    void Update()
    {
        if (!firstDataReceived) return;

        // ACCESO CORRECTO A COORDENADAS (Evita CS0618)
        // x = Longitud, y = Latitud, z = Altura
        double3 currentLLH = globeAnchor.longitudeLatitudeHeight;

        // Comparamos los valores actuales con los nuevos de ROS
        bool positionChanged = math.abs(currentLLH.y - rosLat) > 1e-7 ||
                               math.abs(currentLLH.x - rosLon) > 1e-7;

        if (positionChanged)
        {            Debug.Log($"[RobotTelemetry] Cambio de posición detectado. Lat: {rosLat}, Lon: {rosLon}");            ApplyTelemetry();
        }

        // Aplicamos rotaci�n si hay cambio significativo
        if (Quaternion.Angle(transform.localRotation, rosRot) > 0.1f)
        {
            transform.localRotation = rosRot;
        }

        HandleVisibility();
    }

    void ApplyTelemetry()
    {
        // Actualizamos el GlobeAnchor con el nuevo vector double3
        // IMPORTANTE: El orden es (Longitud, Latitud, Altura)
        globeAnchor.longitudeLatitudeHeight = new double3(rosLon, rosLat, rosHeight);

        // Si el seguimiento est� activo, movemos el origen de la Georeferencia
        if (isFollowActive && mapController != null && mapController.georeference != null)
        {
            mapController.georeference.SetOriginLongitudeLatitudeHeight(rosLon, rosLat, rosHeight);
        }
    }

    public void ToggleFollowRobot()
    {
        isFollowActive = !isFollowActive;
    }

    public void ClosePanel()
    {
        if (rootPanel != null) Destroy(rootPanel);
        else Destroy(transform.root.gameObject);
    }

    void HandleVisibility()
    {
        if (clippingPolygon == null || robotModel == null) return;

        // Obtenemos los l�mites (Bounds) del pol�gono en el mundo de Unity
        // Para que esto funcione, el Pol�gono debe tener un MeshRenderer o un Collider
        Bounds bounds = clippingPolygon.GetComponent<MeshRenderer>().bounds;

        // Comprobamos si la posici�n actual del robot est� dentro de esos l�mites
        // Solo comprobamos X y Z (plano horizontal)
        Vector3 pos = transform.position;
        bool isInside = pos.x >= (bounds.min.x - margin) && pos.x <= (bounds.max.x + margin) &&
                        pos.z >= (bounds.min.z - margin) && pos.z <= (bounds.max.z + margin);

        // Activamos o desactivamos el modelo visual
        if (robotModel.activeSelf != isInside)
        {
            robotModel.SetActive(isInside);
        }
    }

    // Método para debugging - llamar desde el Inspector si es necesario
    public void DebugStatus()
    {
        Debug.Log("=== [RobotTelemetry] Estado de Depuración ===");
        Debug.Log($"ros2Unity: {(ros2Unity != null ? "OK" : "NULL")}");
        Debug.Log($"ros2Unity.Ok(): {(ros2Unity != null ? ros2Unity.Ok() : "N/A")}");
        Debug.Log($"ros2Node: {(ros2Node != null ? "OK" : "NULL")}");
        Debug.Log($"gpsSub: {(gpsSub != null ? "ACTIVA" : "NULL")}");
        Debug.Log($"orientSub: {(orientSub != null ? "ACTIVA" : "NULL")}");
        Debug.Log($"firstDataReceived: {firstDataReceived}");
        Debug.Log($"Datos actuales - Lat: {rosLat}, Lon: {rosLon}, Alt: {rosHeight}");
        Debug.Log($"Topics - GPS: {(string.IsNullOrEmpty(gpsTopicInput.text) ? "/mavlink/gps" : gpsTopicInput.text)}");
        Debug.Log($"Topics - Orientación: {defaultOrientationTopic}");
        Debug.Log("=== Fin Estado ===");
    }
}