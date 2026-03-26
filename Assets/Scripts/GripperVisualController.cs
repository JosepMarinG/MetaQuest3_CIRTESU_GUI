using UnityEngine;

public class GripperVisualController : MonoBehaviour
{
    [Header("Referencias de dedos")]
    public Transform leftFinger;
    public Transform rightFinger;

    [Header("Rotacion en eje X (grados locales)")]
    public float openAngle = 0f;
    public float closedAngle = 25f;
    public float leftFingerDirection = 1f;
    public float rightFingerDirection = -1f;

    [Header("Suavizado")]
    public bool smoothMotion = true;
    public float degreesPerSecond = 240f;

    private bool targetClosed;
    private float leftFingerInitialAngle;
    private float rightFingerInitialAngle;

    public void SetClosed(bool closed)
    {
        targetClosed = closed;

        if (!smoothMotion)
        {
            ApplyStateInstant();
        }
    }

    private void Awake()
    {
        if (leftFinger != null)
        {
            leftFingerInitialAngle = NormalizeAngle(leftFinger.localEulerAngles.x);
        }

        if (rightFinger != null)
        {
            rightFingerInitialAngle = NormalizeAngle(rightFinger.localEulerAngles.x);
        }

        ApplyStateInstant();
    }

    private void Update()
    {
        if (!smoothMotion)
        {
            return;
        }

        float targetAngle = targetClosed ? closedAngle : openAngle;
        UpdateFingerTowards(leftFinger, leftFingerInitialAngle + (targetAngle * leftFingerDirection));
        UpdateFingerTowards(rightFinger, rightFingerInitialAngle + (targetAngle * rightFingerDirection));
    }

    private void ApplyStateInstant()
    {
        float targetAngle = targetClosed ? closedAngle : openAngle;
        ApplyFingerAngle(leftFinger, leftFingerInitialAngle + (targetAngle * leftFingerDirection));
        ApplyFingerAngle(rightFinger, rightFingerInitialAngle + (targetAngle * rightFingerDirection));
    }

    private void UpdateFingerTowards(Transform finger, float targetLocalX)
    {
        if (finger == null) return;

        Vector3 euler = finger.localEulerAngles;
        float currentX = NormalizeAngle(euler.x);
        float nextX = Mathf.MoveTowards(currentX, targetLocalX, degreesPerSecond * Time.deltaTime);
        euler.x = nextX;
        finger.localEulerAngles = euler;
    }

    private void ApplyFingerAngle(Transform finger, float targetLocalX)
    {
        if (finger == null) return;

        Vector3 euler = finger.localEulerAngles;
        euler.x = targetLocalX;
        finger.localEulerAngles = euler;
    }

    private float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle > 180f) angle -= 360f;
        return angle;
    }
}
