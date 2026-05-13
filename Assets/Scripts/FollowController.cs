using UnityEngine;

public class FollowController : MonoBehaviour
{
    [Header("Configuración del Controlador")]
    [SerializeField] private OVRInput.Controller targetController = OVRInput.Controller.RTouch;
    [SerializeField] private bool followPosition = true;
    [SerializeField] private bool followRotation = false;

    [Header("Offset relativo")]
    [SerializeField] private Vector3 positionOffset = new Vector3(0.1f, 0.05f, 0.15f);
    [SerializeField] private Vector3 rotationOffset = Vector3.zero;

    [Header("Suavizado")]
    [SerializeField] private bool useSmoothFollow = true;
    [SerializeField] private float smoothSpeed = 10f;

    private OVRControllerHelper controllerHelper;
    private Vector3 targetPosition;
    private Quaternion targetRotation;

    private void Start()
    {
        // Buscar OVRControllerHelper para el controlador especificado
        OVRControllerHelper[] helpers = FindObjectsOfType<OVRControllerHelper>();
        foreach (OVRControllerHelper helper in helpers)
        {
            if (helper.m_controller == targetController)
            {
                controllerHelper = helper;
                break;
            }
        }

        if (controllerHelper == null)
        {
            Debug.LogWarning($"[FollowController] OVRControllerHelper para {targetController} no encontrado. Intentando búsqueda alternativa.");
            // Fallback: buscar por nombre o usar el padre si el script está en un hijo del controlador
            controllerHelper = GetComponentInParent<OVRControllerHelper>();
        }

        if (controllerHelper != null)
        {
            targetPosition = transform.position;
            targetRotation = transform.rotation;
        }
    }

    private void Update()
    {
        if (controllerHelper == null || !OVRInput.IsControllerConnected(targetController))
        {
            return;
        }

        // Obtener la posición y rotación del controlador
        Vector3 controllerPos = controllerHelper.transform.position;
        Quaternion controllerRot = controllerHelper.transform.rotation;

        // Aplicar offset
        targetPosition = controllerPos + controllerRot * positionOffset;

        if (followRotation)
        {
            targetRotation = controllerRot * Quaternion.Euler(rotationOffset);
        }

        // Actualizar transform
        if (useSmoothFollow)
        {
            if (followPosition)
            {
                transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * smoothSpeed);
            }

            if (followRotation)
            {
                transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, Time.deltaTime * smoothSpeed);
            }
        }
        else
        {
            if (followPosition)
            {
                transform.position = targetPosition;
            }

            if (followRotation)
            {
                transform.rotation = targetRotation;
            }
        }
    }
}
