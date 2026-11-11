using UnityEngine;
using Microsoft.MixedReality.OpenXR;

/// <summary>
/// 幫每個 QR Marker 加上視線判定用的 BoxCollider。
///
/// 【v8】判定範圍從「貼在 QR 碼平面上的薄板」改成「以燈號為中心的立方體」：
///   - 老師離開 QR 碼附近後主要是看燈號，判定範圍跟著燈號走比較直覺
///   - 立方體三個軸一樣長，從側面、斜角看也打得中（薄板從側面看幾乎沒有面積）
///   - 燈號高度直接讀同物件上 StudentQRTracker 的 LightOffset，不用兩邊手動同步
/// </summary>
public class MarkerColliderInjector : MonoBehaviour
{
    [Tooltip("跟 StudentQRTracker 用同一個 ARMarkerManager")]
    public ARMarkerManager markerManager;

    [Tooltip("判定方塊邊長（公尺）。燈號含白邊約 6 公分，再加上眼動誤差 5~15 公分")]
    public float cubeSize = 0.18f;

    [Tooltip("找不到 StudentQRTracker 時使用的燈號高度")]
    public float fallbackLightOffset = 0.05f;

    private StudentQRTracker qrTracker;

    private void Awake()
    {
        qrTracker = GetComponent<StudentQRTracker>(); // 兩支腳本都掛在 XR Origin 上
    }

    private void OnEnable() => markerManager.markersChanged += OnMarkersChanged;
    private void OnDisable() => markerManager.markersChanged -= OnMarkersChanged;

    private void OnMarkersChanged(ARMarkersChangedEventArgs args)
    {
        // added 跟 updated 都要處理（訂閱順序不保證，added 可能被錯過）
        foreach (var marker in args.added) UpdateCollider(marker);
        foreach (var marker in args.updated) UpdateCollider(marker);
    }

    private void UpdateCollider(ARMarker marker)
    {
        BoxCollider col = marker.GetComponent<BoxCollider>();
        if (col == null)
        {
            col = marker.gameObject.AddComponent<BoxCollider>();
            Debug.Log($"[MarkerColliderInjector] 已幫 marker「{marker.GetDecodedString()}」加上 BoxCollider");
        }

        float lightOffset = qrTracker != null ? qrTracker.LightOffset : fallbackLightOffset;

        // 跟燈號同一套算法：世界正上方換算成 marker 本地方向。每次 updated 都重算，角度修正後會跟上
        Vector3 localUp = marker.transform.InverseTransformDirection(Vector3.up);
        col.center = localUp * (marker.size.y / 2f + lightOffset);
        col.size = Vector3.one * cubeSize;
    }
}