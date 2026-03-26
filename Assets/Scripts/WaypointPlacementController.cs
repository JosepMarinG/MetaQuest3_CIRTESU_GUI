using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.Controls;

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

    public IReadOnlyList<GameObject> Waypoints => waypoints;

    private void Start()
    {
        ApplyPlacementModeState(false);
        if (iconFeedback != null)
        {
            iconFeedback.UpdateIcon(isActivated);
        }
    }

    private void Update()
    {
        if (!isActivated)
        {
            return;
        }

        if (rightRayOrigin == null)
        {
            if (debugLogs)
            {
                Debug.LogWarning("[WaypointPlacement] rightRayOrigin no asignado.");
            }
            return;
        }

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

        if (TryGetMapHit(out RaycastHit hit))
        {
            CreateWaypoint(hit.point, hit.normal);
        }
    }

    public void SetActivation()
    {
        isActivated = !isActivated;
        activationTime = Time.time;

        ApplyPlacementModeState(isActivated);

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
