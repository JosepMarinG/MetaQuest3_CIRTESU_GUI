using ROS2;
using sensor_msgs.msg;
using System.Collections.Concurrent;
using UnityEngine;

public class Stereo_OVR_SBS : MonoBehaviour
{
    private ROS2UnityComponent ros2Unity;
    public ROS2Node ros2Node;

    [Header("Configuración ROS2")]
    public string cameraTopic = "/camera/stereo/compressed";
    [SerializeField] private string nodeNamePrefix = "stereo_ovr_sbs";

    [Header("OVR Overlay")]
    public OVROverlay overlay;
    public OVROverlay.OverlayType overlayType = OVROverlay.OverlayType.Overlay;
    public OVROverlay.OverlayShape overlayShape = OVROverlay.OverlayShape.Quad;
    public bool overlayIsDynamic = true;
    public bool overrideTextureRectMatrix = true;
    public bool invertTextureRects = false;

    [Header("Rects SBS")]
    public Rect srcRectLeft = new Rect(0f, 0f, 0.5f, 1f);
    public Rect srcRectRight = new Rect(0.5f, 0f, 0.5f, 1f);
    public Rect destRectLeft = new Rect(0f, 0f, 1f, 1f);
    public Rect destRectRight = new Rect(0f, 0f, 1f, 1f);

    private readonly ConcurrentQueue<byte[]> imageQueue = new ConcurrentQueue<byte[]>();
    private ISubscription<CompressedImage> subImage;
    private Texture2D sbsTexture;

    private void Start()
    {
        ros2Unity = Object.FindAnyObjectByType<ROS2UnityComponent>();
        sbsTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

        if (overlay == null)
        {
            overlay = GetComponent<OVROverlay>();
        }

        if (overlay == null)
        {
            overlay = gameObject.AddComponent<OVROverlay>();
        }

        ConfigureOverlay();
    }

    private void Update()
    {
        if (ros2Node == null && ros2Unity != null && ros2Unity.Ok())
        {
            string nodeName = nodeNamePrefix + "_" + gameObject.name.Replace(" ", "_") + "_" + Random.Range(0, 1000);
            ros2Node = ros2Unity.CreateNode(nodeName);
            SubscribeToTopic();
        }

        ProcessQueue();
    }

    private void ConfigureOverlay()
    {
        if (overlay == null)
        {
            return;
        }

        if (!overlay.enabled)
        {
            overlay.enabled = true;
        }

        overlay.currentOverlayType = overlayType;
        overlay.currentOverlayShape = overlayShape;
        overlay.isDynamic = overlayIsDynamic;
        overlay.overrideTextureRectMatrix = overrideTextureRectMatrix;
        overlay.invertTextureRects = invertTextureRects;
        overlay.SetSrcDestRects(srcRectLeft, srcRectRight, destRectLeft, destRectRight);

        if (overlay.overrideTextureRectMatrix)
        {
            overlay.UpdateTextureRectMatrix();
        }

        if (overlay.textures == null || overlay.textures.Length < 2)
        {
            overlay.textures = new Texture[] { null, null };
        }

        if (sbsTexture != null)
        {
            overlay.textures[0] = sbsTexture;
            overlay.textures[1] = null;
        }
    }

    private void SubscribeToTopic()
    {
        subImage = ros2Node.CreateSubscription<CompressedImage>(
            cameraTopic,
            msg =>
            {
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

        if (!sbsTexture.LoadImage(frameData))
        {
            return;
        }

        if (overlay == null)
        {
            return;
        }

        overlay.textures[0] = sbsTexture;

        if (overlay.textures.Length > 1)
        {
            overlay.textures[1] = null;
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
            Debug.Log("[Stereo_OVR_SBS] Suscrito a nuevo topic: " + newTopic);
        }
    }

    public void ApplySbsRects()
    {
        ConfigureOverlay();
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

        if (sbsTexture != null)
        {
            Destroy(sbsTexture);
            sbsTexture = null;
        }
    }
}
