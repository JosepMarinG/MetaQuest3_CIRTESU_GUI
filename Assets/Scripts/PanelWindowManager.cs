using UnityEngine;

public class PanelWindowManager : MonoBehaviour
{
    [Header("Prefabs de los Paneles")]
    public GameObject monoPanelPrefab;
    public GameObject stereoPanelPrefab;

    [Header("Punto de Aparición")]
    public Transform spawnPoint; // Ponlo a 1 metro delante de la cámara (OVRCameraRig)

    public void SpawnMonoPanel()
    {
        InstantiatePanel(monoPanelPrefab);
    }

    public void SpawnStereoPanel()
    {
        InstantiatePanel(stereoPanelPrefab);
    }

    private void InstantiatePanel(GameObject prefab)
    {
        if (prefab == null) return;

        // Si no hay spawnPoint, lo creamos delante de la cámara
        Vector3 pos = spawnPoint != null ? spawnPoint.position : Camera.main.transform.position + Camera.main.transform.forward * 1.5f;
        Quaternion rot = spawnPoint != null ? spawnPoint.rotation : Quaternion.LookRotation(Camera.main.transform.forward);

        GameObject newPanel = Instantiate(prefab, pos, rot);

        // Corregir rotación para que mire al usuario
        newPanel.transform.LookAt(Camera.main.transform);
        newPanel.transform.Rotate(0, 180, 0);
    }
}