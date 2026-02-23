using ROS2;
using sensor_msgs.msg;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Concurrent;

public class SingleCameraViewer : MonoBehaviour
{
    private ROS2UnityComponent ros2Unity;
    public ROS2Node ros2Node;

    [Header("Configuraci�n")]
    public string cameraTopic = "/camera/left/compressed";
    public RawImage displayImage;
    public float displayRate = 5f; // Hz - Limitar a 5 FPS para ahorrar memoria

    // Buffer management
    private byte[] currentFrameData;
    private object bufferLock = new object();
    private bool newFrameAvailable = false;
    private float displayTimer = 0f;
    private int framesReceived = 0;
    private int framesDisplayed = 0;
    private int gcCounter = 0;
    private const int GC_CALL_INTERVAL = 100; // Llamar GC cada 100 frames recibidos
    
    private Texture2D texture;
    private ISubscription<CompressedImage> subImage;

    void Start()
    {
        ros2Unity = Object.FindAnyObjectByType<ROS2UnityComponent>();
        texture = new Texture2D(2, 2);
        if (displayImage != null) displayImage.texture = texture;
    }

    void Update()
    {
        if (ros2Node == null && ros2Unity != null && ros2Unity.Ok())
        {
            string nodeName = "viewer_" + gameObject.name.Replace(" ", "_") + "_" + Random.Range(0, 1000);
            ros2Node = ros2Unity.CreateNode(nodeName);
            SubscribeToTopic();
        }

        displayTimer += Time.deltaTime;
        
        // Solo actualizar la textura a la frecuencia especificada (5 Hz por defecto)
        if (displayTimer >= 1f / displayRate && newFrameAvailable && displayImage != null)
        {
            lock (bufferLock)
            {
                if (currentFrameData != null && currentFrameData.Length > 0)
                {
                    // Reutilizar la textura existente para evitar crear objetos nuevos
                    if (texture.LoadImage(currentFrameData))
                    {
                        displayImage.texture = texture;
                        framesDisplayed++;
                    }
                    
                    // Liberar referencia al buffer para permitir GC
                    currentFrameData = null;
                }
                newFrameAvailable = false;
            }
            displayTimer = 0f;
        }
    }

    private void SubscribeToTopic()
    {
        subImage = ros2Node.CreateSubscription<CompressedImage>(
            cameraTopic, msg => {
                framesReceived++;
                
                // Descartar frame si aún hay uno pendiente de mostrar (throttling)
                lock (bufferLock)
                {
                    if (!newFrameAvailable)
                    {
                        currentFrameData = msg.Data;
                        newFrameAvailable = true;
                    }
                    // Si newFrameAvailable es true, descartamos este frame
                }
                
                // Llamar al GC periódicamente para limpiar memoria
                gcCounter++;
                if (gcCounter >= GC_CALL_INTERVAL)
                {
                    System.GC.Collect();
                    gcCounter = 0;
                    Debug.Log($"[Camera] GC llamado. Frames: Recibidos={framesReceived}, Mostrados={framesDisplayed}");
                }
            });
    }

    public void ChangeTopic(string newTopic)
    {
        if (string.IsNullOrEmpty(newTopic) || newTopic == cameraTopic) return;

        if (subImage != null && ros2Node != null)
            ros2Node.RemoveSubscription<CompressedImage>(subImage);

        cameraTopic = newTopic;
        SubscribeToTopic();
        Debug.Log("[ROS2] Suscrito a nuevo topic: " + newTopic);
    }

    // El cierre de la suscripci�n se hace autom�ticamente aqu� al destruir el panel
    private void OnDestroy()
    {
        if (ros2Node != null && ros2Unity != null)
        {
            ros2Unity.RemoveNode(ros2Node);
        }        
        // Limpiar recursos de textura
        if (texture != null)
        {
            Destroy(texture);
            texture = null;
        }
        
        // Limpiar buffers
        lock (bufferLock)
        {
            currentFrameData = null;
        }
        
        // Forzar limpieza de memoria al destruir
        System.GC.Collect();    }
}