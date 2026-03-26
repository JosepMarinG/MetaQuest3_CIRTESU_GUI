using UnityEngine;

public class PanelWindowManager : MonoBehaviour
{
    [Header("Prefabs de los Paneles")]
    public GameObject monoPanelPrefab;
    public GameObject stereoPanelPrefab;
    [Header("Mapa en Escena")]
    public GameObject mapa3d;
    public ToggleIconFeedback mapaIconFeedback;
    public GameObject GironaPrefab;
    public GameObject CirteSubPrefab;
    public GameObject CatamaranPrefab;

    [Header("Punto de Aparici�n")]
    public Transform spawnPoint; // Ponlo a 1 metro delante de la c�mara (OVRCameraRig)

    private void Start()
    {
        SyncMapaIcon();
    }

    public void SpawnMonoPanel()
    {
        InstantiatePanel(monoPanelPrefab);
    }

    public void SpawnStereoPanel()
    {
        InstantiatePanel(stereoPanelPrefab);
    }
    public void SpawnMapa3d()
    {
        ToggleMapa3d();
    }

    public void SpawnGirona()
    {
        InstantiatePanel(GironaPrefab);
    }

    public void SpawnCirteSub()
    {
        InstantiatePanel(CirteSubPrefab);
    }

    public void SpawnCatamaran()
    {
        InstantiatePanel(CatamaranPrefab);
    }

    public void ToggleMapa3d()
    {
        if (mapa3d == null) return;
        mapa3d.SetActive(!mapa3d.activeSelf);
        SyncMapaIcon();
    }

    private void SyncMapaIcon()
    {
        if (mapaIconFeedback == null) return;
        mapaIconFeedback.UpdateIcon(mapa3d != null && mapa3d.activeSelf);
    }

    private void InstantiatePanel(GameObject prefab)
    {
        if (prefab == null) return;

        // Si no hay spawnPoint, lo creamos delante de la c�mara
        Vector3 pos = spawnPoint != null ? spawnPoint.position : Camera.main.transform.position + Camera.main.transform.forward * 1.5f;
        Quaternion rot = spawnPoint != null ? spawnPoint.rotation : Quaternion.LookRotation(Camera.main.transform.forward);

        GameObject newPanel = Instantiate(prefab, pos, rot);

        // Corregir rotaci�n para que mire al usuario
        newPanel.transform.LookAt(Camera.main.transform);
        newPanel.transform.Rotate(0, 180, 0);
    }
}