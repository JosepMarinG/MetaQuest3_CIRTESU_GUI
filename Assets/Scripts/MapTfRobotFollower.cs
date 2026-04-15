using UnityEngine;
using System.Collections.Generic;

public class MapTfRobotFollower : MonoBehaviour
{
    [Header("Estado")]
    public bool isActivated = false;

    [Header("Referencias")]
    public TF_Suscriber tfSubscriber;
    public ToggleIconFeedback iconFeedback;
    [Tooltip("Transform de referencia que representa el frame ROS world_ned ya corregido al sistema de ejes de Unity.")]
    public Transform worldNedReference;

    [Header("Frames TF")]
    public string worldFrame = "world_ned";
    public string robotBaseFrame = "girona500/base_link";
    public string endEffectorFrame = "girona500/bravo/gripper/camera";
    public string goalFrame = "girona500/goal_position";

    [Header("Objetos a mover")]
    [Tooltip("Raiz visual del robot en el minimapa. Se movera con girona500/base_link.")]
    public Transform robotRoot;

    [Tooltip("Axis para visualizar la TF de girona500/base_link.")]
    public Transform baseLinkAxis;

    [Tooltip("Axis para visualizar la TF de girona500/bravo/gripper/camera.")]
    public Transform endEffectorAxis;

    [Tooltip("Axis para visualizar la TF de girona500/goal_position.")]
    public Transform goalAxis;

    [Header("Aplicacion de Pose")]
    [Tooltip("Si esta activo, se aplica pose local (recomendado cuando todo cuelga del objeto mapa).")]
    public bool applyAsLocalPose = true;

    [Tooltip("Si esta activo, tambien se mueve robotRoot con la TF base_link.")]
    public bool moveRobotRoot = true;

    [Tooltip("Evita aplicar TF a objetos que sean ancestros de la camara XR para no mover el mundo/jugador por error.")]
    public bool preventMovingCameraAncestors = true;

    [Tooltip("Frecuencia maxima de refresco de visualizacion (Hz). 0 = cada frame.")]
    [Min(0f)] public float updateRateHz = 30f;

    [Header("Debug")]
    public bool hideAxisWhenTfMissing = false;
    public bool logMissingTf = false;
    public bool logRotationConversion = false;
    [Min(0f)] public float rotationLogIntervalSeconds = 0.5f;

    private float nextUpdateTime;
    private bool loggedMissingSubscriber;
    private readonly HashSet<int> warnedCameraAncestorTargets = new HashSet<int>();
    private readonly Dictionary<int, float> nextRotationLogByTarget = new Dictionary<int, float>();

    private void Start()
    {
        UpdateIcon();
        SetAxisVisibility(isActivated || !hideAxisWhenTfMissing);
    }

    private void Update()
    {
        if (!isActivated)
        {
            if (hideAxisWhenTfMissing)
            {
                SetAxisVisibility(false);
            }
            return;
        }

        TF_Suscriber subscriber = tfSubscriber != null ? tfSubscriber : TF_Suscriber.Instance;
        if (subscriber == null)
        {
            if (!loggedMissingSubscriber)
            {
                Debug.LogWarning("[MapTfRobotFollower] No hay TF_Suscriber asignado ni singleton activo.");
                loggedMissingSubscriber = true;
            }
            return;
        }

        loggedMissingSubscriber = false;

        if (updateRateHz > 0f && Time.unscaledTime < nextUpdateTime)
        {
            return;
        }

        if (updateRateHz > 0f)
        {
            nextUpdateTime = Time.unscaledTime + (1f / updateRateHz);
        }

        bool anyResolved = false;

        if (TryGetTf(subscriber, worldFrame, robotBaseFrame, out TF_Suscriber.TFData baseTf))
        {
            anyResolved = true;
            ApplyTf(baseLinkAxis, baseTf);

            if (moveRobotRoot)
            {
                ApplyTf(robotRoot, baseTf);
            }
        }

        if (TryGetTf(subscriber, worldFrame, endEffectorFrame, out TF_Suscriber.TFData eeTf))
        {
            anyResolved = true;
            ApplyTf(endEffectorAxis, eeTf);
        }

        if (TryGetTf(subscriber, worldFrame, goalFrame, out TF_Suscriber.TFData goalTf))
        {
            anyResolved = true;
            ApplyTf(goalAxis, goalTf);
        }

        if (hideAxisWhenTfMissing)
        {
            SetAxisVisibility(anyResolved);
        }
    }

    public void SetActivation()
    {
        isActivated = !isActivated;
        ApplyActivationState();
    }

    public void SetActivation(bool active)
    {
        if (isActivated == active)
        {
            return;
        }

        isActivated = active;
        ApplyActivationState();
    }

    private void ApplyActivationState()
    {
        UpdateIcon();

        if (!isActivated && hideAxisWhenTfMissing)
        {
            SetAxisVisibility(false);
        }
        else if (isActivated)
        {
            SetAxisVisibility(true);
        }
    }

    private void UpdateIcon()
    {
        if (iconFeedback != null)
        {
            iconFeedback.UpdateIcon(isActivated);
        }
    }

    private bool TryGetTf(TF_Suscriber subscriber, string parentFrame, string childFrame, out TF_Suscriber.TFData tfData)
    {
        if (subscriber.TryGetTransform(parentFrame, childFrame, out tfData))
        {
            return true;
        }

        if (subscriber.TryGetTransform(childFrame, parentFrame, out TF_Suscriber.TFData inverseTf))
        {
            InvertTf(inverseTf, out tfData);
            tfData.ParentFrame = NormalizeFrame(parentFrame);
            tfData.ChildFrame = NormalizeFrame(childFrame);
            return true;
        }

        if (logMissingTf)
        {
            Debug.LogWarning($"[MapTfRobotFollower] TF no disponible: '{parentFrame}' -> '{childFrame}'.");
        }

        return false;
    }

    private static void InvertTf(TF_Suscriber.TFData source, out TF_Suscriber.TFData inverted)
    {
        Quaternion normalized = source.Rotation == default ? Quaternion.identity : Quaternion.Normalize(source.Rotation);
        Quaternion invRotation = Quaternion.Inverse(normalized);
        Vector3 invTranslation = -(invRotation * source.Translation);

        inverted = source;
        inverted.Translation = invTranslation;
        inverted.Rotation = invRotation;
    }

    private void ApplyTf(Transform target, TF_Suscriber.TFData tfData)
    {
        if (target == null)
        {
            return;
        }

        if (preventMovingCameraAncestors && IsAncestorOfMainCamera(target))
        {
            int id = target.GetInstanceID();
            if (!warnedCameraAncestorTargets.Contains(id))
            {
                Debug.LogWarning($"[MapTfRobotFollower] Se ignora TF sobre '{target.name}' porque es ancestro de la camara. Esto evita desplazar el mundo/jugador accidentalmente.");
                warnedCameraAncestorTargets.Add(id);
            }
            return;
        }

        Vector3 unityLocalPos = new Vector3(-tfData.Translation.y * 0.1f, tfData.Translation.z * 0.1f, tfData.Translation.x * 0.1f); // Escalamos
        Quaternion unityLocalRot = ConvertRosToUnityRotation(tfData.Rotation);

        TryLogRotationConversion(target, tfData, unityLocalRot);

        // Si el padre es la referencia, esto es equivalente a lo que tenías antes
        // pero mucho más frágil si cambias algo en el Editor.
        target.localPosition = unityLocalPos;
        target.localRotation = unityLocalRot;

        /*// ROS: +X adelante, +Y izquierda, +Z arriba
        // Unity: +Z adelante, -X izquierda, +Y arriba
        Vector3 unityPos = new Vector3(-tfData.Translation.y, tfData.Translation.z, tfData.Translation.x);
        Quaternion unityRot = Quaternion.Normalize(new Quaternion(
            -tfData.Rotation.y,
            tfData.Rotation.z,
            tfData.Rotation.x,
            tfData.Rotation.w));

        // Usamos la referencia corregida solo como origen y orientacion.
        // No aplicamos su escala al desplazamiento de la TF.
        Vector3 worldPosition = worldNedReference != null
            ? worldNedReference.position + (worldNedReference.rotation * unityPos)
            : unityPos;

        Quaternion worldRotation = worldNedReference != null
            ? Quaternion.Normalize(worldNedReference.rotation * unityRot)
            : unityRot;

        if (applyAsLocalPose && target.parent != null)
        {
            target.localPosition = target.parent.InverseTransformPoint(worldPosition);
            target.localRotation = Quaternion.Normalize(Quaternion.Inverse(target.parent.rotation) * worldRotation);
        }
        else
        {
            target.SetPositionAndRotation(worldPosition, worldRotation);
        }*/
    }

    private void SetAxisVisibility(bool visible)
    {
        SetTransformVisibility(baseLinkAxis, visible);
        SetTransformVisibility(endEffectorAxis, visible);
        SetTransformVisibility(goalAxis, visible);
    }

    private static Quaternion ConvertRosToUnityRotation(Quaternion rosRotation)
    {
        Quaternion normalizedRos = rosRotation == default ? Quaternion.identity : Quaternion.Normalize(rosRotation);

        // Convertimos orientacion ROS (+X delante, +Y izquierda, +Z arriba)
        // a ejes Unity (+Z delante, +Y arriba, +X derecha) mediante vectores base.
        Vector3 rosForward = normalizedRos * Vector3.right;
        Vector3 rosUp = normalizedRos * Vector3.forward;

        Vector3 unityForward = RosToUnityVector(rosForward);
        Vector3 unityUp = RosToUnityVector(rosUp);

        if (unityForward.sqrMagnitude < 1e-8f || unityUp.sqrMagnitude < 1e-8f)
        {
            return Quaternion.identity;
        }

        return Quaternion.Normalize(Quaternion.LookRotation(unityForward.normalized, unityUp.normalized));
    }

    private static Vector3 RosToUnityVector(Vector3 rosVector)
    {
        return new Vector3(-rosVector.y, rosVector.z, rosVector.x);
    }

    private void TryLogRotationConversion(Transform target, TF_Suscriber.TFData tfData, Quaternion unityRotation)
    {
        if (!logRotationConversion || target == null)
        {
            return;
        }

        float interval = rotationLogIntervalSeconds > 0f ? rotationLogIntervalSeconds : 0f;
        int targetId = target.GetInstanceID();

        if (interval > 0f &&
            nextRotationLogByTarget.TryGetValue(targetId, out float nextLogTime) &&
            Time.unscaledTime < nextLogTime)
        {
            return;
        }

        nextRotationLogByTarget[targetId] = Time.unscaledTime + interval;

        Quaternion rosRotation = tfData.Rotation == default ? Quaternion.identity : Quaternion.Normalize(tfData.Rotation);
        Debug.Log(
            $"[MapTfRobotFollower][Rot] target='{target.name}', frame='{tfData.ParentFrame}'->'{tfData.ChildFrame}', " +
            $"ROS q=({rosRotation.x:F5}, {rosRotation.y:F5}, {rosRotation.z:F5}, {rosRotation.w:F5}) | " +
            $"Unity q=({unityRotation.x:F5}, {unityRotation.y:F5}, {unityRotation.z:F5}, {unityRotation.w:F5})");
    }

    private static void SetTransformVisibility(Transform target, bool visible)
    {
        if (target != null && target.gameObject.activeSelf != visible)
        {
            target.gameObject.SetActive(visible);
        }
    }

    private static string NormalizeFrame(string frame)
    {
        if (string.IsNullOrWhiteSpace(frame))
        {
            return string.Empty;
        }

        return frame.Trim().TrimStart('/');
    }

    private static bool IsAncestorOfMainCamera(Transform candidate)
    {
        if (candidate == null || Camera.main == null)
        {
            return false;
        }

        Transform cameraTransform = Camera.main.transform;
        return cameraTransform == candidate || cameraTransform.IsChildOf(candidate);
    }
}
