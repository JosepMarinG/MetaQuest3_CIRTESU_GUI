using UnityEngine;

public class ObjectActivationFeedback : MonoBehaviour
{
    [Header("Estado")]
    public bool isActivated = true;

    [Header("Objetos a controlar")]
    [Tooltip("Lista de objetos que se activan o desactivan con este controlador.")]
    public GameObject[] controlledObjects;

    [Header("UI Feedback")]
    public ToggleIconFeedback iconFeedback;

    [Header("Debug")]
    public bool verboseDebugLogs = false;

    private void Start()
    {
        ApplyActivationState();
    }

    public void SetActivation()
    {
        isActivated = !isActivated;
        ApplyActivationState();
    }

    public void SetActivation(bool active)
    {
        if (isActivated == active)
        {
            return;
        }

        SetActivation();
    }

    public void EnableObjects(bool enable)
    {
        SetActivation(enable);
    }

    private void ApplyActivationState()
    {
        SetControlledObjectsActive(isActivated);

        if (iconFeedback != null)
        {
            iconFeedback.UpdateIcon(isActivated);
        }

        if (verboseDebugLogs)
        {
            Debug.Log($"[ObjectActivationFeedback] Objetos {(isActivated ? "ACTIVOS" : "INACTIVOS")}");
        }
    }

    private void SetControlledObjectsActive(bool active)
    {
        if (controlledObjects == null)
        {
            return;
        }

        for (int i = 0; i < controlledObjects.Length; i++)
        {
            GameObject controlledObject = controlledObjects[i];
            if (controlledObject == null)
            {
                continue;
            }

            if (controlledObject.activeSelf != active)
            {
                controlledObject.SetActive(active);
            }
        }
    }
}