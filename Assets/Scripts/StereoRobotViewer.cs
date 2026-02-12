using ROS2;
using sensor_msgs.msg;
using UnityEngine;
using System.Collections.Concurrent;

public class StereoRobotViewer : MonoBehaviour
{
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;

    [Header("Configuración de ROS 2")]
    public string leftTopic = "/camera/left/compressed";
    public string rightTopic = "/camera/right/compressed";

    [Header("Referencias de Escena")]
    public MeshRenderer displayQuad; // Arrastra aquí el Quad con el Shader Estéreo

    // Colas para las imágenes de cada ojo
    private ConcurrentQueue<byte[]> leftImageQueue = new ConcurrentQueue<byte[]>();
    private ConcurrentQueue<byte[]> rightImageQueue = new ConcurrentQueue<byte[]>();

    // Texturas y Material
    private Texture2D leftTexture;
    private Texture2D rightTexture;
    private Material stereoMaterial;

    private ISubscription<CompressedImage> subLeftImage;
    private ISubscription<CompressedImage> subRightImage;

    void Start()
    {
        ros2Unity = GetComponent<ROS2UnityComponent>();

        // Inicializamos las texturas (LoadImage las redimensionará automáticamente)
        leftTexture = new Texture2D(2, 2);
        rightTexture = new Texture2D(2, 2);

        if (displayQuad != null)
            stereoMaterial = displayQuad.material;
    }

    void Update()
    {
        // 1. Inicializar el nodo si ROS está listo
        if (ros2Node == null && ros2Unity.Ok())
        {
            ros2Node = ros2Unity.CreateNode("stereo_vision_node");

            subLeftImage = ros2Node.CreateSubscription<CompressedImage>(
                leftTopic, msg => EnqueueImage(msg, leftImageQueue));

            subRightImage = ros2Node.CreateSubscription<CompressedImage>(
                rightTopic, msg => EnqueueImage(msg, rightImageQueue));
        }

        // 2. Procesar las colas de imágenes
        ProcessQueues();
    }

    private void EnqueueImage(CompressedImage msg, ConcurrentQueue<byte[]> queue)
    {
        // Mantenemos solo la imagen más fresca (Low Latency)
        while (queue.Count >= 1) queue.TryDequeue(out _);
        queue.Enqueue(msg.Data);
    }

    private void ProcessQueues()
    {
        if (stereoMaterial == null) return;

        // Ojo Izquierdo
        if (leftImageQueue.TryDequeue(out byte[] leftData))
        {
            if (leftTexture.LoadImage(leftData))
            {
                stereoMaterial.SetTexture("_LeftEyeTex", leftTexture);
            }
        }

        // Ojo Derecho
        if (rightImageQueue.TryDequeue(out byte[] rightData))
        {
            if (rightTexture.LoadImage(rightData))
            {
                stereoMaterial.SetTexture("_RightEyeTex", rightTexture);
            }
        }
    }

    public void Destroy()
    {
        if (ros2Node != null) ros2Unity.RemoveNode(ros2Node);
    }
}