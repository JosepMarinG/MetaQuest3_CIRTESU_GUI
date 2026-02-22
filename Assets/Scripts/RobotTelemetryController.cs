using UnityEngine;
using ROS2;
using CesiumForUnity;
using Unity.Mathematics;

public class RobotTelemetryController : MonoBehaviour
{
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private ISubscription<sensor_msgs.msg.NavSatFix> gpsSub;

    [Header("Configuración")]
    public string gpsTopic = "/catamaran/mavlink/gps";
    public CesiumGlobeAnchor globeAnchor;
    
    private double lat = 39.96837693;
    private double lon = 0.01961313;
    private double alt = 48.5374;
    private bool newDataReceived = false;
    private int messageCount = 0;

    void Start()
    {
        ros2Unity = GetComponentInParent<ROS2UnityComponent>();
        if (globeAnchor == null) globeAnchor = GetComponent<CesiumGlobeAnchor>();

        if (ros2Unity == null || !ros2Unity.Ok())
        {
            Debug.LogError("[RobotTelemetry] ROS2Unity no disponible");
            return;
        }

        ros2Node = ros2Unity.CreateNode("RobotTelemetryNode");
        if (ros2Node == null)
        {
            Debug.LogError("[RobotTelemetry] No se pudo crear nodo");
            return;
        }

        QualityOfServiceProfile qos = new QualityOfServiceProfile(QosPresetProfile.SENSOR_DATA);

        // Suscripción directa con actualización de variables (QoS sensor data)
        gpsSub = ros2Node.CreateSubscription<sensor_msgs.msg.NavSatFix>(
            gpsTopic, msg => {
                lat = msg.Latitude;
                lon = msg.Longitude;
                alt = msg.Altitude;
                newDataReceived = true;
                messageCount++;
                Debug.Log($"[RobotTelemetry][ROS2 Callback #{messageCount}] Recibido → Lat:{lat:F6}, Lon:{lon:F6}, Alt:{alt:F1}");
            }, qos);

        Debug.Log($"[RobotTelemetry] Suscrito a {gpsTopic}");
        Debug.Log($"[RobotTelemetry] GlobeAnchor inicial: {globeAnchor.longitudeLatitudeHeight}");
    }

    private void Update()
    {
        if (newDataReceived && globeAnchor != null)
        {
            double3 newPosition = new double3(lon, lat, alt);
            globeAnchor.longitudeLatitudeHeight = newPosition;
            
            Debug.Log($"[Update] GlobeAnchor actualizado → Lon:{lon:F6}, Lat:{lat:F6}, Alt:{alt:F1}");
            Debug.Log($"[Update] Valor en GlobeAnchor: {globeAnchor.longitudeLatitudeHeight}");
            
            newDataReceived = false;
        }
    }
}
