using UnityEngine;
using TMPro;
using ROS2;
using CesiumForUnity;

public class RobotTelemetryController : MonoBehaviour
{
    // ... (Variables anteriores: ros2Node, subs, etc.)
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private ISubscription<sensor_msgs.msg.NavSatFix> gpsSub;
    private ISubscription<geometry_msgs.msg.Quaternion> orientSub;

    [Header("UI - Configuración")]
    public TMP_InputField gpsTopicInput;
    public string defaultOrientationTopic = "/mavlink/orientation";

    [Header("Referencias Cesium")]
    public CesiumGlobeAnchor globeAnchor;
    public MinimapNavigationController mapController;

    [Header("Modo Seguimiento")]
    public bool isFollowActive = false; // Estado del seguimiento

    private double currentLat, currentLon, currentHeight;
    private Quaternion currentRot;

    public GameObject rootPanel;

    // --- FUNCIÓN PARA EL BOTÓN ---
    public void ToggleFollowRobot()
    {
        isFollowActive = !isFollowActive;
        Debug.Log($"<color=green>[Telemetry]</color> Modo Seguimiento: {(isFollowActive ? "ON" : "OFF")}");

        // Opcional: Podrías cambiar el color del botón aquí si tienes la referencia
    }

    void Start()
    {
        ros2Unity = GetComponentInParent<ROS2UnityComponent>();
        if (globeAnchor == null) globeAnchor = GetComponent<CesiumGlobeAnchor>();

        if (ros2Unity.Ok())
        {
            ros2Node = ros2Unity.CreateNode("RobotTelemetryNode");
            StartSubscriptions();
        }
    }

    public void StartSubscriptions()
    {
        string gpsTopic = string.IsNullOrEmpty(gpsTopicInput.text) ? "/mavlink/gps" : gpsTopicInput.text;

        gpsSub = ros2Node.CreateSubscription<sensor_msgs.msg.NavSatFix>(
            gpsTopic, msg => {
                currentLat = msg.Latitude;
                currentLon = msg.Longitude;
                currentHeight = msg.Altitude;
                UpdatePosition();
            });

        orientSub = ros2Node.CreateSubscription<geometry_msgs.msg.Quaternion>(
            defaultOrientationTopic, msg => {
                currentRot = new Quaternion((float)msg.X, (float)msg.Y, (float)msg.Z, (float)msg.W);
                UpdateOrientation();
            });
    }

    void UpdatePosition()
    {
        // 1. Siempre movemos el robot físicamente en la maqueta
        globeAnchor.longitudeLatitudeHeight = new Unity.Mathematics.double3((float)currentLon, (float)currentLat, (float)currentHeight);

        // 2. Si el seguimiento está activo, movemos el "suelo" (mapa) para centrar al robot
        if (isFollowActive && mapController != null && mapController.georeference != null)
        {
            // Ponemos el origen del mundo en las coordenadas del robot
            mapController.georeference.SetOriginLongitudeLatitudeHeight(currentLon, currentLat, currentHeight);

            // Forzamos un refresco del overlay para que el recorte sea perfecto mientras se mueve
            // mapController.RefreshOverlay(); // Opcional, si notas lag en el recorte
        }
    }

    void UpdateOrientation()
    {
        transform.localRotation = currentRot;
    }
    public void ClosePanel()
    {
        if (rootPanel != null) Destroy(rootPanel);
        else Destroy(transform.root.gameObject);
    }
}