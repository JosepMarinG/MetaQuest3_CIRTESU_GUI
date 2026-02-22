using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public class MapToggleController : MonoBehaviour
{
    [Header("Referencias")]
    public GameObject mapRoot; // El objeto Map_Root que quieres apagar/encender

    private bool wasBPressed = false; // Para evitar el toggle infinito mientras se mantiene pulsado

    void Update()
    {
        var rightHand = UnityEngine.InputSystem.XR.XRController.rightHand;
        if (rightHand == null) return;

        // "secondaryButton" es el Botón B en el mando derecho (OpenXR)
        bool isBPressed = GetButtonState(rightHand, "secondaryButton");

        // Detectamos el momento justo en que se pulsa (pero no estaba pulsado antes)
        if (isBPressed && !wasBPressed)
        {
            if (mapRoot != null)
            {
                // Invertimos el estado actual del objeto
                //mapRoot.SetActive(!mapRoot.activeSelf);
                Destroy(mapRoot);
                Debug.Log($"<color=orange>[MapToggle]</color> Mapa {(mapRoot.activeSelf ? "Visible" : "Oculto")}");
            }
            wasBPressed = true;
        }
        // Cuando sueltas el botón, reseteamos para permitir la siguiente pulsación
        else if (!isBPressed)
        {
            wasBPressed = false;
        }
    }

    // Usamos exactamente tu misma lógica de lectura
    private bool GetButtonState(UnityEngine.InputSystem.XR.XRController controller, string buttonName)
    {
        if (controller != null)
        {
            var control = controller[buttonName] as UnityEngine.InputSystem.Controls.ButtonControl;
            if (control != null) return control.isPressed;
        }
        return false;
    }
}