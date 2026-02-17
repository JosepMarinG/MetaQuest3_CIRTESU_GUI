using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using CesiumForUnity;

[DefaultExecutionOrder(1000)]
public class MinimapNavigationController : MonoBehaviour
{
    [Header("Referencias")]
    public CesiumGeoreference georeference;
    public GameObject cesiumWorldTerrain; // El objeto que contiene las Tiles
    public CesiumPolygonRasterOverlay rasterOverlay;
    // public ROS2.QuestTeleopROS2 teleopController; // Comentado

    // Opciones para determinar el punto de enfoque al hacer zoom
    [Tooltip("Transform a usar para el raycast de enfoque (por defecto Main Camera)")]
    public Transform headTransform;
    [Tooltip("Transform del controlador derecho si prefieres usar su rayo para el foco")]
    public Transform controllerTransform;
    [Tooltip("Si está activo, usa el controlador para calcular el punto de foco; si no, usa la cabeza/cámara")]
    public bool useControllerForZoomFocus = false;
    [Tooltip("Layer(s) del minimapa para que el raycast lo detecte (asigna la capa del minimapa) ")]
    public LayerMask minimapLayerMask = ~0;

    [Header("Debug")]
    public bool debugLogs = false;

    [Header("Ajustes de Escala")]
    public float scaleSpeed = 0.5f;
    public float minScale = 0.0001f;
    public float maxScale = 0.05f;

    [Header("Ajustes de Panning")]
    public float panSpeed = 1.0f;

    [Header("Cartographic Polygon")]
    [Tooltip("Transform del CesiumCartographicPolygon (assign)")]
    public Transform cartographicPolygonTransform;
    [Tooltip("Si está activo, mantiene el CartographicPolygon fijo respecto a la cabeza mientras navegas")]
    public bool keepCartographicPolygonFixedToHead = true;

    // Offset/rotación del polygon relativa a la cabeza (capturada al iniciar navegación)
    private Vector3 polygonHeadLocalPos;
    private Quaternion polygonHeadLocalRot;
    private bool polygonOffsetCaptured = false;

    [Header("Overlay refresh")]
    [Tooltip("Tiempo (s) de debounce antes de refrescar el overlay para evitar parpadeos mientras interactúas")]
    public float overlayRefreshDebounce = 0.08f;

    [Tooltip("Umbral relativo de cambio de escala (p. ej. 0.02 = 2%) necesario para refrescar el overlay")]
    public float overlayScaleChangeThreshold = 0.02f;

    [Tooltip("Umbral (grados) de cambio en lat/lon para considerar que el origen geográfico cambió lo suficiente como para refrescar el overlay")]
    public double overlayGeoMoveThresholdDegrees = 0.00005; // ~5-6 m

    // estado para refresco diferido
    private bool needsOverlayRefresh = false;
    private float lastOverlayChangeTime = 0f;
    private bool overlayRefreshInProgress = false;

    // Estado usado para evitar refrescos cuando el georeference se mueve "muy poco"
    private double lastOverlayGeoLat = double.NaN;
    private double lastOverlayGeoLon = double.NaN;

    private bool isNavigating = false;

    void Start()
    {
        // Inicializar último origen geográfico conocido (evita refrescos innecesarios)
        if (georeference != null)
        {
            lastOverlayGeoLat = georeference.latitude;
            lastOverlayGeoLon = georeference.longitude;
        }

        // En dispositivos móviles/Quest, aumentar el debounce por defecto para reducir toggles
        #if UNITY_ANDROID || UNITY_IOS
        if (Application.isMobilePlatform)
        {
            overlayRefreshDebounce = Mathf.Max(overlayRefreshDebounce, 0.35f);
            if (debugLogs) Debug.Log($"[Minimap] Mobile platform detected — overlayRefreshDebounce={overlayRefreshDebounce}");
        }
        #endif
    }

    void Update()
    {
        var leftHand = UnityEngine.InputSystem.XR.XRController.leftHand;
        var rightHand = UnityEngine.InputSystem.XR.XRController.rightHand;

        if (rightHand == null || leftHand == null) return;

        // Botón A (Mando Derecho) = primaryButton en OpenXR
        if (GetButtonState(rightHand, "primaryButton"))
        {
            if (!isNavigating)
            {
                isNavigating = true;
                if (debugLogs) Debug.Log("<color=cyan>[Minimap]</color> Navegación Activa");

                // Capture polygon offset relative to head so it remains fixed during navigation
                if (keepCartographicPolygonFixedToHead && cartographicPolygonTransform != null)
                {
                    Transform head = headTransform != null ? headTransform : Camera.main?.transform;
                    if (head != null)
                    {
                        polygonHeadLocalPos = head.InverseTransformPoint(cartographicPolygonTransform.position);
                        polygonHeadLocalRot = Quaternion.Inverse(head.rotation) * cartographicPolygonTransform.rotation;
                        polygonOffsetCaptured = true;
                    }
                }
            }

            bool scaleChanged = HandleScale(leftHand);
            bool posChanged = HandlePanning(leftHand);

            // Schedule overlay refresh (debounced) to avoid one-frame flicker when toggling enabled repeatedly
            if (scaleChanged || posChanged) ScheduleOverlayRefresh();
        }
        else if (isNavigating)
        {
            isNavigating = false;
            polygonOffsetCaptured = false;

            // Force refresh at the end of navigation to ensure overlay state is consistent
            if (needsOverlayRefresh) RefreshOverlayImmediate();
        }

        // Intentar refrescar overlay si está programado (debounce)
        //TryPerformOverlayRefresh();
    }

    void LateUpdate()
    {
        // Mientras navegamos, reposicionar el polygon después de que Cesium actualice su transform
        if (isNavigating && polygonOffsetCaptured && keepCartographicPolygonFixedToHead && cartographicPolygonTransform != null)
        {
            Transform head = headTransform != null ? headTransform : Camera.main?.transform;
            if (head != null)
            {
                cartographicPolygonTransform.position = head.TransformPoint(polygonHeadLocalPos);
                cartographicPolygonTransform.rotation = head.rotation * polygonHeadLocalRot;
            }
        }
        else if (isNavigating && keepCartographicPolygonFixedToHead && cartographicPolygonTransform != null && !polygonOffsetCaptured)
        {
            // Fallback: capture offset si no se capturó al iniciar navegación
            Transform head = headTransform != null ? headTransform : Camera.main?.transform;
            if (head != null)
            {
                polygonHeadLocalPos = head.InverseTransformPoint(cartographicPolygonTransform.position);
                polygonHeadLocalRot = Quaternion.Inverse(head.rotation) * cartographicPolygonTransform.rotation;
                polygonOffsetCaptured = true;
                if (debugLogs) Debug.Log("[Minimap] Polygon offset capturado en LateUpdate (fallback).");
            }
        }
    }

    private bool HandleScale(UnityEngine.InputSystem.XR.XRController left)
    {
        // Y = secondaryButton | X = primaryButton
        bool isY = GetButtonState(left, "secondaryButton");
        bool isX = GetButtonState(left, "primaryButton");

        float delta = 0f;
        if (isY) delta = scaleSpeed * Time.deltaTime;
        if (isX) delta = -scaleSpeed * Time.deltaTime;

        if (Mathf.Approximately(delta, 0f) || cesiumWorldTerrain == null) return false;

        float currentScale = cesiumWorldTerrain.transform.localScale.x;
        float nextScale = Mathf.Clamp(currentScale + delta, minScale, maxScale);
        if (Mathf.Approximately(nextScale, currentScale)) return false;

        // Punto de foco (en coordenadas mundo) — raycast desde cabeza o controlador
        Vector3 focusPoint = GetFocusPointWorld();

        // Mantener 'focusPoint' fijo en el mundo al cambiar la escala:
        // newPos = focus - s * (focus - oldPos)
        float scaleFactor = nextScale / currentScale;
        Vector3 oldPos = cesiumWorldTerrain.transform.position;
        Vector3 newPos = focusPoint - scaleFactor * (focusPoint - oldPos);

        cesiumWorldTerrain.transform.position = newPos;
        cesiumWorldTerrain.transform.localScale = Vector3.one * nextScale;

        bool significantScaleChange = Mathf.Abs(nextScale - currentScale) / currentScale >= overlayScaleChangeThreshold;
        if (debugLogs) Debug.Log($"[Minimap] Scale {currentScale:F6} -> {nextScale:F6} (significant={significantScaleChange}), focus {focusPoint}");

        // Sólo solicitamos refresh del overlay si el cambio de escala es relevante
        return significantScaleChange;
    }

    private Vector2 ReadThumbstick(UnityEngine.InputSystem.XR.XRController controller)
    {
        if (controller == null) return Vector2.zero;

        string[] candidates = new string[] { "thumbstick", "primary2DAxis", "secondary2DAxis", "joystick", "stick" };
        foreach (var name in candidates)
        {
            var control = controller[name] as Vector2Control;
            if (control != null) return control.ReadValue();
        }

        // Fallback adicional: si hay un Gamepad conectado (útil en editor)
        if (Gamepad.current != null)
        {
            return Gamepad.current.leftStick.ReadValue();
        }

        return Vector2.zero;
    }

    private bool HandlePanning(UnityEngine.InputSystem.XR.XRController left)
    {
        Vector2 joy = ReadThumbstick(left);
        if (joy.magnitude <= 0.1f) return false;

        double lat = georeference.latitude;
        double lon = georeference.longitude;

        float currentScale = cesiumWorldTerrain != null ? cesiumWorldTerrain.transform.localScale.x : 1f;
        double factor = 0.01 / (currentScale * 100.0);

        lat += (double)joy.y * panSpeed * factor * Time.deltaTime;
        lon += (double)joy.x * panSpeed * factor * Time.deltaTime;

        // Aplicamos el nuevo origen geográfico
        georeference.SetOriginLongitudeLatitudeHeight(lon, lat, georeference.height);

        // Considerar sólo como "posChanged" si el origen se movió lo suficiente
        bool significantGeoChange = double.IsNaN(lastOverlayGeoLat)
            || System.Math.Abs(lat - lastOverlayGeoLat) >= overlayGeoMoveThresholdDegrees
            || System.Math.Abs(lon - lastOverlayGeoLon) >= overlayGeoMoveThresholdDegrees;

        if (significantGeoChange)
        {
            lastOverlayGeoLat = lat;
            lastOverlayGeoLon = lon;
            if (debugLogs) Debug.Log($"[Minimap] Georeference moved (lat:{lat:F6}, lon:{lon:F6}) — scheduling overlay refresh.");
            return true;
        }

        if (debugLogs) Debug.Log("[Minimap] Georeference moved, but below threshold — no overlay refresh.");
        // El reposition del CartographicPolygon se realiza en LateUpdate para evitar que Cesium lo sobrescriba.
        return false;
    }

    // Devuelve el punto en el mundo donde estás "mirando" para usar como foco al hacer zoom
    private Vector3 GetFocusPointWorld()
    {
        Transform origin = useControllerForZoomFocus && controllerTransform != null ? controllerTransform : (headTransform != null ? headTransform : Camera.main?.transform);
        if (origin == null)
        {
            if (cesiumWorldTerrain != null) return cesiumWorldTerrain.transform.position;
            return Vector3.zero;
        }

        Ray ray = new Ray(origin.position, origin.forward);
        RaycastHit hit;

        // Primero intentamos raycast contra la capa del minimapa (asignar capa al minimapa para detección)
        if (Physics.Raycast(ray, out hit, 200f, minimapLayerMask))
        {
            return hit.point;
        }

        // Fallback: intersección con plano horizontal a la altura del georeference / terreno
        float planeY = georeference != null ? (float)georeference.height : (cesiumWorldTerrain != null ? cesiumWorldTerrain.transform.position.y : origin.position.y);
        Plane plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
        if (plane.Raycast(ray, out float enter))
        {
            return ray.GetPoint(enter);
        }

        // Último recurso: centro del terreno
        return cesiumWorldTerrain != null ? cesiumWorldTerrain.transform.position : origin.position + origin.forward * 10f;
    }

    private void RefreshOverlay()
    {
        // Only refresh if overlay exists and is actually used for excluding tiles
        if (rasterOverlay == null) return;
        if (overlayRefreshInProgress) return;
        if (!rasterOverlay.excludeSelectedTiles)
        {
            if (debugLogs) Debug.Log("[Minimap] rasterOverlay.excludeSelectedTiles == false — skipping overlay refresh.");
            needsOverlayRefresh = false;
            return;
        }
        if (rasterOverlay.polygons == null || rasterOverlay.polygons.Count == 0)
        {
            if (debugLogs) Debug.Log("[Minimap] rasterOverlay.polygons empty — skipping overlay refresh.");
            needsOverlayRefresh = false;
            return;
        }

        overlayRefreshInProgress = true;

        // Toggle enabled once (legacy method used by Cesium overlay). Debounced callers prevent continuous toggles.
        rasterOverlay.enabled = false;
        rasterOverlay.enabled = true;

        needsOverlayRefresh = false;
        overlayRefreshInProgress = false;

        if (debugLogs) Debug.Log("[Minimap] Overlay refrescado");
    }

    private void ScheduleOverlayRefresh()
    {
        // Don't even schedule if overlay is not configured to exclude tiles or has no polygons
        if (rasterOverlay == null)
        {
            if (debugLogs) Debug.Log("[Minimap] No rasterOverlay assigned — skipping schedule.");
            return;
        }
        if (!rasterOverlay.excludeSelectedTiles)
        {
            if (debugLogs) Debug.Log("[Minimap] excludeSelectedTiles disabled — skipping schedule.");
            return;
        }
        if (rasterOverlay.polygons == null || rasterOverlay.polygons.Count == 0)
        {
            if (debugLogs) Debug.Log("[Minimap] rasterOverlay.polygons empty — skipping schedule.");
            return;
        }

        needsOverlayRefresh = true;
        lastOverlayChangeTime = Time.time;
        if (debugLogs) Debug.Log("[Minimap] Overlay refresh programado");
    }

    private void TryPerformOverlayRefresh()
    {
        if (!needsOverlayRefresh || overlayRefreshInProgress || rasterOverlay == null) return;

        // double-check that overlay is meaningful to refresh
        if (!rasterOverlay.excludeSelectedTiles) { needsOverlayRefresh = false; return; }
        if (rasterOverlay.polygons == null || rasterOverlay.polygons.Count == 0) { needsOverlayRefresh = false; return; }

        if (Time.time - lastOverlayChangeTime < overlayRefreshDebounce) return;
        RefreshOverlay();
    }

    private void RefreshOverlayImmediate()
    {
        // Bypass debounce and refresh right away (usa con moderación)
        if (rasterOverlay == null) return;
        if (!rasterOverlay.excludeSelectedTiles)
        {
            if (debugLogs) Debug.Log("[Minimap] excludeSelectedTiles disabled — skipping immediate overlay refresh.");
            needsOverlayRefresh = false;
            return;
        }
        if (rasterOverlay.polygons == null || rasterOverlay.polygons.Count == 0)
        {
            if (debugLogs) Debug.Log("[Minimap] rasterOverlay.polygons empty — skipping immediate overlay refresh.");
            needsOverlayRefresh = false;
            return;
        }

        if (debugLogs) Debug.Log("[Minimap] Overlay refresh inmediato");
        rasterOverlay.enabled = false;
        rasterOverlay.enabled = true;
        needsOverlayRefresh = false;
    }

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