using UnityEngine;

public class GazeDebugDot : MonoBehaviour
{
    public EyeGazeProvider eyeGazeProvider;
    private GameObject debugDot;

    void Start()
    {
        // 建立一顆小紅球，代表「系統認為視線打中的位置」
        debugDot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        debugDot.name = "GazeDebugDot";
        debugDot.transform.localScale = Vector3.one * 0.03f; // 3公分小球

        // 重要：一定要把這顆球自己的Collider移除，不然它會擋到自己的Raycast！
        Destroy(debugDot.GetComponent<Collider>());

        var renderer = debugDot.GetComponent<Renderer>();
        renderer.material = new Material(Shader.Find("Unlit/Color"));
        renderer.material.color = Color.red;
    }

    void Update()
    {
        if (eyeGazeProvider == null) return;

        if (eyeGazeProvider.TryGetGazeRay(out Vector3 origin, out Vector3 direction))
        {
            debugDot.SetActive(true);
            Ray ray = new Ray(origin, direction);

            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                debugDot.transform.position = hit.point;
                Debug.Log($"[視線除錯] 打中: {hit.collider.gameObject.name}");
            }
            else
            {
                // 沒打中任何東西時，球固定顯示在視線方向2公尺處
                debugDot.transform.position = origin + direction * 2f;
            }
        }
        else
        {
            debugDot.SetActive(false);
        }
    }
}