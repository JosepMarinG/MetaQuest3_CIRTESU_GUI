using ROS2;
using sensor_msgs.msg;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Concurrent;

public class StereoRobotUIViewer : MonoBehaviour
{
    private ROS2UnityComponent ros2Unity;
    public ROS2Node ros2Node;

    [Header("Configuración de ROS 2")]
    public string leftTopic = "/camera/left/compressed";
    public string rightTopic = "/camera/right/compressed";

    [Header("Referencias UI")]
    public RawImage displayImage;
    public TMP_InputField leftInputField;
    public TMP_InputField rightInputField;
    public GameObject rootPanel;

    // Colas y Texturas
    private ConcurrentQueue<byte[]> leftImageQueue = new ConcurrentQueue<byte[]>();
    private ConcurrentQueue<byte[]> rightImageQueue = new ConcurrentQueue<byte[]>();
    private Texture2D leftTexture;
    private Texture2D rightTexture;
    private Material stereoMaterial;

    private ISubscription<CompressedImage> subLeftImage;
    private ISubscription<CompressedImage> subRightImage;

    // Gestión del Teclado de Meta
    private TouchScreenKeyboard overlayKeyboard;
    private TMP_InputField activeInputField;

    void Start()
    {
        ros2Unity = Object.FindAnyObjectByType<ROS2UnityComponent>();
        leftTexture = new Texture2D(2, 2);
        rightTexture = new Texture2D(2, 2);

        if (displayImage != null)
        {
            stereoMaterial = Instantiate(displayImage.material);
            displayImage.material = stereoMaterial;
        }

        // Configurar eventos de selección para abrir el teclado
        if (leftInputField != null)
            leftInputField.onSelect.AddListener(delegate { OpenKeyboard(leftInputField); });

        if (rightInputField != null)
            rightInputField.onSelect.AddListener(delegate { OpenKeyboard(rightInputField); });
    }

    void Update()
    {
        // 1. Inicializar el nodo
        if (ros2Node == null && ros2Unity.Ok())
        {
            string nodeName = "stereo_viewer_" + gameObject.name.Replace(" ", "_") + "_" + Random.Range(0, 1000);
            ros2Node = ros2Unity.CreateNode(nodeName);
            SubscribeLeft();
            SubscribeRight();
        }

        // 2. Gestión del teclado
        if (overlayKeyboard != null && activeInputField != null)
        {
            activeInputField.text = overlayKeyboard.text;

            if (overlayKeyboard.status == TouchScreenKeyboard.Status.Done)
            {
                if (activeInputField == leftInputField) ChangeLeftTopic(activeInputField.text);
                else if (activeInputField == rightInputField) ChangeRightTopic(activeInputField.text);

                overlayKeyboard = null;
                activeInputField = null;
            }
        }

        ProcessQueues();
    }

    public void OpenKeyboard(TMP_InputField inputField)
    {
        activeInputField = inputField;
        overlayKeyboard = TouchScreenKeyboard.Open(activeInputField.text, TouchScreenKeyboardType.Default);
    }

    public void ChangeLeftTopic(string newTopic)
    {
        if (string.IsNullOrEmpty(newTopic) || newTopic == leftTopic) return;
        if (subLeftImage != null) ros2Node.RemoveSubscription<CompressedImage>(subLeftImage);
        leftTopic = newTopic;
        SubscribeLeft();
    }

    public void ChangeRightTopic(string newTopic)
    {
        if (string.IsNullOrEmpty(newTopic) || newTopic == rightTopic) return;
        if (subRightImage != null) ros2Node.RemoveSubscription<CompressedImage>(subRightImage);
        rightTopic = newTopic;
        SubscribeRight();
    }

    private void SubscribeLeft()
    {
        subLeftImage = ros2Node.CreateSubscription<CompressedImage>(
            leftTopic, msg => EnqueueImage(msg, leftImageQueue));
    }

    private void SubscribeRight()
    {
        subRightImage = ros2Node.CreateSubscription<CompressedImage>(
            rightTopic, msg => EnqueueImage(msg, rightImageQueue));
    }

    private void EnqueueImage(CompressedImage msg, ConcurrentQueue<byte[]> queue)
    {
        while (queue.Count >= 1) queue.TryDequeue(out _);
        queue.Enqueue(msg.Data);
    }

    private void ProcessQueues()
    {
        if (stereoMaterial == null) return;

        if (leftImageQueue.TryDequeue(out byte[] leftData) && leftTexture.LoadImage(leftData))
            stereoMaterial.SetTexture("_LeftEyeTex", leftTexture);

        if (rightImageQueue.TryDequeue(out byte[] rightData) && rightTexture.LoadImage(rightData))
            stereoMaterial.SetTexture("_RightEyeTex", rightTexture);
    }

    public void ClosePanel()
    {
        if (rootPanel != null) Destroy(rootPanel);
        else Destroy(transform.root.gameObject);
    }

    private void OnDestroy()
    {
        if (ros2Node != null && ros2Unity != null) ros2Unity.RemoveNode(ros2Node);
    }
}