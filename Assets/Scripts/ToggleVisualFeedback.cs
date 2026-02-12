using UnityEngine;
using TMPro; // Necesario para TextMeshProUGUI

public class ToggleVisualFeedback : MonoBehaviour
{
    [Header("Referencias")]
    public TextMeshProUGUI buttonLabel; // Arrastra aquí el objeto 'Label' del botón

    [Header("Configuración de Colores")]
    public Color activeColor = Color.green;
    public Color originalColor = Color.white;

    void Start()
    {
        // Si no asignaste un color original manualmente, lo leemos al empezar
        if (buttonLabel != null)
        {
            originalColor = buttonLabel.color;
        }
    }

    // Esta función la llama tu script QuestTeleopROS2
    public void UpdateColor(bool isOn)
    {
        if (buttonLabel == null) return;

        // Cambiamos el color del texto directamente
        buttonLabel.color = isOn ? activeColor : originalColor;
    }
}