using System;
using System.Collections.Generic;
using UnityEngine;

public class ImmersiveModeController : MonoBehaviour
{
    [Serializable]
    public class ImmersiveTarget
    {
        public Transform target;
        public bool useWorldPose = true;
        public Vector3 immersivePosition;
        public Vector3 immersiveEulerAngles;
        public Vector3 immersiveScale = Vector3.one;
    }

    [Header("Estado")]
    public bool isActivated = false;

    [Header("UI Feedback")]
    public ToggleIconFeedback iconFeedback;

    [Header("Mapa inmersivo")]
    [Tooltip("Padre del mapa inmersivo. En tu escena deberia ser InmersiveMap.")]
    public GameObject immersiveMapRoot;

    [Header("Objetos afectados")]
    [Tooltip("Paneles, MiniMap_GoogleMaps1 y ControlPanel_extended. Se guardan al entrar y se restauran al salir.")]
    public ImmersiveTarget[] affectedObjects;

    [Header("Debug")]
    public bool verboseDebugLogs = true;

    private readonly Dictionary<Transform, SavedPose> savedTargetPoses = new Dictionary<Transform, SavedPose>();
    private bool savedImmersiveMapActive;
    private bool hasSavedImmersiveMapState;

    private struct SavedPose
    {
        public Transform Parent;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation;
        public Vector3 LocalScale;

        public SavedPose(Transform source)
        {
            Parent = source.parent;
            LocalPosition = source.localPosition;
            LocalRotation = source.localRotation;
            LocalScale = source.localScale;
        }

        public void Restore(Transform target)
        {
            target.SetParent(Parent, true);
            target.localPosition = LocalPosition;
            target.localRotation = LocalRotation;
            target.localScale = LocalScale;
        }
    }

    private void Start()
    {
        UpdateIcon();
    }

    public void SetActivation()
    {
        isActivated = !isActivated;

        if (isActivated)
        {
            EnterImmersiveMode();
        }
        else
        {
            ExitImmersiveMode();
        }

        UpdateIcon();

        if (verboseDebugLogs)
        {
            Debug.Log($"[ImmersiveModeController] Modo inmersivo {(isActivated ? "ACTIVO" : "INACTIVO")}");
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

    private void EnterImmersiveMode()
    {
        SaveCurrentState();
        ApplyImmersiveTargets();

        if (immersiveMapRoot != null)
        {
            immersiveMapRoot.SetActive(true);
        }
    }

    private void ExitImmersiveMode()
    {
        foreach (KeyValuePair<Transform, SavedPose> pair in savedTargetPoses)
        {
            if (pair.Key != null)
            {
                pair.Value.Restore(pair.Key);
            }
        }

        savedTargetPoses.Clear();

        if (hasSavedImmersiveMapState && immersiveMapRoot != null)
        {
            immersiveMapRoot.SetActive(savedImmersiveMapActive);
        }
    }

    private void SaveCurrentState()
    {
        savedTargetPoses.Clear();

        if (affectedObjects != null)
        {
            foreach (ImmersiveTarget item in affectedObjects)
            {
                if (item != null && item.target != null && !savedTargetPoses.ContainsKey(item.target))
                {
                    savedTargetPoses.Add(item.target, new SavedPose(item.target));
                }
            }
        }

        if (immersiveMapRoot != null)
        {
            savedImmersiveMapActive = immersiveMapRoot.activeSelf;
            hasSavedImmersiveMapState = true;
        }
    }

    private void ApplyImmersiveTargets()
    {
        if (affectedObjects == null)
        {
            return;
        }

        foreach (ImmersiveTarget item in affectedObjects)
        {
            if (item == null || item.target == null)
            {
                continue;
            }

            Quaternion rotation = Quaternion.Euler(item.immersiveEulerAngles);

            if (item.useWorldPose)
            {
                item.target.SetPositionAndRotation(item.immersivePosition, rotation);
                item.target.localScale = item.immersiveScale;
            }
            else
            {
                item.target.localPosition = item.immersivePosition;
                item.target.localRotation = rotation;
                item.target.localScale = item.immersiveScale;
            }
        }
    }

    private void UpdateIcon()
    {
        if (iconFeedback != null)
        {
            iconFeedback.UpdateIcon(isActivated);
        }
    }
}
