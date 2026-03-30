using UnityEngine;

public class QuestTransformCalculator : MonoBehaviour
{
    public struct PoseComputationResult
    {
        public UnityEngine.Vector3 UnityTargetPosition;
        public UnityEngine.Quaternion UnityTargetRotation;
        public UnityEngine.Vector3 RosPosition;
        public UnityEngine.Quaternion RosRotation;
        public string OutputFrameId;
        public bool HasWorldReference;
    }

    public bool HasWorldTfReference => hasWorldTfReference;

    [Header("Debug")]
    [SerializeField] private bool verboseTfLookupLogs = false;
    [SerializeField] private float tfLookupLogIntervalSeconds = 1f;

    [Header("XR to Tool Frame Conversion")]
    [SerializeField] private UnityEngine.Quaternion xrToToolAxisConversion = UnityEngine.Quaternion.identity;

    private TF_Suscriber tfSubscriber;
    private string tfWorldFrame;
    private string tfToolFrame;

    private UnityEngine.Vector3 anchorPosition;
    private UnityEngine.Quaternion anchorRotation;
    private UnityEngine.Vector3 worldReferencePositionUnity;
    private UnityEngine.Quaternion worldReferenceRotationUnity;
    private bool hasWorldTfReference;
    private float nextTfLookupLogTime;

    public void BeginControl(
        UnityEngine.Vector3 newAnchorPosition,
        UnityEngine.Quaternion newAnchorRotation,
        TF_Suscriber subscriber,
        string worldFrame,
        string toolFrame)
    {
        anchorPosition = newAnchorPosition;
        anchorRotation = newAnchorRotation;
        tfSubscriber = subscriber;
        tfWorldFrame = worldFrame;
        tfToolFrame = toolFrame;
        nextTfLookupLogTime = 0f;

        hasWorldTfReference = TryCaptureWorldReferenceFromTf();
    }

    public bool TryComputeTargetPose(
        UnityEngine.Vector3 currentPosition,
        UnityEngine.Quaternion currentRotation,
        bool requireTfAtStart,
        string localFrameId,
        string worldFrameId,
        out PoseComputationResult result,
        out string blockReason)
    {
        result = default;
        blockReason = string.Empty;

        if (!hasWorldTfReference)
        {
            hasWorldTfReference = TryCaptureWorldReferenceFromTf();
        }

        if (requireTfAtStart && !hasWorldTfReference)
        {
            blockReason = $"[TransformCalculator] Esperando TF: {tfWorldFrame} -> {tfToolFrame}";
            return false;
        }

        UnityEngine.Vector3 worldDeltaPos = currentPosition - anchorPosition;
        UnityEngine.Vector3 localDeltaPosXr = UnityEngine.Quaternion.Inverse(anchorRotation) * worldDeltaPos;
        UnityEngine.Vector3 localDeltaPos = xrToToolAxisConversion * localDeltaPosXr;
        localDeltaPos.x = -localDeltaPos.x;
        localDeltaPos.y = -localDeltaPos.y;
        localDeltaPos.z = -localDeltaPos.z;

        UnityEngine.Quaternion deltaRotXr = UnityEngine.Quaternion.Inverse(anchorRotation) * currentRotation;
        UnityEngine.Quaternion deltaRotUnity = xrToToolAxisConversion * deltaRotXr * UnityEngine.Quaternion.Inverse(xrToToolAxisConversion);
        //deltaRotUnity.z = -deltaRotUnity.z;
        deltaRotUnity = UnityEngine.Quaternion.Normalize(deltaRotUnity);

        UnityEngine.Vector3 targetPositionUnity = localDeltaPos;
        UnityEngine.Quaternion targetRotationUnity = deltaRotUnity;
        string outputFrameId = localFrameId;

        if (hasWorldTfReference)
        {
            // Keep the same convention as TF input so the first published pose matches TF exactly.
            UnityEngine.Vector3 worldDeltaFromLocal = worldReferenceRotationUnity * localDeltaPos;
            targetPositionUnity = worldReferencePositionUnity + worldDeltaFromLocal;
            targetRotationUnity = worldReferenceRotationUnity * deltaRotUnity; 
            targetRotationUnity = UnityEngine.Quaternion.Normalize(targetRotationUnity);
            outputFrameId = worldFrameId;
        }

        result = new PoseComputationResult
        {
            UnityTargetPosition = targetPositionUnity,
            UnityTargetRotation = targetRotationUnity,
            RosPosition = targetPositionUnity,
            RosRotation = targetRotationUnity,
            OutputFrameId = outputFrameId,
            HasWorldReference = hasWorldTfReference
        };

        return true;
    }

    private bool TryCaptureWorldReferenceFromTf()
    {
        TF_Suscriber subscriber = tfSubscriber != null ? tfSubscriber : TF_Suscriber.Instance;
        if (subscriber == null)
        {
            Debug.LogWarning("[QuestTransformCalculator] TF_Suscriber instance es null (ni asignado ni singleton).");
            return false;
        }

        if (!subscriber.IsReady)
        {
            Debug.LogWarning($"[QuestTransformCalculator] TF_Suscriber no esta listo (IsReady=false). Mensajes={subscriber.TotalTfMessages}, Updates={subscriber.TotalTransformUpdates}, Links={subscriber.UniqueTransformCount}.");
            return false;
        }

        LogTfLookup(
            $"Solicitud TF '{tfWorldFrame}' -> '{tfToolFrame}'. IsReady={subscriber.IsReady}, Mensajes={subscriber.TotalTfMessages}, Updates={subscriber.TotalTransformUpdates}, Links={subscriber.UniqueTransformCount}.");

        if (!subscriber.TryGetTransform(tfWorldFrame, tfToolFrame, out TF_Suscriber.TFData tfData))
        {
            LogTfLookup($"No se encontro TF directa '{tfWorldFrame}' -> '{tfToolFrame}'. Intentando inversa.");

            if (!subscriber.TryGetTransform(tfToolFrame, tfWorldFrame, out TF_Suscriber.TFData inverseTfData))
            {
                Debug.LogWarning($"[QuestTransformCalculator] No se encontro TF ni directa ni inversa entre '{tfWorldFrame}' y '{tfToolFrame}'.");
                return false;
            }

            LogTfLookup(
                $"TF inversa encontrada '{inverseTfData.ParentFrame}' -> '{inverseTfData.ChildFrame}': pos={FormatVector3(inverseTfData.Translation)}, rot={FormatQuaternion(inverseTfData.Rotation)}.",
                true);

            worldReferenceRotationUnity = UnityEngine.Quaternion.Inverse(UnityEngine.Quaternion.Normalize(inverseTfData.Rotation));
            worldReferencePositionUnity = -(worldReferenceRotationUnity * inverseTfData.Translation);

            LogTfLookup(
                $"Referencia mundo calculada desde inversa: pos={FormatVector3(worldReferencePositionUnity)}, rot={FormatQuaternion(worldReferenceRotationUnity)}.",
                true);
            return true;
        }

        worldReferencePositionUnity = tfData.Translation;
        worldReferenceRotationUnity = UnityEngine.Quaternion.Normalize(tfData.Rotation);

        LogTfLookup(
            $"TF directa encontrada '{tfData.ParentFrame}' -> '{tfData.ChildFrame}': pos={FormatVector3(tfData.Translation)}, rot={FormatQuaternion(tfData.Rotation)}.",
            true);
        return true;
    }

    private void LogTfLookup(string message, bool force = false)
    {
        if (!verboseTfLookupLogs)
        {
            return;
        }

        float interval = tfLookupLogIntervalSeconds > 0f ? tfLookupLogIntervalSeconds : 0f;
        if (!force && interval > 0f && UnityEngine.Time.unscaledTime < nextTfLookupLogTime)
        {
            return;
        }

        nextTfLookupLogTime = UnityEngine.Time.unscaledTime + interval;
        Debug.Log($"[QuestTransformCalculator] {message}");
    }

    private static string FormatVector3(UnityEngine.Vector3 value)
    {
        return $"({value.x:F4}, {value.y:F4}, {value.z:F4})";
    }

    private static string FormatQuaternion(UnityEngine.Quaternion value)
    {
        return $"({value.x:F4}, {value.y:F4}, {value.z:F4}, {value.w:F4})";
    }
}
