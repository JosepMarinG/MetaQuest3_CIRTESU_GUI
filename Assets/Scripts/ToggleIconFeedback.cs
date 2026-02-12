using UnityEngine;
using UnityEngine.UI;

public class ToggleIconFeedback : MonoBehaviour
{
    [Header("Referencias")]
    public Image iconImage; // El componente Image que quieres cambiar

    [Header("Sprites")]
    public Sprite activeIcon;   // Icono cuando está activado (ej: verde o encendido)
    public Sprite inactiveIcon; // Icono cuando está desactivado (ej: blanco o apagado)

    public void UpdateIcon(bool isActive)
    {
        if (iconImage == null || activeIcon == null || inactiveIcon == null) return;

        // Cambiamos el sprite según el estado
        iconImage.sprite = isActive ? activeIcon : inactiveIcon;
    }
}