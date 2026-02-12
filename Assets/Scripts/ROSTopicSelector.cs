using UnityEngine;
using TMPro;

public class ROSTopicSelector : MonoBehaviour
{
    [Header("Referencias UI")]
    public TMP_InputField topicInputField;
    public GameObject rootPanel;

    private SingleCameraViewer cameraViewer;

    // Variables para el teclado de Meta (según doc oficial)
    private TouchScreenKeyboard overlayKeyboard;
    public string inputText = "";

    void Start()
    {
        cameraViewer = GetComponent<SingleCameraViewer>();

        // Configuramos el InputField para que cuando se seleccione, abra el teclado
        if (topicInputField != null)
        {
            topicInputField.onSelect.AddListener(delegate { OpenKeyboard(); });
        }
    }

    void Update()
    {
        // Si el teclado está abierto, actualizamos el texto del InputField en tiempo real
        if (overlayKeyboard != null)
        {
            inputText = overlayKeyboard.text;
            topicInputField.text = inputText;

            // Si el usuario confirma en el teclado (le da al "check")
            if (overlayKeyboard.status == TouchScreenKeyboard.Status.Done ||
                overlayKeyboard.status == TouchScreenKeyboard.Status.Canceled)
            {
                cameraViewer.ChangeTopic(inputText);
                overlayKeyboard = null; // Liberamos la referencia
            }
        }
    }

    public void OpenKeyboard()
    {
        // Abrimos el teclado del sistema (Overlay)
        overlayKeyboard = TouchScreenKeyboard.Open("", TouchScreenKeyboardType.Default);
    }

    public void ClosePanel()
    {
        if (rootPanel != null) Destroy(rootPanel);
        else Destroy(transform.root.gameObject);
    }
}