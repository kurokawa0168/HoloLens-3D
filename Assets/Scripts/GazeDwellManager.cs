using Microsoft.MixedReality.OpenXR;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

/// <summary>
/// 視線 Dwell 判斷與詳細面板管理。
///
/// 【v7 修改】
///   - 支援同時開多個面板（最多 maxPanels 個，超過時關掉最舊的）
///   - 新增眼動關閉鈕：盯著面板右上角的 X 跑滿 closeDwellThreshold 秒 → 關閉該面板
///   - 面板改為「跟隨 QR 碼、蓋在燈號前面」，位置交給 DetailPanelController 每幀計算
///   - 面板不再是 marker 的子物件：marker 被系統移除時，面板留在原地，交給「離開畫面」規則處理
///
/// Raycast 判斷優先順序：
///   1. 打中某面板的關閉鈕 → 累積「關閉」Dwell
///   2. 打中面板本體      → 視為在讀面板，不累積任何 Dwell
///   3. 打中 QR 碼        → 若該座位面板已開，暫停；否則累積「開啟」Dwell
/// </summary>
public class GazeDwellManager : MonoBehaviour
{
    [Header("依賴元件")]
    public EyeGazeProvider gazeProvider;
    public float maxRayDistance = 5f;

    [Header("Dwell 設定")]
    [Tooltip("盯 QR 碼多久開啟面板（秒）")]
    public float dwellThreshold = 1.0f;

    [Tooltip("盯關閉鈕多久關閉面板（秒）")]
    public float closeDwellThreshold = 0.8f;

    [Tooltip("視線短暫離開目標時，容許的最大中斷時間（秒）")]
    public float missTolerance = 0.2f;

    [Header("詳細面板")]
    public GameObject detailPanelPrefab;
    public StudentDetailData studentDetailData;

    [Tooltip("（目前未使用，保留欄位避免 Inspector 參照遺失）")]
    public Transform mainCameraTransform;

    [Tooltip("同時最多開幾個面板，超過時關掉最舊的")]
    public int maxPanels = 3;

    [Header("面板位置")]
    [Tooltip("燈號球心在 QR 碼上緣再往上多少公尺，要跟 StudentQRTracker 的 lightOffset 一致")]
    public float lightOffsetAboveMarker = 0.05f;

    [Tooltip("面板中心相對燈號的上下偏移（公尺）。0 = 面板中心對準燈號")]
    public float panelVerticalOffset = 0f;

    [Tooltip("面板往攝影機方向拉近多少公尺，確保蓋在燈號前面")]
    public float panelForwardOffset = 0.06f;

    [Tooltip("面板大小倍率（1 = Prefab 原始大小 30x40 公分）")]
    public float panelScaleMultiplier = 1.0f;

    // 目前累積 Dwell 的目標：可能是 ARMarker（開啟），也可能是 DetailPanelController（關閉）
    private Component currentTarget;
    private float dwellTimer = 0f;
    private float missTimer = 0f;

    private DwellRingUI ringUI;

    // 已開啟的面板：座位編號 → 面板。openOrder 記錄開啟順序，最前面是最舊的
    private readonly Dictionary<string, DetailPanelController> openPanels = new Dictionary<string, DetailPanelController>();
    private readonly List<string> openOrder = new List<string>();

    void Start()
    {
        GameObject ringObj = new GameObject("DwellRing");
        ringUI = ringObj.AddComponent<DwellRingUI>();
        ringUI.Hide();
    }

    void OnEnable()
    {
        if (studentDetailData != null) studentDetailData.OnDataChanged += RefreshAllPanels;
    }

    void OnDisable()
    {
        if (studentDetailData != null) studentDetailData.OnDataChanged -= RefreshAllPanels;
    }

    void Update()
    {
        CleanupDestroyedPanels();

        if (gazeProvider == null) return;

        if (!gazeProvider.TryGetGazeRay(out Vector3 origin, out Vector3 direction))
        {
            HandleGazeMiss();
            return;
        }

        if (Physics.Raycast(new Ray(origin, direction), out RaycastHit hit, maxRayDistance))
        {
            // 1 & 2：打中面板（必須在找 ARMarker 之前判斷）
            DetailPanelController panel = hit.collider.GetComponentInParent<DetailPanelController>();
            if (panel != null)
            {
                if (hit.collider == panel.CloseButtonCollider && !panel.IsClosing)
                {
                    HandleGazeOnTarget(panel, hit.point, closeDwellThreshold);
                }
                else
                {
                    HandleGazeMiss();
                }
                return;
            }

            // 3：打中 QR 碼
            ARMarker marker = hit.collider.GetComponentInParent<ARMarker>();
            if (marker != null)
            {
                if (openPanels.ContainsKey(marker.GetDecodedString()))
                {
                    HandleGazeMiss(); // 這個座位的面板已經開著 → 暫停
                }
                else
                {
                    HandleGazeOnTarget(marker, hit.point, dwellThreshold);
                }
                return;
            }
        }

        HandleGazeMiss();
    }

    private void HandleGazeOnTarget(Component target, Vector3 hitPoint, float threshold)
    {
        if (currentTarget != target)
        {
            currentTarget = target;
            dwellTimer = 0f;
        }

        missTimer = 0f;
        dwellTimer += Time.deltaTime;

        ringUI.Show();
        ringUI.UpdateTransform(hitPoint, Camera.main != null ? Camera.main.transform : null);
        ringUI.SetFillAmount(dwellTimer / threshold);

        if (dwellTimer >= threshold)
        {
            if (target is ARMarker marker) OpenPanel(marker);
            else if (target is DetailPanelController panel) panel.Close();

            ResetDwell();
        }
    }

    private void HandleGazeMiss()
    {
        if (currentTarget == null) return;

        missTimer += Time.deltaTime;
        if (missTimer > missTolerance) ResetDwell();
    }

    private void ResetDwell()
    {
        currentTarget = null;
        dwellTimer = 0f;
        missTimer = 0f;
        if (ringUI != null) ringUI.Hide();
    }

    private void OpenPanel(ARMarker marker)
    {
        string seatId = marker.GetDecodedString();
        if (openPanels.ContainsKey(seatId)) return;

        if (detailPanelPrefab == null)
        {
            Debug.LogError("[GazeDwellManager] detailPanelPrefab 尚未在 Inspector 指定。");
            return;
        }

        // 超過上限 → 關掉最舊的（直接銷毀，不淡出，避免畫面同時出現 4 個）
        while (openOrder.Count >= maxPanels)
        {
            string oldest = openOrder[0];
            if (openPanels.TryGetValue(oldest, out var oldPanel) && oldPanel != null)
                Destroy(oldPanel.gameObject);
            openPanels.Remove(oldest);
            openOrder.RemoveAt(0);
        }

        // 不設父物件：marker 被移除時面板不會連坐消失
        GameObject go = Instantiate(detailPanelPrefab);
        go.name = $"DetailPanel_{seatId}";
        go.transform.localScale *= panelScaleMultiplier;

        DetailPanelController controller = go.GetComponent<DetailPanelController>();
        if (controller == null)
        {
            Debug.LogError("[GazeDwellManager] DetailPanel Prefab 上沒有 DetailPanelController。");
            Destroy(go);
            return;
        }

        controller.eyeGazeProvider = gazeProvider; // Prefab 無法保存場景參照，要手動轉交

        float heightAboveCenter = marker.size.y / 2f + lightOffsetAboveMarker + panelVerticalOffset;
        controller.Initialize(marker.transform, heightAboveCenter, panelForwardOffset);

        openPanels[seatId] = controller;
        openOrder.Add(seatId);

        FillPanelContent(controller, seatId);
    }

    /// <summary>
    /// 面板自己淡出銷毀後，從字典裡移除（Unity 物件被 Destroy 後 == null 會成立）
    /// </summary>
    private void CleanupDestroyedPanels()
    {
        for (int i = openOrder.Count - 1; i >= 0; i--)
        {
            string id = openOrder[i];
            if (!openPanels.TryGetValue(id, out var p) || p == null)
            {
                openPanels.Remove(id);
                openOrder.RemoveAt(i);
            }
        }
    }

    private void RefreshAllPanels()
    {
        foreach (var pair in openPanels)
        {
            if (pair.Value != null) FillPanelContent(pair.Value, pair.Key);
        }
    }

    private void FillPanelContent(DetailPanelController controller, string seatId)
    {
        if (studentDetailData == null)
        {
            Debug.LogError("[GazeDwellManager] studentDetailData 尚未在 Inspector 指定。");
            return;
        }

        if (!studentDetailData.IsLoaded)
        {
            controller.SetContent("資料載入中...", "", 0f, 0f, 0f, 0f);
            controller.SetStatusColor(null);
            return;
        }

        StudentDetailRecord record = studentDetailData.GetRecord(seatId);
        if (record != null)
        {
            controller.SetContent(
                record.name,
                record.scoreColor,
                record.effectiveParticipationRate,
                1f - record.quizAccuracy, // 後端只有正確率，反推錯題率
                record.repeatWatchRatio,
                record.quizParticipation
            );
            controller.SetStatusColor(record.scoreColor);
        }
        else
        {
            controller.SetContent("未登錄學生", $"QR: {seatId}", 0f, 0f, 0f, 0f);
            controller.SetStatusColor(null);
        }
    }
}