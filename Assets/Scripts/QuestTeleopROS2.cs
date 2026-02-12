using UnityEngine;
using UnityEngine.InputSystem;
using ROS2;
using geometry_msgs.msg;
using TMPro;

namespace ROS2
{
    public class QuestTeleopROS2 : MonoBehaviour
    {
        private ROS2UnityComponent ros2Unity;
        private ROS2Node ros2Node;
        private IPublisher<geometry_msgs.msg.Twist> cmd_vel_publisher;

        [Header("Estado de Control")]
        public bool isActivated = true; // Controlado por el Toggle del Panel

        [Header("Configuración de ROS 2")]
        public string topicName = "/cmd_vel";
        public TMP_InputField inputField;

        [Header("Sensibilidad")]
        public float velocityLinear = 0.5f;
        public float velocityAngular = 1.0f;

        private Vector2 moveInput;
        private Vector2 rotateInput;

        public ToggleIconFeedback iconFeedback;


        void Start()
        {
            ros2Unity = GetComponent<ROS2UnityComponent>();
            if (iconFeedback != null)
            {
                iconFeedback.UpdateIcon(isActivated);
            }
        }

        void Update()
        {
            // 1. Si no está activado, no leemos mandos ni publicamos nada
            //if (!isActivated) return;

            // 2. Leer los joysticks físicos
            var leftHand = UnityEngine.InputSystem.XR.XRController.leftHand;
            var rightHand = UnityEngine.InputSystem.XR.XRController.rightHand;

            if (leftHand != null)
            {
                var thumb = leftHand["thumbstick"] as UnityEngine.InputSystem.Controls.Vector2Control;
                if (thumb != null) moveInput = thumb.ReadValue();
            }

            if (rightHand != null)
            {
                var thumb = rightHand["thumbstick"] as UnityEngine.InputSystem.Controls.Vector2Control;
                if (thumb != null) rotateInput = thumb.ReadValue();
            }

            // 3. Inicializar el nodo una sola vez si ROS está listo
            if (ros2Node == null && ros2Unity.Ok())
            {
                ros2Node = ros2Unity.CreateNode("QuestTeleopNode");
                string finalTopic = (inputField != null && !string.IsNullOrEmpty(inputField.text))
                                    ? inputField.text : topicName;
                cmd_vel_publisher = ros2Node.CreatePublisher<geometry_msgs.msg.Twist>(finalTopic);
                Debug.Log($"[Teleop] Nodo creado. Publicando en: {finalTopic}");
            }

            // 4. Publicar comandos de movimiento
            if (cmd_vel_publisher != null && isActivated == true)
            {
                geometry_msgs.msg.Twist msg = new geometry_msgs.msg.Twist
                {
                    Linear = new geometry_msgs.msg.Vector3
                    {
                        X = moveInput.y * velocityLinear,
                        Y = moveInput.x * velocityLinear
                    },
                    Angular = new geometry_msgs.msg.Vector3
                    {
                        Z = -rotateInput.x * velocityAngular
                    }
                };
                cmd_vel_publisher.Publish(msg);
            }
        }

        // Método público para conectar con el Toggle de la UI
        public void SetActivation()
        {
            isActivated = !isActivated;
            if (iconFeedback != null)
            {
                iconFeedback.UpdateIcon(isActivated);
            }

            // SEGURIDAD: Si desactivamos el control, enviamos un mensaje de "Parada" (0,0,0)
            // para evitar que el robot se quede moviendo con el último comando recibido.
            if (!isActivated && cmd_vel_publisher != null)
            {
                geometry_msgs.msg.Twist stopMsg = new geometry_msgs.msg.Twist();
                cmd_vel_publisher.Publish(stopMsg);
                Debug.Log("[Teleop] Control desactivado. Enviando comando de parada.");
            }
        }

        public void Destroy()
        {
            if (ros2Node != null) ros2Unity.RemoveNode(ros2Node);
        }
    }
}