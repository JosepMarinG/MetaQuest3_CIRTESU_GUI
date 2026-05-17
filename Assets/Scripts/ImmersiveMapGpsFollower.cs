using UnityEngine;
using ROS2;
using CesiumForUnity;

public class ImmersiveMapGpsFollower : MonoBehaviour
{
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private ISubscription<sensor_msgs.msg.NavSatFix> gpsSub;
    private ISubscription<geometry_msgs.msg.Quaternion> orientationSub;

    [Header("ROS2")]
    public string gpsTopic = "/catamaran/mavlink/gps";
    public string orientationTopic = "/mavlink/orientation";
    public string nodeName = "immersive_map_gps_follower";

    [Header("Mapa inmersivo")]
    [Tooltip("CesiumGeoreference del mapa grande en escala real.")]
    public CesiumGeoreference georeference;

    [Tooltip("Opcional: raiz visual del mapa, sin incluir el robot, para girarla con la orientacion del robot.")]
    public Transform mapYawRoot;

    [Header("Altura")]
    public bool useGpsAltitude = true;
    public double fixedHeight = 0.0;

    [Header("Orientacion")]
    public bool useOrientation = true;
    public bool invertYaw = true;
    public float yawOffsetDegrees = 0f;

    [Header("Actualizacion")]
    public float updateRate = 6f;
    public bool verboseDebugLogs = false;

    private double lat;
    private double lon;
    private double alt;
    private float qx;
    private float qy;
    private float qz;
    private float qw = 1f;
    private bool newGpsReceived;
    private bool newOrientationReceived;
    private float updateTimer;

    private void Start()
    {
        if (georeference == null)
        {
            georeference = GetComponentInChildren<CesiumGeoreference>(true);
        }

        InitializeROS2();
    }

    private void Update()
    {
        updateTimer += Time.deltaTime;
        if (updateTimer < 1f / Mathf.Max(0.1f, updateRate))
        {
            return;
        }

        if (newGpsReceived)
        {
            ApplyGpsToMap();
            newGpsReceived = false;
        }

        if (useOrientation && newOrientationReceived)
        {
            ApplyOrientationToMap();
            newOrientationReceived = false;
        }

        updateTimer = 0f;
    }

    private void InitializeROS2()
    {
        ros2Unity = GetComponentInParent<ROS2UnityComponent>();
        if (ros2Unity == null)
        {
            ros2Unity = Object.FindAnyObjectByType<ROS2UnityComponent>();
        }

        if (ros2Unity == null || !ros2Unity.Ok())
        {
            Debug.LogError("[ImmersiveMapGpsFollower] ROS2Unity no disponible");
            return;
        }

        ros2Node = ros2Unity.CreateNode(nodeName);
        if (ros2Node == null)
        {
            Debug.LogError("[ImmersiveMapGpsFollower] No se pudo crear nodo ROS2");
            return;
        }

        QualityOfServiceProfile qos = new QualityOfServiceProfile(QosPresetProfile.SENSOR_DATA);

        gpsSub = ros2Node.CreateSubscription<sensor_msgs.msg.NavSatFix>(
            gpsTopic,
            msg =>
            {
                lat = msg.Latitude;
                lon = msg.Longitude;
                alt = msg.Altitude;
                newGpsReceived = true;
            },
            qos);

        if (useOrientation)
        {
            orientationSub = ros2Node.CreateSubscription<geometry_msgs.msg.Quaternion>(
                orientationTopic,
                msg =>
                {
                    qx = (float)msg.X;
                    qy = (float)msg.Y;
                    qz = (float)msg.Z;
                    qw = (float)msg.W;
                    newOrientationReceived = true;
                },
                qos);
        }

        if (verboseDebugLogs)
        {
            Debug.Log($"[ImmersiveMapGpsFollower] Suscrito a {gpsTopic} y {(useOrientation ? orientationTopic : "sin orientacion")}");
        }
    }

    private void ApplyGpsToMap()
    {
        if (georeference == null)
        {
            Debug.LogWarning("[ImmersiveMapGpsFollower] Falta asignar CesiumGeoreference.");
            return;
        }

        double height = useGpsAltitude ? alt : fixedHeight;
        georeference.SetOriginLongitudeLatitudeHeight(lon, lat, height);

        if (verboseDebugLogs)
        {
            Debug.Log($"[ImmersiveMapGpsFollower] Origen mapa -> Lon:{lon:F6}, Lat:{lat:F6}, H:{height:F2}");
        }
    }

    private void ApplyOrientationToMap()
    {
        if (mapYawRoot == null)
        {
            return;
        }

        float yawDegrees = ExtractYawDegrees(qx, qy, qz, qw);
        if (invertYaw)
        {
            yawDegrees = -yawDegrees;
        }

        yawDegrees += yawOffsetDegrees;
        mapYawRoot.localRotation = Quaternion.Euler(0f, -yawDegrees, 0f);
    }

    private static float ExtractYawDegrees(float x, float y, float z, float w)
    {
        float sinyCosp = 2f * (w * z + x * y);
        float cosyCosp = 1f - 2f * (y * y + z * z);
        float yawRadians = Mathf.Atan2(sinyCosp, cosyCosp);
        return yawRadians * Mathf.Rad2Deg;
    }
}
