using UnityEngine;
using ROS2;
using CesiumForUnity;
using Unity.Mathematics;

public class RobotTelemetryController : MonoBehaviour
{
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private ISubscription<sensor_msgs.msg.NavSatFix> gpsSub;
    private ISubscription<geometry_msgs.msg.Quaternion> orientationSub;

    [Header("Configuración")]
    public string gpsTopic = "/catamaran/mavlink/gps";
    public string orientationTopic = "/mavlink/orientation";
    public CesiumGlobeAnchor globeAnchor;
    public Transform orientationTarget;
    public bool applyOrientationAsLocalRotation = true;
    public float updateRate = 6f; // Hz
    
    private double lat = 39.96837693;
    private double lon = 0.01961313;
    private double alt = 48.5374;
    private float qx = 0f;
    private float qy = 0f;
    private float qz = 0f;
    private float qw = 1f;
    private bool newDataReceived = false;
    private bool newOrientationReceived = false;
    private int messageCount = 0;
    private int orientationMessageCount = 0;
    private float updateTimer = 0f;

    void Start()
    {
        ros2Unity = GetComponentInParent<ROS2UnityComponent>();
        if (globeAnchor == null) globeAnchor = GetComponent<CesiumGlobeAnchor>();
        if (orientationTarget == null)
        {
            orientationTarget = globeAnchor != null ? globeAnchor.transform : transform;
        }

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

        orientationSub = ros2Node.CreateSubscription<geometry_msgs.msg.Quaternion>(
            orientationTopic, msg => {
                qx = (float)msg.X;
                qy = (float)msg.Y;
                qz = (float)msg.Z;
                qw = (float)msg.W;
                newOrientationReceived = true;
                orientationMessageCount++;
                Debug.Log($"[RobotTelemetry][Orientation #{orientationMessageCount}] Recibido → x:{qx:F4}, y:{qy:F4}, z:{qz:F4}, w:{qw:F4}");
            }, qos);

        Debug.Log($"[RobotTelemetry] Suscrito a {gpsTopic}");
        Debug.Log($"[RobotTelemetry] Suscrito a {orientationTopic}");
        Debug.Log($"[RobotTelemetry] GlobeAnchor inicial: {(globeAnchor != null ? globeAnchor.longitudeLatitudeHeight.ToString() : "null")}");
        Debug.Log($"[RobotTelemetry] OrientationTarget: {(orientationTarget != null ? orientationTarget.name : "null")}, applyLocalRotation={applyOrientationAsLocalRotation}");
    }

    private void Update()
    {
        updateTimer += Time.deltaTime;
        
        // Solo actualizar a la frecuencia especificada (6 Hz por defecto)
        if (updateTimer >= 1f / updateRate)
        {
            if (newDataReceived && globeAnchor != null)
            {
                double3 newPosition = new double3(lon, lat, alt);
                globeAnchor.longitudeLatitudeHeight = newPosition;
                
                Debug.Log($"[Update] GlobeAnchor actualizado → Lon:{lon:F6}, Lat:{lat:F6}, Alt:{alt:F1}");
                Debug.Log($"[Update] Valor en GlobeAnchor: {globeAnchor.longitudeLatitudeHeight}");
                
                newDataReceived = false;
            }

            if (newOrientationReceived && orientationTarget != null)
            {
                Quaternion targetRotation = Quaternion.Normalize(new Quaternion(qx, qy, qz, qw));

                if (applyOrientationAsLocalRotation)
                {
                    orientationTarget.localRotation = targetRotation;
                }
                else
                {
                    orientationTarget.rotation = targetRotation;
                }
                Debug.Log($"[Update] Rotación aplicada → x:{qx:F4}, y:{qy:F4}, z:{qz:F4}, w:{qw:F4}");
                newOrientationReceived = false;
            }

            updateTimer = 0f;
        }
    }
}
