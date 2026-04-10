using System;
using ROS2;
using sensor_msgs.msg;
using UnityEngine;
using UnityEngine.Rendering;

public class PointCloudSubscriberGPU : MonoBehaviour
{
    private const string PointShaderName = "Unlit/ROS/Point";

    [Header("Configuracion ROS2")]
    [SerializeField] private string topicName = "/points";

    [Header("Visualizacion")]
    [SerializeField] private Material pointMaterial;
    [SerializeField] private Shader pointShader;
    [SerializeField, Min(1)] private int maxBufferPoints = 500000;
    [SerializeField, Min(1)] private int displayPointLimit = 120000;
    [SerializeField, Min(0.001f)] private float pointSize = 0.05f;
    [SerializeField, Min(0.001f)] private float cloudScale = 1f;
    [SerializeField] private bool centerCloudOnBounds = false;
    [SerializeField] private bool displayAllPoints = true;
    [SerializeField] private bool applyRosToUnityTransform = false;
    [SerializeField] private bool forceSolidWhiteColor = true;
    [SerializeField] private Transform anchorTransform;
    [SerializeField] private Vector3 localPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 localEulerOffset = Vector3.zero;
    [SerializeField] private Color intensityMin = Color.black;
    [SerializeField] private Color intensityMax = Color.white;
    [SerializeField] private bool visualizationEnabled = true;

    [Header("Diagnostico")]
    [SerializeField] private bool verboseLogs = false;
    [SerializeField] private bool enableBasicLogs = true;

    [Header("UI Feedback")]
    [SerializeField] private ToggleIconFeedback iconFeedback;

    private const int BytesPerPoint = 16; // x,y,z,intensity(or rgb packed)

    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private ISubscription<PointCloud2> pointCloudSubscription;

    private GraphicsBuffer meshTriangles;
    private GraphicsBuffer meshVertices;
    private GraphicsBuffer pointData;
    private RenderParams renderParams;
    private Mesh pointMesh;
    private Material runtimePointMaterial;

    private readonly object pendingLock = new object();
    private byte[] pendingPointBytes;
    private int pendingPointCount;

    private int currentPointCount;
    private bool initialized;
    private bool renderResourcesReady;
    private bool loggedRosWaiting;
    private bool firstUpdateLogged;
    private int receivedMessageCount;
    private int uploadCount;
    private float nextVerboseUploadLogTime;
    private float nextRosWaitLogTime;
    private float nextStateLogTime;
    private bool loggedRenderState;
    private bool loggedFirstUpload;
    private float nextBoundsLogTime;
    private Vector3 latestCloudCenter = Vector3.zero;

    private uint baseVertexIndex;

    public int CurrentPointCount => currentPointCount;

    private void Start()
    {
        if (enableBasicLogs)
        {
            Debug.Log($"[PointCloudSubscriberGPU] Start en '{gameObject.name}'. Topic='{topicName}', visualizationEnabled={visualizationEnabled}");
        }

        ros2Unity = UnityEngine.Object.FindAnyObjectByType<ROS2UnityComponent>();
        if (ros2Unity == null)
        {
            Debug.LogError("[PointCloudSubscriberGPU] No se encontro ROS2UnityComponent en la escena.");
            return;
        }

        if (!EnsurePointMaterial())
        {
            return;
        }

        if (!CreateRenderResources())
        {
            Debug.LogError("[PointCloudSubscriberGPU] No se pudieron crear recursos de render GPU.");
            return;
        }

        if (verboseLogs)
        {
            Debug.Log("[PointCloudSubscriberGPU] Esperando ROS2 listo para crear la suscripcion...");
        }

        UpdateIconFeedback();
    }

    private void Update()
    {
        if (verboseLogs && Time.time >= nextStateLogTime)
        {
            Debug.Log($"[PointCloudSubscriberGPU] Update heartbeat. enabled={enabled}, activeInHierarchy={gameObject.activeInHierarchy}, isActiveAndEnabled={isActiveAndEnabled}, initialized={initialized}, pending={(pendingPointBytes != null ? pendingPointCount : 0)}, currentPointCount={currentPointCount}");
            nextStateLogTime = Time.time + 2f;
        }

        if (!firstUpdateLogged && enableBasicLogs)
        {
            Debug.Log($"[PointCloudSubscriberGPU] Primer Update en '{gameObject.name}'.");
            firstUpdateLogged = true;
        }

        if (!initialized)
        {
            TryInitializeSubscription();
        }

        UploadPendingPointCloud();

        if (!visualizationEnabled || !renderResourcesReady || currentPointCount <= 0)
        {
            if (verboseLogs && Time.time >= nextStateLogTime)
            {
                Debug.Log($"[PointCloudSubscriberGPU] Render omitido. visualizationEnabled={visualizationEnabled}, renderResourcesReady={renderResourcesReady}, currentPointCount={currentPointCount}, initialized={initialized}, pending={(pendingPointBytes != null ? pendingPointCount : 0)}");
                nextStateLogTime = Time.time + 2f;
            }

            return;
        }

        Transform anchor = anchorTransform != null ? anchorTransform : transform;
        Matrix4x4 anchorMatrix = anchor.localToWorldMatrix;
        Matrix4x4 localOffsetMatrix = Matrix4x4.TRS(localPositionOffset, Quaternion.Euler(localEulerOffset), Vector3.one);
        Matrix4x4 cloudScaleMatrix = Matrix4x4.Scale(new Vector3(cloudScale, cloudScale, cloudScale));
        Matrix4x4 centerTranslation = centerCloudOnBounds ? Matrix4x4.Translate(-latestCloudCenter) : Matrix4x4.identity;
        Matrix4x4 transformationMatrix = anchorMatrix * localOffsetMatrix * cloudScaleMatrix * centerTranslation;

        if (applyRosToUnityTransform)
        {
            Matrix4x4 rotationMatrix = Matrix4x4.Rotate(Quaternion.Euler(-90f, 90f, 0f));
            Matrix4x4 inversionMatrix = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));
            transformationMatrix = anchorMatrix * localOffsetMatrix * rotationMatrix * inversionMatrix * cloudScaleMatrix * centerTranslation;
        }

        renderParams.matProps.SetMatrix("_ObjectToWorld", transformationMatrix);

        Graphics.RenderPrimitivesIndexed(
            renderParams,
            MeshTopology.Triangles,
            meshTriangles,
            meshTriangles.count,
            0,
            currentPointCount);

        if (!loggedRenderState && enableBasicLogs)
        {
            loggedRenderState = true;
            Debug.Log($"[PointCloudSubscriberGPU] Render activo. points={currentPointCount}, pointSize={pointSize}, cloudScale={cloudScale}, centerCloudOnBounds={centerCloudOnBounds}, displayAllPoints={displayAllPoints}, anchor={(anchorTransform != null ? anchorTransform.name : transform.name)}, position={anchor.position}, material={(pointMaterial != null ? pointMaterial.name : "null")}, shader={(pointMaterial != null && pointMaterial.shader != null ? pointMaterial.shader.name : "null")}");
        }
    }

    private bool EnsurePointMaterial()
    {
        Shader resolvedShader = pointShader;

        if (resolvedShader == null)
        {
            resolvedShader = Shader.Find(PointShaderName);
        }

        if (resolvedShader == null)
        {
            Debug.LogError($"[PointCloudSubscriberGPU] No se encontro un shader utilizable. Asigna manualmente un shader compatible en el inspector o revisa '{PointShaderName}'.");
            return false;
        }

        if (pointMaterial == null)
        {
            runtimePointMaterial = new Material(resolvedShader)
            {
                name = "PointCloud Runtime Material"
            };
            pointMaterial = runtimePointMaterial;

            if (enableBasicLogs)
            {
                Debug.Log($"[PointCloudSubscriberGPU] No habia material asignado. Se creo uno runtime con shader '{resolvedShader.name}'.");
            }

            return true;
        }

        if (pointMaterial.shader == null || !string.Equals(pointMaterial.shader.name, resolvedShader.name, StringComparison.Ordinal))
        {
            string assignedShaderName = pointMaterial.shader != null ? pointMaterial.shader.name : "null";
            Debug.LogWarning($"[PointCloudSubscriberGPU] El material asignado '{pointMaterial.name}' usa '{assignedShaderName}', no '{resolvedShader.name}'. Se reemplaza por un material compatible para poder dibujar el point cloud.");
            runtimePointMaterial = new Material(resolvedShader)
            {
                name = "PointCloud Runtime Material"
            };
            pointMaterial = runtimePointMaterial;
        }

        return true;
    }

    private void TryInitializeSubscription()
    {
        if (ros2Unity == null)
        {
            if (enableBasicLogs)
            {
                Debug.LogWarning("[PointCloudSubscriberGPU] TryInitializeSubscription abortado: ros2Unity es null.");
            }
            return;
        }

        if (!ros2Unity.Ok())
        {
            if (!loggedRosWaiting || Time.time >= nextRosWaitLogTime)
            {
                Debug.Log($"[PointCloudSubscriberGPU] ROS2 no esta listo (Ok=false). Esperando... t={Time.time:F2}s");
                loggedRosWaiting = true;
                nextRosWaitLogTime = Time.time + 2f;
            }
            return;
        }

        if (loggedRosWaiting)
        {
            Debug.Log("[PointCloudSubscriberGPU] ROS2 listo. Creando nodo...");
            loggedRosWaiting = false;
        }

        try
        {
            string nodeName = "pointcloud_sub_gpu_" + UnityEngine.Random.Range(0, 10000);
            if (enableBasicLogs)
            {
                Debug.Log($"[PointCloudSubscriberGPU] Intentando CreateNode('{nodeName}')...");
            }

            ros2Node = ros2Unity.CreateNode(nodeName);

            if (ros2Node == null)
            {
                Debug.LogError("[PointCloudSubscriberGPU] CreateNode devolvio null.");
                return;
            }

            pointCloudSubscription = ros2Node.CreateSubscription<PointCloud2>(topicName, OnPointCloudReceived);
            if (pointCloudSubscription == null)
            {
                Debug.LogError($"[PointCloudSubscriberGPU] No se pudo crear la suscripcion a '{topicName}'.");
                return;
            }

            initialized = true;
            Debug.Log($"[PointCloudSubscriberGPU] Suscrito a '{topicName}'.");
            Debug.Log($"[PointCloudSubscriberGPU] Suscrito a '{topicName}'. Node='{nodeName}', visualizationEnabled={visualizationEnabled}, renderResourcesReady={renderResourcesReady}, material={(pointMaterial != null ? pointMaterial.name : "null")}, shader={(pointMaterial != null && pointMaterial.shader != null ? pointMaterial.shader.name : "null")}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PointCloudSubscriberGPU] Error creando suscripcion: {ex}");
        }
    }

    private bool CreateRenderResources()
    {
        if (enableBasicLogs)
        {
            Debug.Log($"[PointCloudSubscriberGPU] Creando recursos GPU. maxBufferPoints={maxBufferPoints}, displayPointLimit={displayPointLimit}, displayAllPoints={displayAllPoints}, pointSize={pointSize}, materialShader={(pointMaterial != null && pointMaterial.shader != null ? pointMaterial.shader.name : "null")}");
        }

        if (pointMaterial == null)
        {
            Debug.LogError("[PointCloudSubscriberGPU] Asigna un material con shader 'Unlit/ROS/Point'.");
            return false;
        }

        pointMesh = MakeQuadMesh();

        meshTriangles = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pointMesh.triangles.Length, sizeof(int));
        meshTriangles.SetData(pointMesh.triangles);

        meshVertices = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pointMesh.vertices.Length, 3 * sizeof(float));
        meshVertices.SetData(pointMesh.vertices);

        pointData = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(1, maxBufferPoints), BytesPerPoint);

        renderParams = new RenderParams(pointMaterial)
        {
            worldBounds = new Bounds(Vector3.zero, Vector3.one * 5000f),
            matProps = new MaterialPropertyBlock()
        };

        pointMaterial.DisableKeyword("COLOR_RGB");
        pointMaterial.DisableKeyword("COLOR_Z");
        pointMaterial.DisableKeyword("COLOR_INTENSITY");

        baseVertexIndex = (uint)pointMesh.GetBaseVertex(0);
        renderParams.matProps.SetBuffer("_PointData", pointData);
        renderParams.matProps.SetBuffer("_Positions", meshVertices);
        renderParams.matProps.SetInt("_BaseVertexIndex", (int)baseVertexIndex);
        renderParams.matProps.SetFloat("_PointSize", pointSize);
        Color renderColorMin = forceSolidWhiteColor ? Color.white : intensityMin;
        Color renderColorMax = forceSolidWhiteColor ? Color.white : intensityMax;
        renderParams.matProps.SetColor("_ColorMin", renderColorMin);
        renderParams.matProps.SetColor("_ColorMax", renderColorMax);
        renderParams.matProps.SetMatrix("_ObjectToWorld", Matrix4x4.identity);

        renderResourcesReady = true;

        if (enableBasicLogs)
        {
            Debug.Log($"[PointCloudSubscriberGPU] Recursos GPU listos. Triangles={meshTriangles.count}, Vertices={pointMesh.vertexCount}, BufferCapacityPts={Mathf.Max(1, maxBufferPoints)}, shader={(pointMaterial != null && pointMaterial.shader != null ? pointMaterial.shader.name : "null")}");
        }

        return true;
    }

    private void UploadPendingPointCloud()
    {
        byte[] dataToUpload = null;
        int pointCount = 0;

        lock (pendingLock)
        {
            if (pendingPointBytes == null || pendingPointCount <= 0)
            {
                return;
            }

            dataToUpload = pendingPointBytes;
            pointCount = pendingPointCount;
            pendingPointBytes = null;
            pendingPointCount = 0;
        }

        if (!renderResourcesReady || dataToUpload == null || pointCount <= 0)
        {
            currentPointCount = 0;
            return;
        }

        pointData.SetData(dataToUpload);
        currentPointCount = pointCount;
        uploadCount++;

        if (!loggedFirstUpload)
        {
            loggedFirstUpload = true;
            Debug.Log($"[PointCloudSubscriberGPU] Primer upload a GPU realizado. points={currentPointCount}, bytes={dataToUpload.Length}, time={Time.time:F3}");
        }

        if (verboseLogs && Time.time >= nextVerboseUploadLogTime)
        {
            Debug.Log($"[PointCloudSubscriberGPU] Upload GPU listo. uploadCount={uploadCount}, currentPointCount={currentPointCount}, bytes={dataToUpload.Length}, pointDataNull={(pointData == null)}, pendingCleared={(pendingPointBytes == null)}");

            Debug.Log($"[PointCloudSubscriberGPU] Upload GPU #{uploadCount}: puntos={currentPointCount}, bytes={dataToUpload.Length}");
            nextVerboseUploadLogTime = Time.time + 1.0f;
        }
    }

    private void OnPointCloudReceived(PointCloud2 message)
    {
        if (message == null || message.Data == null || message.Data.Length == 0 || message.Fields == null)
        {
            if (verboseLogs)
            {
                Debug.LogWarning($"[PointCloudSubscriberGPU] PointCloud2 invalido. messageNull={message == null}, dataNull={(message == null || message.Data == null)}, dataLen={(message != null && message.Data != null ? message.Data.Length : 0)}, fieldsNull={(message == null || message.Fields == null)}");
            }

            return;
        }

        receivedMessageCount++;
        if (enableBasicLogs && receivedMessageCount == 1)
        {
            Debug.Log($"[PointCloudSubscriberGPU] Primer PointCloud2 recibido en topic '{topicName}'. Bytes={message.Data.Length}, point_step={message.Point_step}");
        }
        else if (verboseLogs && receivedMessageCount % 30 == 0)
        {
            Debug.Log($"[PointCloudSubscriberGPU] Mensajes recibidos: {receivedMessageCount}");
        }

        int safeLimit = displayAllPoints ? Mathf.Max(1, maxBufferPoints) : Mathf.Clamp(displayPointLimit, 1, Mathf.Max(1, maxBufferPoints));

        if (verboseLogs && receivedMessageCount % 30 == 1)
        {
            Debug.Log($"[PointCloudSubscriberGPU] Recibido PointCloud2. topic='{topicName}', width={message.Width}, height={message.Height}, point_step={message.Point_step}, row_step={message.Row_step}, dataLen={message.Data.Length}, fields={message.Fields.Length}, safeLimit={safeLimit}, enabled={enabled}, activeInHierarchy={gameObject.activeInHierarchy}, isActiveAndEnabled={isActiveAndEnabled}");
        }

        byte[] packedData = ExtractXYZI(message, safeLimit, out int sampledPointCount, out int totalPoints, out int decimator, out bool hadColorField, out Vector3 minPoint, out Vector3 maxPoint);
        if (packedData == null || sampledPointCount <= 0)
        {
            if (enableBasicLogs || verboseLogs)
            {
                Debug.LogWarning("[PointCloudSubscriberGPU] ExtractXYZI devolvio vacio. Revisar fields/point_step/data.");
            }
            return;
        }

        if (verboseLogs && receivedMessageCount % 30 == 1)
        {
            Debug.Log($"[PointCloudSubscriberGPU] Msg stats: totalPoints={totalPoints}, sampled={sampledPointCount}, decimator={decimator}, colorField={(hadColorField ? "si" : "no")}, width={message.Width}, height={message.Height}");
        }

        if (enableBasicLogs && Time.time >= nextBoundsLogTime)
        {
            Vector3 center = (minPoint + maxPoint) * 0.5f;
            Vector3 size = maxPoint - minPoint;
            Debug.Log($"[PointCloudSubscriberGPU] Bounds cloud: min={minPoint}, max={maxPoint}, center={center}, size={size}");
            nextBoundsLogTime = Time.time + 5f;
        }

        latestCloudCenter = (minPoint + maxPoint) * 0.5f;

        if (verboseLogs)
        {
            Debug.Log($"[PointCloudSubscriberGPU] Nube lista para GPU. totalPoints={totalPoints}, sampled={sampledPointCount}, decimator={decimator}, colorField={(hadColorField ? "si" : "no")}, min={minPoint}, max={maxPoint}");
        }

        lock (pendingLock)
        {
            pendingPointBytes = packedData;
            pendingPointCount = sampledPointCount;
        }
    }

    private static byte[] ExtractXYZI(PointCloud2 cloud, int maxPoints, out int sampledPointCount, out int totalPoints, out int decimator, out bool hadColorField, out Vector3 minPoint, out Vector3 maxPoint)
    {
        sampledPointCount = 0;
        totalPoints = 0;
        decimator = 1;
        hadColorField = false;
        minPoint = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        maxPoint = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

        if (cloud.Point_step == 0 || cloud.Data == null || cloud.Data.Length == 0)
        {
            return null;
        }

        totalPoints = cloud.Data.Length / (int)cloud.Point_step;
        if (totalPoints <= 0)
        {
            return null;
        }

        int xOffset = GetFieldOffset(cloud.Fields, "x");
        int yOffset = GetFieldOffset(cloud.Fields, "y");
        int zOffset = GetFieldOffset(cloud.Fields, "z");
        int intensityOffset = GetFieldOffset(cloud.Fields, "intensity");
        int rgbOffset = GetFieldOffset(cloud.Fields, "rgb");

        if (xOffset < 0 || yOffset < 0 || zOffset < 0)
        {
            Debug.LogWarning($"[PointCloudSubscriberGPU] Faltan fields XYZ en PointCloud2. x={xOffset}, y={yOffset}, z={zOffset}");
            return null;
        }

        int colorOffset = intensityOffset >= 0 ? intensityOffset : rgbOffset;
        hadColorField = colorOffset >= 0;

        sampledPointCount = totalPoints;
        if (sampledPointCount > maxPoints)
        {
            decimator = Mathf.CeilToInt((float)sampledPointCount / maxPoints);
            sampledPointCount /= decimator;
        }

        if (sampledPointCount <= 0)
        {
            return null;
        }

        byte[] output = new byte[sampledPointCount * BytesPerPoint];
        int pointStep = (int)cloud.Point_step;

        byte[] defaultIntensity = BitConverter.GetBytes(1f);

        for (int i = 0; i < sampledPointCount; i++)
        {
            int inputPointIndex = i * decimator;
            int inputBase = inputPointIndex * pointStep;
            int outputBase = i * BytesPerPoint;

            float x = BitConverter.ToSingle(cloud.Data, inputBase + xOffset);
            float y = BitConverter.ToSingle(cloud.Data, inputBase + yOffset);
            float z = BitConverter.ToSingle(cloud.Data, inputBase + zOffset);

            if (x < minPoint.x) minPoint.x = x;
            if (y < minPoint.y) minPoint.y = y;
            if (z < minPoint.z) minPoint.z = z;
            if (x > maxPoint.x) maxPoint.x = x;
            if (y > maxPoint.y) maxPoint.y = y;
            if (z > maxPoint.z) maxPoint.z = z;

            Buffer.BlockCopy(cloud.Data, inputBase + xOffset, output, outputBase + 0, 4);
            Buffer.BlockCopy(cloud.Data, inputBase + yOffset, output, outputBase + 4, 4);
            Buffer.BlockCopy(cloud.Data, inputBase + zOffset, output, outputBase + 8, 4);

            if (colorOffset >= 0)
            {
                Buffer.BlockCopy(cloud.Data, inputBase + colorOffset, output, outputBase + 12, 4);
            }
            else
            {
                Buffer.BlockCopy(defaultIntensity, 0, output, outputBase + 12, 4);
            }
        }

        return output;
    }

    private static int GetFieldOffset(PointField[] fields, string fieldName)
    {
        for (int i = 0; i < fields.Length; i++)
        {
            if (string.Equals(fields[i].Name, fieldName, StringComparison.OrdinalIgnoreCase))
            {
                return (int)fields[i].Offset;
            }
        }

        return -1;
    }

    private static Mesh MakeQuadMesh()
    {
        Mesh mesh = new Mesh();
        Vector3[] vertices = new Vector3[4];
        int[] triangles = new int[6];

        vertices[0] = new Vector3(-1f, -1f, 0f);
        vertices[1] = new Vector3(1f, -1f, 0f);
        vertices[2] = new Vector3(-1f, 1f, 0f);
        vertices[3] = new Vector3(1f, 1f, 0f);

        triangles[0] = 0;
        triangles[1] = 2;
        triangles[2] = 1;
        triangles[3] = 1;
        triangles[4] = 2;
        triangles[5] = 3;

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.UploadMeshData(false);
        return mesh;
    }

    public void SetVisualizationEnabled(bool value)
    {
        visualizationEnabled = value;
        UpdateIconFeedback();

        if (enableBasicLogs)
        {
            Debug.Log($"[PointCloudSubscriberGPU] visualizationEnabled={visualizationEnabled}");
        }
    }

    public void ToggleVisualization()
    {
        visualizationEnabled = !visualizationEnabled;
        UpdateIconFeedback();
        if (enableBasicLogs)
        {
            Debug.Log($"[PointCloudSubscriberGPU] visualizationEnabled={visualizationEnabled}");
        }
    }

    // Metodo auxiliar para enlazar botones que reutilizan el patron de otros scripts.
    public void SetActivation()
    {
        ToggleVisualization();
    }

    // Sobrecarga para enlazar Toggle UI (OnValueChanged bool).
    public void SetActivation(bool active)
    {
        SetVisualizationEnabled(active);
    }

    private void UpdateIconFeedback()
    {
        if (iconFeedback != null)
        {
            iconFeedback.UpdateIcon(visualizationEnabled);
        }
    }

    private void OnValidate()
    {
        if (displayPointLimit < 1)
        {
            displayPointLimit = 1;
        }

        if (maxBufferPoints < 1)
        {
            maxBufferPoints = 1;
        }

        if (displayPointLimit > maxBufferPoints)
        {
            displayPointLimit = maxBufferPoints;
        }

        if (anchorTransform == null)
        {
            anchorTransform = transform;
        }

        if (renderParams.matProps != null)
        {
            renderParams.matProps.SetFloat("_PointSize", pointSize);
            Color renderColorMin = forceSolidWhiteColor ? Color.white : intensityMin;
            Color renderColorMax = forceSolidWhiteColor ? Color.white : intensityMax;
            renderParams.matProps.SetColor("_ColorMin", renderColorMin);
            renderParams.matProps.SetColor("_ColorMax", renderColorMax);

            if (pointMaterial != null)
            {
                pointMaterial.DisableKeyword("COLOR_RGB");
                pointMaterial.DisableKeyword("COLOR_Z");
                pointMaterial.DisableKeyword("COLOR_INTENSITY");
            }
        }
    }

    private void OnDestroy()
    {
        meshTriangles?.Dispose();
        meshTriangles = null;

        meshVertices?.Dispose();
        meshVertices = null;

        pointData?.Dispose();
        pointData = null;

        if (runtimePointMaterial != null)
        {
            Destroy(runtimePointMaterial);
            runtimePointMaterial = null;
        }

        if (pointCloudSubscription != null && ros2Node != null)
        {
            ros2Node.RemoveSubscription<PointCloud2>(pointCloudSubscription);
            pointCloudSubscription = null;
        }
    }
}
