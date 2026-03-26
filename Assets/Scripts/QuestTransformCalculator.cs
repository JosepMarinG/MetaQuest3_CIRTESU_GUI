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

    private TF_Suscriber tfSubscriber;
    private string tfWorldFrame;
    private string tfToolFrame;

    private UnityEngine.Vector3 anchorPosition;
    private UnityEngine.Quaternion anchorRotation;
    private UnityEngine.Vector3 worldReferencePositionUnity;
    private UnityEngine.Quaternion worldReferenceRotationUnity;
    private bool hasWorldTfReference;

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
        UnityEngine.Vector3 localDeltaPos = UnityEngine.Quaternion.Inverse(anchorRotation) * worldDeltaPos;
        UnityEngine.Quaternion deltaRotUnity = UnityEngine.Quaternion.Inverse(anchorRotation) * currentRotation;
        deltaRotUnity = UnityEngine.Quaternion.Normalize(deltaRotUnity);

        UnityEngine.Vector3 targetPositionUnity = localDeltaPos;
        UnityEngine.Quaternion targetRotationUnity = deltaRotUnity;
        string outputFrameId = localFrameId;

        if (hasWorldTfReference)
        {
            targetPositionUnity = worldReferencePositionUnity + (worldReferenceRotationUnity * localDeltaPos);
            targetRotationUnity = worldReferenceRotationUnity * deltaRotUnity;
            targetRotationUnity = UnityEngine.Quaternion.Normalize(targetRotationUnity);
            outputFrameId = worldFrameId;
        }

        result = new PoseComputationResult
        {
            UnityTargetPosition = targetPositionUnity,
            UnityTargetRotation = targetRotationUnity,
            RosPosition = ConvertUnityVectorToRos(targetPositionUnity),
            RosRotation = ConvertUnityQuaternionToRos(targetRotationUnity),
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

        if (!subscriber.TryGetTransform(tfWorldFrame, tfToolFrame, out TF_Suscriber.TFData tfData))
        {
            if (!subscriber.TryGetTransform(tfToolFrame, tfWorldFrame, out TF_Suscriber.TFData inverseTfData))
            {
                Debug.LogWarning($"[QuestTransformCalculator] No se encontro TF ni directa ni inversa entre '{tfWorldFrame}' y '{tfToolFrame}'.");
                return false;
            }

            UnityEngine.Vector3 inversePositionUnity = ConvertRosVectorToUnity(inverseTfData.Translation);
            UnityEngine.Quaternion inverseRotationUnity = UnityEngine.Quaternion.Normalize(ConvertRosQuaternionToUnity(inverseTfData.Rotation));

            worldReferenceRotationUnity = UnityEngine.Quaternion.Inverse(inverseRotationUnity);
            worldReferencePositionUnity = -(worldReferenceRotationUnity * inversePositionUnity);
            return true;
        }

        worldReferencePositionUnity = ConvertRosVectorToUnity(tfData.Translation);
        worldReferenceRotationUnity = UnityEngine.Quaternion.Normalize(ConvertRosQuaternionToUnity(tfData.Rotation));
        return true;
    }

    private UnityEngine.Vector3 ConvertUnityVectorToRos(UnityEngine.Vector3 unityVector)
    {
        return new UnityEngine.Vector3(unityVector.z, -unityVector.x, unityVector.y);
    }

    private UnityEngine.Quaternion ConvertUnityQuaternionToRos(UnityEngine.Quaternion unityQuaternion)
    {
        return new UnityEngine.Quaternion(unityQuaternion.z, -unityQuaternion.x, unityQuaternion.y, unityQuaternion.w);
    }

    private UnityEngine.Vector3 ConvertRosVectorToUnity(UnityEngine.Vector3 rosVector)
    {
        return new UnityEngine.Vector3(-rosVector.y, rosVector.z, rosVector.x);
    }

    private UnityEngine.Quaternion ConvertRosQuaternionToUnity(UnityEngine.Quaternion rosQuaternion)
    {
        return new UnityEngine.Quaternion(-rosQuaternion.y, rosQuaternion.z, rosQuaternion.x, rosQuaternion.w);
    }
}
