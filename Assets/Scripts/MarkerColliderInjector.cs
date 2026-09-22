using UnityEngine;
using Microsoft.MixedReality.OpenXR;

/// <summary>
/// 幫每個新出現的 QR Marker 動態加上 BoxCollider，讓視線 Raycast 打得到。
/// 完全獨立於組員的 StudentQRTracker.cs，不修改他的任何檔案。
/// </summary>
public class MarkerColliderInjector : MonoBehaviour
{
    [Tooltip("跟 StudentQRTracker 用同一個 ARMarkerManager")]
    public ARMarkerManager markerManager;

    private void OnEnable() => markerManager.markersChanged += OnMarkersChanged;
    private void OnDisable() => markerManager.markersChanged -= OnMarkersChanged;

    private void OnMarkersChanged(ARMarkersChangedEventArgs args)
    {
        // 修正：原本只處理 added，但 Unity 不保證多個腳本 OnEnable() 的訂閱順序，
        // 如果 StudentQRTracker 比這支腳本早訂閱到事件，
        // 「第一次偵測到」的 added 事件就可能在這支腳本訂閱完成前已經觸發過、被錯過，
        // 之後 OpenXR 對同一個 marker 只會持續發 updated 事件，
        // 導致 Collider 永遠沒有機會被加上去。
        // 所以 added 跟 updated 都要處理，靠「是否已有 BoxCollider」來判斷要不要加，避免重複加。
        foreach (var marker in args.added)
        {
            AddColliderIfNeeded(marker);
        }

        foreach (var marker in args.updated)
        {
            AddColliderIfNeeded(marker);
        }
    }

    /// <summary>
    /// 如果這個 marker 身上還沒有 BoxCollider，就加一個上去。
    /// 不管是被 added 還是 updated 事件呼叫到，都共用這同一個方法，避免重複加。
    /// </summary>
    private void AddColliderIfNeeded(ARMarker marker)
    {

        if (marker.gameObject.GetComponent<BoxCollider>() == null)
        {
            BoxCollider col = marker.gameObject.AddComponent<BoxCollider>();
            // 不要寫死，直接用系統自動偵測到的marker真實物理尺寸
            // 新增：多留一點容錯空間，補償裝置本身約5~15公分的視線精準度極限
            float padding = 0.08f;
            col.size = new Vector3(marker.size.x + padding, marker.size.y + padding, 0.02f);
            col.center = Vector3.zero;
            Debug.Log($"[偵錯] 已幫 marker「{marker.GetDecodedString()}」加上 BoxCollider");
        }
        Debug.Log($"[診斷] 收到更新: {marker.GetDecodedString()}，時間: {Time.time:F1}");
    }
}