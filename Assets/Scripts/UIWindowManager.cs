using UnityEngine;
using UnityEngine.UIElements;

public class UIWindowManager : MonoBehaviour
{
    private VisualElement workspace;
    public VisualTreeAsset windowTemplate; // UXML opcional con el diseño de una ventana

    void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;
        workspace = root.Q<VisualElement>("workspace");

        // Ejemplo: crear dos ventanas al inicio
        CreateWindow("Cámara Frontal", new Vector2(50, 50));
        CreateWindow("Cámara Trasera", new Vector2(500, 100));
    }

    void CreateWindow(string title, Vector2 position)
    {
        VisualElement window;
        VisualElement header = null;

        // Si hay plantilla UXML
        if (windowTemplate != null)
        {
            window = windowTemplate.CloneTree();
            // intenta buscar el header dentro del UXML por name
            header = window.Q<VisualElement>("window-header");
        }
        else
        {
            // Crear la ventana desde código si no hay UXML
            window = new VisualElement();
            window.AddToClassList("window");

            header = new VisualElement();
            header.AddToClassList("window-header");
            header.Add(new Label(title));

            var closeButton = new Button(() => workspace.Remove(window)) { text = "✕" };
            header.Add(closeButton);

            var content = new VisualElement();
            content.AddToClassList("window-content");

            var resizer = new VisualElement();
            resizer.AddToClassList("window-resizer");

            window.Add(header);
            window.Add(content);
            window.Add(resizer);
        }

        // Añadir ventana al workspace
        workspace.Add(window);
        window.style.left = position.x;
        window.style.top = position.y;

        // Solo si encontró header (no null)
        if (header != null)
            MakeDraggable(header);

        MakeResizable(window);
    }

    void MakeDraggable(VisualElement header)
    {
        VisualElement window = header.parent;
        Vector2 offset = Vector2.zero;

        header.RegisterCallback<PointerDownEvent>(e =>
        {
            offset = (Vector2)e.position - new Vector2(window.resolvedStyle.left, window.resolvedStyle.top);
            header.CapturePointer(e.pointerId);
        });

        header.RegisterCallback<PointerMoveEvent>(e =>
        {
            if (header.HasPointerCapture(e.pointerId))
            {
                Vector2 newPos = (Vector2)e.position - offset;
                window.style.left = newPos.x;
                window.style.top = newPos.y;
            }
        });

        header.RegisterCallback<PointerUpEvent>(e =>
        {
            header.ReleasePointer(e.pointerId);
        });
    }

    void MakeResizable(VisualElement window)
    {
        var resizer = window.Q<VisualElement>("window-resizer");
        if (resizer == null)
            return; // seguridad si el elemento no existe

        Vector2 startSize = Vector2.zero;
        Vector2 startPos = Vector2.zero;

        resizer.RegisterCallback<PointerDownEvent>(e =>
        {
            startSize = new Vector2(window.resolvedStyle.width, window.resolvedStyle.height);
            startPos = e.position;
            resizer.CapturePointer(e.pointerId);
        });

        resizer.RegisterCallback<PointerMoveEvent>(e =>
        {
            if (resizer.HasPointerCapture(e.pointerId))
            {
                Vector2 delta = (Vector2)e.position - startPos;
                window.style.width = startSize.x + delta.x;
                window.style.height = startSize.y + delta.y;
            }
        });

        resizer.RegisterCallback<PointerUpEvent>(e =>
        {
            resizer.ReleasePointer(e.pointerId);
        });
    }
}
