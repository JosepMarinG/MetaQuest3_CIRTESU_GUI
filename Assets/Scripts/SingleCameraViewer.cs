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

    // Buffer management
    private byte[] currentFrameData;
    private byte[] nextFrameData;
    private object bufferLock = new object();
    private bool newFrameAvailable = false;
    
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

        // Thread-safe frame update
        if (newFrameAvailable && displayImage != null)
        {
            lock (bufferLock)
            {
                if (currentFrameData != null && texture.LoadImage(currentFrameData))
                {
                    displayImage.texture = texture;
                }
                newFrameAvailable = false;
            }
        }
    }

    private void SubscribeToTopic()
    {
        subImage = ros2Node.CreateSubscription<CompressedImage>(
            cameraTopic, msg => {
                // Thread-safe buffer update
                lock (bufferLock)
                {
                    currentFrameData = msg.Data;
                    newFrameAvailable = true;
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
    }
}