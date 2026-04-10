using UnityEngine;
using UnityEngine.Rendering;

public class TestSinglePoint : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool renderingEnabled = true;
    [SerializeField, Min(0.001f)] private float pointSize = 0.05f;
    [SerializeField] private Color pointColor = Color.white;
    [SerializeField] private bool verboseLogs = true;
    [SerializeField] private Shader pointShader;
    [SerializeField] private bool useSubscriberTransform = true;
    [SerializeField] private bool followCameraPosition = false;
    [SerializeField, Min(0.1f)] private float distanceFromCamera = 2.0f;
    [SerializeField] private Vector3 worldPosition = Vector3.zero;

    private Material pointMaterial;
    private GraphicsBuffer meshTriangles;
    private GraphicsBuffer meshVertices;
    private GraphicsBuffer pointData;
    private RenderParams renderParams;
    private int triangleCount;

    private bool initialized;
    private float nextLogTime = 0f;

    private void Start()
    {
        Debug.Log("[TestSinglePoint] Start. Inicializando punto de prueba...");
        try
        {
            InitializeRender();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[TestSinglePoint] Excepcion durante InitializeRender: {ex}");
        }
    }

    private void InitializeRender()
    {
        Shader shader = pointShader;

        if (shader == null)
        {
            shader = Shader.Find("Unlit/ROS/Point");
        }

        Debug.Log($"[TestSinglePoint] Shader source => {(pointShader != null ? "inspector" : "Shader.Find" )}, shader={(shader != null ? shader.name : "null")}");
        if (shader == null)
        {
            Debug.LogError("[TestSinglePoint] No se encontro el shader 'Unlit/ROS/Point'. Arrastralo en pointShader desde el inspector.");
            return;
        }

        pointMaterial = new Material(shader);
        pointMaterial.name = "TestSinglePoint Material";
        pointMaterial.DisableKeyword("COLOR_RGB");
        pointMaterial.DisableKeyword("COLOR_Z");
        pointMaterial.EnableKeyword("COLOR_INTENSITY");

        InitializePointBuffers();

        initialized = true;
        Debug.Log($"[TestSinglePoint] Inicializacion completa. Pipeline=RenderPrimitivesIndexed, size={pointSize}, color={pointColor}");
    }

    private void InitializePointBuffers()
    {
        // Mismo quad base que usa PointCloudSubscriberGPU
        Vector3[] vertices = new Vector3[4];
        vertices[0] = new Vector3(-1f, -1f, 0f);
        vertices[1] = new Vector3(1f, -1f, 0f);
        vertices[2] = new Vector3(-1f, 1f, 0f);
        vertices[3] = new Vector3(1f, 1f, 0f);

        int[] triangles = new int[6] { 0, 2, 1, 1, 2, 3 };

        meshTriangles = new GraphicsBuffer(GraphicsBuffer.Target.Structured, triangles.Length, sizeof(int));
        meshTriangles.SetData(triangles);

        meshVertices = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vertices.Length, 3 * sizeof(float));
        meshVertices.SetData(vertices);

        byte[] pointDataBytes = new byte[16];
        System.BitConverter.GetBytes(0f).CopyTo(pointDataBytes, 0);
        System.BitConverter.GetBytes(0f).CopyTo(pointDataBytes, 4);
        System.BitConverter.GetBytes(0f).CopyTo(pointDataBytes, 8);
        System.BitConverter.GetBytes(1f).CopyTo(pointDataBytes, 12);

        pointData = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 16);
        pointData.SetData(pointDataBytes);

        renderParams = new RenderParams(pointMaterial)
        {
            worldBounds = new Bounds(Vector3.zero, Vector3.one * 5000f),
            matProps = new MaterialPropertyBlock()
        };

        renderParams.matProps.SetBuffer("_PointData", pointData);
        renderParams.matProps.SetBuffer("_Positions", meshVertices);
        renderParams.matProps.SetInt("_BaseVertexIndex", 0);
        renderParams.matProps.SetFloat("_PointSize", pointSize);
        renderParams.matProps.SetColor("_ColorMin", pointColor);
        renderParams.matProps.SetColor("_ColorMax", pointColor);
        triangleCount = triangles.Length;

        Debug.Log($"[TestSinglePoint] Buffers listos. triCount={triangleCount}, vertexCount={vertices.Length}, pointCount=1");
    }

    private void Update()
    {
        if (!renderingEnabled || !initialized)
        {
            if (verboseLogs && Time.time >= nextLogTime)
            {
                Debug.Log($"[TestSinglePoint] Renderizado desactivado (renderingEnabled={renderingEnabled}, initialized={initialized})", this);
                nextLogTime = Time.time + 2f;
            }
            return;
        }

        renderParams.matProps.SetFloat("_PointSize", pointSize);
        renderParams.matProps.SetColor("_ColorMin", pointColor);
        renderParams.matProps.SetColor("_ColorMax", pointColor);

        Vector3 drawPosition = worldPosition;

        if (followCameraPosition && Camera.main != null)
        {
            Transform cam = Camera.main.transform;
            drawPosition = cam.position + cam.forward * distanceFromCamera;
        }

        Matrix4x4 localToWorldMatrix = Matrix4x4.TRS(drawPosition, Quaternion.identity, Vector3.one);
        Matrix4x4 transformationMatrix = localToWorldMatrix;

        if (useSubscriberTransform)
        {
            Matrix4x4 rotationMatrix = Matrix4x4.Rotate(Quaternion.Euler(-90f, 90f, 0f));
            Matrix4x4 inversionMatrix = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));
            transformationMatrix = localToWorldMatrix * rotationMatrix * inversionMatrix;
        }

        renderParams.matProps.SetMatrix("_ObjectToWorld", transformationMatrix);

        Graphics.RenderPrimitivesIndexed(
            renderParams,
            MeshTopology.Triangles,
            meshTriangles,
            triangleCount,
            0,
            1);

        if (verboseLogs && Time.time >= nextLogTime)
        {
            Debug.Log($"[TestSinglePoint] Dibujando. size={pointSize}, color={pointColor}, drawPos={drawPosition}, useSubscriberTransform={useSubscriberTransform}, followCameraPosition={followCameraPosition}, camera={(Camera.main != null ? Camera.main.name : "null")}", this);
            nextLogTime = Time.time + 2f;
        }
    }

    private void OnDestroy()
    {
        if (pointMaterial != null)
        {
            Destroy(pointMaterial);
        }

        meshTriangles?.Dispose();
        meshTriangles = null;

        meshVertices?.Dispose();
        meshVertices = null;

        pointData?.Dispose();
        pointData = null;
    }
}
