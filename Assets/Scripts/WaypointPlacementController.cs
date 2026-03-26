using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.Controls;
using UnityEngine.Rendering;

public class WaypointPlacementController : MonoBehaviour
{
    [Header("Estado")]
    public bool isActivated = false;

    [Header("Referencias")]
    [Tooltip("Prefab del waypoint. Debe incluir CesiumGlobeAnchor y tus scripts de interacción.")]
    public GameObject waypointPrefab;

    [Tooltip("Transform del rayo del mando derecho (normalmente el objeto del ray interactor).")]
    public Transform rightRayOrigin;

    [Tooltip("Padre donde se instancian los waypoints dentro del minimapa.")]
    public Transform minimapWaypointsRoot;

    [Header("Raycast")]
    [Tooltip("Capas válidas para detectar la intersección con el minimapa.")]
    public LayerMask minimapLayerMask = ~0;

    public float rayDistance = 300f;

    [Header("Comportamiento")]
    [Tooltip("Tiempo mínimo tras activar para evitar colocar un waypoint con el mismo click del botón UI.")]
    public float activationClickGuardTime = 0.2f;

    [Header("Visual del Rayo (Opcional)")]
    public bool showPlacementRay = true;
    public LineRenderer placementRayRenderer;
    public GameObject hitIndicator;
    public float missRayLength = 20f;
    public float lineWidth = 0.005f;
    public Color lineColor = Color.cyan;

    [Tooltip("Si está activo, deshabilita scripts para evitar interferencias durante el modo creación.")]
    public MonoBehaviour[] behavioursToDisableWhilePlacing;

    [Tooltip("Si está activo, deshabilita objetos para evitar interferencias durante el modo creación.")]
    public GameObject[] objectsToDisableWhilePlacing;

    [Header("UI Feedback")]
    public ToggleIconFeedback iconFeedback;

    [Header("Debug")]
    public bool debugLogs = false;

    [SerializeField]
    private List<GameObject> waypoints = new List<GameObject>();

    private float activationTime = -999f;
    private Material runtimeLineMaterial;

    public IReadOnlyList<GameObject> Waypoints => waypoints;

    private void Start()
    {
        ApplyPlacementModeState(false);
        EnsurePlacementRayRenderer();
        SetRayVisualActive(false);
        UpdateHitIndicator(false, Vector3.zero, Vector3.up);
        if (iconFeedback != null)
        {
            iconFeedback.UpdateIcon(isActivated);
        }
    }

    private void OnDestroy()
    {
        if (runtimeLineMaterial != null)
        {
            Destroy(runtimeLineMaterial);
            runtimeLineMaterial = null;
        }
    }

    private void Update()
    {
        if (!isActivated)
        {
            SetRayVisualActive(false);
            UpdateHitIndicator(false, Vector3.zero, Vector3.up);
            return;
        }

        if (rightRayOrigin == null)
        {
            SetRayVisualActive(false);
            UpdateHitIndicator(false, Vector3.zero, Vector3.up);
            if (debugLogs)
            {
                Debug.LogWarning("[WaypointPlacement] rightRayOrigin no asignado.");
            }
            return;
        }

        bool hasHit = TryGetMapHit(out RaycastHit hit);
        UpdateRayVisual(hasHit ? hit.point : rightRayOrigin.position + (rightRayOrigin.forward * missRayLength));
        UpdateHitIndicator(hasHit, hasHit ? hit.point : Vector3.zero, hasHit ? hit.normal : Vector3.up);

        if (Time.time - activationTime < activationClickGuardTime)
        {
            return;
        }

        var rightHand = UnityEngine.InputSystem.XR.XRController.rightHand;
        if (rightHand == null)
        {
            return;
        }

        var trigger = rightHand["triggerPressed"] as ButtonControl;
        if (trigger == null || !trigger.wasPressedThisFrame)
        {
            return;
        }

        if (hasHit)
        {
            CreateWaypoint(hit.point, hit.normal);
        }
    }

    public void SetActivation()
    {
        isActivated = !isActivated;
        activationTime = Time.time;

        ApplyPlacementModeState(isActivated);
        SetRayVisualActive(isActivated);
        if (!isActivated)
        {
            UpdateHitIndicator(false, Vector3.zero, Vector3.up);
        }

        if (iconFeedback != null)
        {
            iconFeedback.UpdateIcon(isActivated);
        }

        if (debugLogs)
        {
            Debug.Log($"[WaypointPlacement] Modo creación {(isActivated ? "ACTIVO" : "INACTIVO")}. Waypoints={waypoints.Count}");
        }
    }

    public void SetActivation(bool active)
    {
        if (isActivated == active)
        {
            return;
        }

        SetActivation();
    }

    public bool RemoveWaypoint(GameObject waypoint)
    {
        if (waypoint == null)
        {
            return false;
        }

        return waypoints.Remove(waypoint);
    }

    public void ClearWaypoints(bool destroyGameObjects = false)
    {
        if (destroyGameObjects)
        {
            for (int i = 0; i < waypoints.Count; i++)
            {
                if (waypoints[i] != null)
                {
                    Destroy(waypoints[i]);
                }
            }
        }

        waypoints.Clear();
    }

    private bool TryGetMapHit(out RaycastHit hit)
    {
        Ray ray = new Ray(rightRayOrigin.position, rightRayOrigin.forward);
        return Physics.Raycast(ray, out hit, rayDistance, minimapLayerMask, QueryTriggerInteraction.Ignore);
    }

    private void UpdateRayVisual(Vector3 endPoint)
    {
        EnsurePlacementRayRenderer();

        if (!showPlacementRay || placementRayRenderer == null || rightRayOrigin == null)
        {
            return;
        }

        if (!placementRayRenderer.enabled)
        {
            placementRayRenderer.enabled = true;
        }

        if (placementRayRenderer.positionCount != 2)
        {
            placementRayRenderer.positionCount = 2;
        }

        placementRayRenderer.SetPosition(0, rightRayOrigin.position);
        placementRayRenderer.SetPosition(1, endPoint);
    }

    private void SetRayVisualActive(bool active)
    {
        EnsurePlacementRayRenderer();

        if (placementRayRenderer == null)
        {
            return;
        }

        placementRayRenderer.enabled = active && showPlacementRay;
    }

    private void EnsurePlacementRayRenderer()
    {
        if (placementRayRenderer == null)
        {
            GameObject lineObject = new GameObject("WaypointPlacementRay");
            lineObject.transform.SetParent(transform, false);
            placementRayRenderer = lineObject.AddComponent<LineRenderer>();
        }

        placementRayRenderer.useWorldSpace = true;
        placementRayRenderer.positionCount = 2;
        placementRayRenderer.startWidth = lineWidth;
        placementRayRenderer.endWidth = lineWidth;
        placementRayRenderer.startColor = lineColor;
        placementRayRenderer.endColor = lineColor;
        placementRayRenderer.numCapVertices = 8;
        placementRayRenderer.numCornerVertices = 8;
        placementRayRenderer.textureMode = LineTextureMode.Stretch;
        placementRayRenderer.alignment = LineAlignment.View;
        placementRayRenderer.shadowCastingMode = ShadowCastingMode.Off;
        placementRayRenderer.receiveShadows = false;
        placementRayRenderer.lightProbeUsage = LightProbeUsage.Off;
        placementRayRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        placementRayRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        placementRayRenderer.allowOcclusionWhenDynamic = false;
        placementRayRenderer.material = GetOrCreateXrSafeLineMaterial();
    }

    private Material GetOrCreateXrSafeLineMaterial()
    {
        if (runtimeLineMaterial != null)
        {
            ApplyLineColor(runtimeLineMaterial);
            return runtimeLineMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        if (shader == null)
        {
            Debug.LogWarning("[WaypointPlacement] No se encontro shader para la linea. Se usara el material actual del LineRenderer.");
            return placementRayRenderer != null ? placementRayRenderer.material : null;
        }

        runtimeLineMaterial = new Material(shader)
        {
            name = "WaypointPlacementLineMaterial"
        };

        runtimeLineMaterial.enableInstancing = true;
        ApplyLineColor(runtimeLineMaterial);
        return runtimeLineMaterial;
    }

    private void ApplyLineColor(Material material)
    {
        if (material == null) return;

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", lineColor);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", lineColor);
        }
    }

    private void UpdateHitIndicator(bool active, Vector3 hitPoint, Vector3 hitNormal)
    {
        if (hitIndicator == null)
        {
            return;
        }

        hitIndicator.SetActive(active && isActivated);
        if (!hitIndicator.activeSelf)
        {
            return;
        }

        hitIndicator.transform.position = hitPoint;
        if (hitNormal.sqrMagnitude > 0.0001f)
        {
            hitIndicator.transform.rotation = Quaternion.FromToRotation(Vector3.up, hitNormal);
        }
    }

    private void CreateWaypoint(Vector3 worldPosition, Vector3 surfaceNormal)
    {
        if (waypointPrefab == null)
        {
            Debug.LogWarning("[WaypointPlacement] waypointPrefab no asignado.");
            return;
        }

        if (minimapWaypointsRoot == null)
        {
            Debug.LogWarning("[WaypointPlacement] minimapWaypointsRoot no asignado.");
            return;
        }

        Quaternion rotation = waypointPrefab.transform.rotation;
        if (surfaceNormal.sqrMagnitude > 0.0001f)
        {
            rotation = Quaternion.FromToRotation(Vector3.up, surfaceNormal) * rotation;
        }

        GameObject newWaypoint = Instantiate(waypointPrefab, worldPosition, rotation, minimapWaypointsRoot);

        waypoints.Add(newWaypoint);
        newWaypoint.name = $"Waypoint_{waypoints.Count:000}";

        if (debugLogs)
        {
            Debug.Log($"[WaypointPlacement] Creado {newWaypoint.name} en {worldPosition}.");
        }
    }

    private void ApplyPlacementModeState(bool placing)
    {
        if (behavioursToDisableWhilePlacing != null)
        {
            for (int i = 0; i < behavioursToDisableWhilePlacing.Length; i++)
            {
                var behaviour = behavioursToDisableWhilePlacing[i];
                if (behaviour != null)
                {
                    behaviour.enabled = !placing;
                }
            }
        }

        if (objectsToDisableWhilePlacing != null)
        {
            for (int i = 0; i < objectsToDisableWhilePlacing.Length; i++)
            {
                var obj = objectsToDisableWhilePlacing[i];
                if (obj != null)
                {
                    obj.SetActive(!placing);
                }
            }
        }
    }
}
