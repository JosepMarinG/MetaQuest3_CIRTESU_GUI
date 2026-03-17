using UnityEngine;
using TMPro;

public class StereoOVRTopicSelector : MonoBehaviour
{
    [Header("Referencias UI")]
    public TMP_InputField topicInputField;
    public GameObject rootPanel;

    private Stereo_OVR_SBS stereoViewer;

    // Variables para el teclado de Meta (según doc oficial)
    private TouchScreenKeyboard overlayKeyboard;
    public string inputText = "";

    void Start()
    {
        stereoViewer = GetComponent<Stereo_OVR_SBS>();

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
                stereoViewer.ChangeTopic(inputText);
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
