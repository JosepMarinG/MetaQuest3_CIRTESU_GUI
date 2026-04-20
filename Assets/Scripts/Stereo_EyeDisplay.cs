using ROS2;
using sensor_msgs.msg;
using System.Collections.Concurrent;
using UnityEngine;

public class Stereo_EyeDisplay : MonoBehaviour
{
    [Header("Estado")]
    public bool isActivated = true;

    private ROS2UnityComponent ros2Unity;
    public ROS2Node ros2Node;

    [Header("Configuración ROS2")]
    public string cameraTopic = "/camera/stereo/compressed";
    [SerializeField] private string nodeNamePrefix = "stereo_eye_display";

    [Header("Eye Cameras")]
    public Camera leftEyeCamera;
    public Camera rightEyeCamera;

    [Header("Material & Display")]
    public Material eyeDisplayMaterial;
    public bool useBackgroundTexture = true;

    [Header("UI Feedback")]
    public ToggleIconFeedback iconFeedback;

    private readonly ConcurrentQueue<byte[]> imageQueue = new ConcurrentQueue<byte[]>();
    private ISubscription<CompressedImage> subImage;
    
    private Texture2D leftEyeTexture;
    private Texture2D rightEyeTexture;
    private Texture2D sbsTexture;

    private void Start()
    {
        ros2Unity = Object.FindAnyObjectByType<ROS2UnityComponent>();
        
        // Inicializar texturas
        sbsTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        leftEyeTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        rightEyeTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

        // Obtener cámaras de ojos si no están asignadas
        if (leftEyeCamera == null || rightEyeCamera == null)
        {
            OVRCameraRig cameraRig = FindAnyObjectByType<OVRCameraRig>();
            if (cameraRig != null)
            {
                leftEyeCamera = cameraRig.leftEyeCamera;
                rightEyeCamera = cameraRig.rightEyeCamera;
            }
        }

        // Crear material si no está asignado
        if (eyeDisplayMaterial == null)
        {
            eyeDisplayMaterial = new Material(Shader.Find("Unlit/Texture"));
        }

        if (iconFeedback != null)
        {
            iconFeedback.UpdateIcon(isActivated);
        }

        ApplyTexturesToEyes();
    }

    private void Update()
    {
        if (!isActivated)
            return;

        if (ros2Node == null && ros2Unity != null && ros2Unity.Ok())
        {
            string nodeName = nodeNamePrefix + "_" + gameObject.name.Replace(" ", "_") + "_" + Random.Range(0, 1000);
            ros2Node = ros2Unity.CreateNode(nodeName);
            SubscribeToTopic();
        }

        ProcessQueue();
    }

    private void SubscribeToTopic()
    {
        subImage = ros2Node.CreateSubscription<CompressedImage>(
            cameraTopic,
            msg =>
            {
                // Mantener solo el mensaje más reciente
                while (imageQueue.Count >= 1)
                {
                    imageQueue.TryDequeue(out _);
                }

                imageQueue.Enqueue(msg.Data);
            });
    }

    private void ProcessQueue()
    {
        if (!imageQueue.TryDequeue(out byte[] frameData) || frameData == null || frameData.Length == 0)
        {
            return;
        }

        // Cargar la imagen completa (side-by-side)
        if (!sbsTexture.LoadImage(frameData))
        {
            return;
        }

        // Dividir en izquierda y derecha
        SplitSBSTexture(sbsTexture, leftEyeTexture, rightEyeTexture);

        // Aplicar texturas a los ojos
        ApplyTexturesToEyes();
    }

    private void SplitSBSTexture(Texture2D sourceTexture, Texture2D leftTexture, Texture2D rightTexture)
    {
        int width = sourceTexture.width;
        int height = sourceTexture.height;
        int halfWidth = width / 2;

        // Redimensionar texturas si es necesario
        if (leftTexture.width != halfWidth || leftTexture.height != height)
        {
            leftTexture.Reinitialize(halfWidth, height, TextureFormat.RGBA32, false);
        }

        if (rightTexture.width != halfWidth || rightTexture.height != height)
        {
            rightTexture.Reinitialize(halfWidth, height, TextureFormat.RGBA32, false);
        }

        // Obtener los píxeles de la imagen original
        Color[] srcPixels = sourceTexture.GetPixels();
        Color[] leftPixels = new Color[halfWidth * height];
        Color[] rightPixels = new Color[halfWidth * height];

        // Dividir los píxeles
        for (int y = 0; y < height; y++)
        {
            // Lado izquierdo (0 a halfWidth)
            System.Array.Copy(srcPixels, y * width, leftPixels, y * halfWidth, halfWidth);
            
            // Lado derecho (halfWidth a width)
            System.Array.Copy(srcPixels, y * width + halfWidth, rightPixels, y * halfWidth, halfWidth);
        }

        leftTexture.SetPixels(leftPixels);
        leftTexture.Apply();

        rightTexture.SetPixels(rightPixels);
        rightTexture.Apply();
    }

    private void ApplyTexturesToEyes()
    {
        if (useBackgroundTexture)
        {
            // Aplicar como texturas de fondo de las cámaras
            if (leftEyeCamera != null && leftEyeTexture != null)
            {
                Texture[] textures = new Texture[] { leftEyeTexture };
                OVROverlay leftOverlay = leftEyeCamera.GetComponent<OVROverlay>();
                if (leftOverlay == null)
                {
                    leftOverlay = leftEyeCamera.gameObject.AddComponent<OVROverlay>();
                }
                leftOverlay.textures = textures;
            }

            if (rightEyeCamera != null && rightEyeTexture != null)
            {
                Texture[] textures = new Texture[] { rightEyeTexture };
                OVROverlay rightOverlay = rightEyeCamera.GetComponent<OVROverlay>();
                if (rightOverlay == null)
                {
                    rightOverlay = rightEyeCamera.gameObject.AddComponent<OVROverlay>();
                }
                rightOverlay.textures = textures;
            }
        }
        else
        {
            // Aplicar como material en las cámaras
            if (eyeDisplayMaterial != null)
            {
                Material leftMat = new Material(eyeDisplayMaterial);
                Material rightMat = new Material(eyeDisplayMaterial);

                leftMat.mainTexture = leftEyeTexture;
                rightMat.mainTexture = rightEyeTexture;

                if (leftEyeCamera != null)
                {
                    leftEyeCamera.GetComponent<Renderer>().material = leftMat;
                }

                if (rightEyeCamera != null)
                {
                    rightEyeCamera.GetComponent<Renderer>().material = rightMat;
                }
            }
        }
    }

    public void ChangeTopic(string newTopic)
    {
        if (string.IsNullOrEmpty(newTopic) || newTopic == cameraTopic)
        {
            return;
        }

        if (subImage != null && ros2Node != null)
        {
            ros2Node.RemoveSubscription<CompressedImage>(subImage);
            subImage = null;
        }

        cameraTopic = newTopic;

        if (ros2Node != null)
        {
            SubscribeToTopic();
            Debug.Log("[Stereo_EyeDisplay] Suscrito a nuevo topic: " + newTopic);
        }
    }

    public void SetActivation()
    {
        isActivated = !isActivated;

        if (iconFeedback != null)
        {
            iconFeedback.UpdateIcon(isActivated);
        }

        Debug.Log($"[Stereo_EyeDisplay] Visualización estéreo {(isActivated ? "ACTIVA" : "INACTIVA")}");
    }

    public void SetActivation(bool active)
    {
        if (isActivated == active)
        {
            return;
        }

        SetActivation();
    }

    private void OnDestroy()
    {
        if (subImage != null && ros2Node != null)
        {
            ros2Node.RemoveSubscription<CompressedImage>(subImage);
            subImage = null;
        }

        if (ros2Node != null && ros2Unity != null)
        {
            ros2Unity.RemoveNode(ros2Node);
            ros2Node = null;
        }

        while (imageQueue.TryDequeue(out _))
        {
        }

        // Limpiar texturas
        if (leftEyeTexture != null)
        {
            Destroy(leftEyeTexture);
            leftEyeTexture = null;
        }

        if (rightEyeTexture != null)
        {
            Destroy(rightEyeTexture);
            rightEyeTexture = null;
        }

        if (sbsTexture != null)
        {
            Destroy(sbsTexture);
            sbsTexture = null;
        }
    }
}
