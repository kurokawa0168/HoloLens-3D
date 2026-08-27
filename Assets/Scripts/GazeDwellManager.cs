using UnityEngine;
using Microsoft.MixedReality.OpenXR;

/// <summary>
/// 視線 Dwell 判斷主控腳本
/// 職責：
///   1. 每幀用 EyeGazeProvider 提供的視線 Ray 做 Physics.Raycast
///   2. 判斷有沒有打中「座位面板」(PanelSeatIdentity)
///   3. 累加/歸零 Dwell 計時，同步驅動 DwellRingUI 的視覺呈現
///   4. Dwell 時間到 → 生成獨立的詳細資訊介面
///
/// 設計原則（對齊今天確認的架構決策）：
///   - 詳細介面是「另外生成的獨立物件」，不掛在燈號面板底下、
///     不寫進組員的 activePanels 字典，自己管理自己的生命週期。
///   - 燈號面板被組員的 QRReader 銷毀時，詳細介面不會被連坐銷毀，
///     要等使用者視線移開介面本身才關閉（第二階段的透明度邏輯，之後接續）。
///
/// 【新增：視線容錯機制】
///   實機測試發現：眼動追蹤本身有生理性的微小抖動(jitter)，
///   只要某一幀視線剛好沒打中目標（哪怕只有一幀），原本的邏輯會立刻把
///   dwellTimer 整個歸零重來，導致「明明一直盯著同一張QR碼看」，
///   卻因為中間偶爾一兩幀的追蹤雜訊，永遠無法連續累積滿 dwellThreshold。
///   加上 missTolerance 這個緩衝時間後：視線短暫離開目標不會立刻清空進度，
///   只有連續失準超過 missTolerance 秒，才會真的重置。
/// </summary>
public class GazeDwellManager : MonoBehaviour
{
    [Header("依賴元件")]
    [Tooltip("提供視線 Ray 的來源")]
    public EyeGazeProvider gazeProvider;

    [Tooltip("視線最大偵測距離（公尺）")]
    public float maxRayDistance = 5f;

    [Header("Dwell 設定")]
    [Tooltip("視線需要停留多久才觸發詳細介面（秒），暫定範圍 0.5~1.5 秒，之後實機測試微調")]
    public float dwellThreshold = 1.0f;

    [Header("詳細介面（第二階段）")]
    [Tooltip("拖入已設定好 CanvasGroup / BoxCollider / DetailPanelController 的 DetailPanel Prefab")]
    public GameObject detailPanelPrefab;

    [Tooltip("拖入場景裡的 StudentDatabase 物件")]
    public StudentDatabase studentDatabase;

    [Tooltip("拖入 Main Camera，詳細介面會生成在它底下，讓介面跟著教師視角走")]
    public Transform mainCameraTransform;

    // 目前正在被注視的座位面板（null 代表目前沒有注視任何面板）
    private ARMarker currentTarget;
    private float dwellTimer = 0f;

    [Header("視線容錯（新增：避免感測器抖動或QR碼邊緣誤差讓Dwell瞬間歸零）")]
    [Tooltip("視線短暫離開目標時，容許的最大中斷時間（秒）。超過這個時間才會真的重置Dwell計時")]
    public float missTolerance = 0.2f;

    // 記錄「連續」沒打中目標已經過了多久，用來跟 missTolerance 比較
    // 注意：只要有任何一幀重新打中目標，這個值就會被歸零（見 HandleGazeOnPanel）
    private float missTimer = 0f;

    // 進度圈的實例（延遲建立，程式啟動時自動生成一個備用）
    private DwellRingUI ringUI;

    // 目前已經開啟的詳細介面，key 是座位編號，避免同一座位重複開兩個介面
    // 這裡先用最簡單的「目前只允許存在一個」邏輯，對齊計畫書「第三階段：只保留最新介面」
    private GameObject currentDetailPanel;
    private string currentDetailSeatId;

    void Start()
    {
        // 動態建立進度圈物件（獨立於 Dwell 的目標面板之外，一開始就準備好，用完隱藏、不 Destroy）
        GameObject ringObj = new GameObject("DwellRing");
        ringUI = ringObj.AddComponent<DwellRingUI>();
        ringUI.Hide();
    }

    void Update()
    {
        if (gazeProvider == null) return;

        // 【診斷用】確認這一幀有沒有拿到有效視線資料
        bool hasGaze = gazeProvider.TryGetGazeRay(out Vector3 origin, out Vector3 direction);
        if (!hasGaze)
        {
            Debug.Log("[診斷] 沒有拿到有效視線資料 (isTracked=false 或裝置未就緒)");
            // 【修改】原本這裡直接呼叫 ResetDwell() 整個歸零，
            // 改成 HandleGazeMiss()：先看看有沒有超過容忍時間，
            // 短暫失去視線資料（比如裝置瞬間追丟）不會馬上清空進度
            HandleGazeMiss();
            return;
        }

        Ray gazeRay = new Ray(origin, direction);

        // 【診斷用】每幀印出視線 Ray,確認起點/方向數值合理（不是全部是0）
        Debug.Log($"[診斷] 視線 Ray origin={origin} direction={direction}");

        if (Physics.Raycast(gazeRay, out RaycastHit hit, maxRayDistance))
        {
            // 【診斷用】確認Raycast到底打中了什麼物件，不管是不是座位面板
            Debug.Log($"[診斷] Raycast 打中物件: {hit.collider.gameObject.name}");

            ARMarker hitMarker = hit.collider.GetComponentInParent<ARMarker>();

            if (hitMarker != null)
            {
                Debug.Log($"[偵錯] 這次打中的物件有 ARMarker 元件，QR內容={hitMarker.GetDecodedString()}");
                HandleGazeOnPanel(hitMarker, hit.point);
                return;
            }
            else
            {
                Debug.Log("[診斷] 打中的物件沒有 PanelSeatIdentity 元件");
            }
        }
        else
        {
            // 【診斷用】確認是不是Raycast距離內完全沒打到任何東西
            Debug.Log($"[診斷] Raycast 沒打中任何東西 (maxRayDistance={maxRayDistance})");
        }

        // 【修改】沒打中任何面板（打到空氣，或打到別的物件）
        // 原本直接 ResetDwell()，改成先進 HandleGazeMiss() 判斷是否在容忍範圍內
        HandleGazeMiss();
    }

    /// <summary>
    /// 視線目前正打在某個座位面板上，處理 Dwell 累加與進度圈顯示
    /// </summary>
    private void HandleGazeOnPanel(ARMarker panel, Vector3 hitPoint)
    {
        // 換了一個新的注視目標 → 計時重新開始（符合計畫書：Dwell 期間視線移開就重置）
        if (currentTarget != panel)
        {
            currentTarget = panel;
            dwellTimer = 0f;
        }

        // 【新增】這一幀真的打中了目標，代表視線是準的，
        // 把失準計時器清零，重新給滿容忍額度，避免「這次沒打中」的舊紀錄殘留影響下一次判斷
        missTimer = 0f;

        dwellTimer += Time.deltaTime;

        // 更新進度圈：位置跟著碰撞點走，填滿度反映 Dwell 進度
        ringUI.Show();
        ringUI.UpdateTransform(hitPoint, Camera.main != null ? Camera.main.transform : null);
        ringUI.SetFillAmount(dwellTimer / dwellThreshold);

        if (dwellTimer >= dwellThreshold)
        {
            TriggerDetailPanel(panel.GetDecodedString(), hitPoint);
            ResetDwell(); // 觸發後歸零，避免同一次注視重複觸發
        }
    }

    /// <summary>
    /// 【新增】這一幀視線沒有打中任何有效目標時呼叫（取代原本直接呼叫 ResetDwell()）。
    /// 不會立刻清空 Dwell 進度，而是先累計「連續失準時間」，
    /// 只有連續失準超過 missTolerance，才會真的呼叫 ResetDwell() 全部歸零。
    /// 這樣可以吸收掉眼動追蹤本身的微小抖動，避免明明一直在盯著同一個目標，
    /// 卻因為偶爾一兩幀的雜訊就前功盡棄、永遠累積不滿 dwellThreshold。
    /// </summary>
    private void HandleGazeMiss()
    {
        // 本來就沒有在追蹤任何目標（currentTarget 已經是 null），不用重複處理
        if (currentTarget == null) return;

        missTimer += Time.deltaTime;

        if (missTimer > missTolerance)
        {
            // 連續失準時間超過容忍範圍，才是真的判定「視線離開了」，全部歸零重來
            ResetDwell();
        }
        // 【重點】在容忍時間內：dwellTimer 維持原值不變、ringUI 也先不隱藏，
        // 讓使用者感覺不到這次短暫的中斷，視線只要飄回目標，就能無縫接續累積進度
    }

    /// <summary>
    /// 視線離開所有面板（或連續失準時間超過容忍範圍）→ 計時歸零、進度圈收起
    /// </summary>
    private void ResetDwell()
    {
        currentTarget = null;
        dwellTimer = 0f;
        missTimer = 0f; // 【新增】連失準計時器也一併歸零，確保狀態完全乾淨，不影響下一次判斷
        if (ringUI != null) ringUI.Hide();
    }

    /// <summary>
    /// Dwell 時間到，開啟該座位的詳細資訊介面
    /// 對齊計畫書第三階段：畫面上同時最多只存在一個詳細介面，新的取代舊的
    /// </summary>
    private void TriggerDetailPanel(string seatId, Vector3 originHitPoint)
    {
        // 已經是同一個座位的介面在顯示 → 不用重新生成
        if (currentDetailPanel != null && currentDetailSeatId == seatId)
        {
            return;
        }

        // 有其他座位的舊介面還開著 → 依計畫書規則，直接關掉，只留最新的
        if (currentDetailPanel != null)
        {
            Destroy(currentDetailPanel);
        }

        // 生成真正的詳細介面 Prefab,掛在 Main Camera 底下,
        // 這樣教師轉頭時,介面會跟著視角走,維持「畫面右側」的相對位置。
        // 之後的透明度升降、alpha 過低自我銷毀,交給 Prefab 上的 DetailPanelController 自己負責,
        // 這裡生成完就不再插手它的生命週期(對齊「獨立管理」的架構決策)。
        if (detailPanelPrefab == null || mainCameraTransform == null)
        {
            Debug.LogError("[GazeDwellManager] detailPanelPrefab 或 mainCameraTransform 尚未在 Inspector 指定,無法生成詳細介面。");
            // 上面已經 Destroy 掉舊介面(如果有的話),這裡要把參照清空,
            // 避免下次比對時誤以為「舊介面還活著」。
            currentDetailPanel = null;
            currentDetailSeatId = null;
            return;
        }

        currentDetailPanel = Instantiate(detailPanelPrefab, mainCameraTransform);
        currentDetailPanel.name = $"DetailPanel_{seatId}";

        // 【關鍵修正】Prefab 資產無法保存「指向場景物件」的參照——
        // EyeTrackingManager 是場景裡獨立的物件,不是 DetailPanel 的子物件,
        // 存成 Prefab 的那一刻,DetailPanelController 上的 eyeGazeProvider 欄位
        // 會被 Unity 自動斷開變成 None。導致每次生成出來的介面都拿不到視線資料,
        // Update() 裡的透明度邏輯整段被提早 return 擋住,alpha 永遠停在初始值 0,
        // 介面因此「生成了但永遠隱形」。
        // 解法:GazeDwellManager 自己手上已經有一份 gazeProvider,生成完直接轉交給新介面。
        DetailPanelController controller = currentDetailPanel.GetComponent<DetailPanelController>();
        if (controller != null)
        {
            controller.eyeGazeProvider = gazeProvider;

            // 第6步新增：查資料庫，把學生姓名/狀態填進面板
            if (studentDatabase != null)
            {
                if (!studentDatabase.IsLoaded)
                {
                    controller.SetContent("資料載入中...", "");
                    return;
                }
                UserRecord record = studentDatabase.GetRecord(seatId);
                if (record != null)
                {
                    controller.SetContent(record.name, record.role == "teacher" ? "教師" : "學生");
                }
                else
                {
                    controller.SetContent("未登錄學生", $"QR: {seatId}");
                }
            }
            else
            {
                Debug.LogError("[GazeDwellManager] 生成的 DetailPanel 找不到 DetailPanelController 元件,請確認 Prefab 設定。");
            }

            // 【已移除位置/角度的強制覆蓋】原本這裡會寫死 localPosition / localRotation,
            // 但使用者已經在 Prefab 上手動微調過理想的位置角度(Pos 0.222,-0.13,1 / Rotation Y 16)
            // 並存進 Prefab 裡,不需要再用程式碼二次覆蓋——直接沿用 Prefab 本身記住的 Transform 即可。
            currentDetailSeatId = seatId;

            // 【一出現就有基本可見度】Dwell 好不容易跑滿,介面不該是全透明,
            // 讓教師還要自己轉頭去找它才會浮現。這裡直接把初始 alpha 設成 0.7(70%),
            // 之後仍然交給 DetailPanelController 持續判斷視線,維持/淡出。
            CanvasGroup initialCanvasGroup = currentDetailPanel.GetComponent<CanvasGroup>();
            if (initialCanvasGroup != null)
            {
                initialCanvasGroup.alpha = 0.7f;
            }

            // ↓↓↓ 給後端合作的位置 ↓↓↓
            // 這裡就是「詳細介面需要顯示的學生資料」該填入的地方。
            // 目前 SeatManager.cs 尚未串接真實資料（回傳寫死的假資料），
            // 之後後端接上即時分析結果後，預期會是類似：
            //
            //   StudentStatus status = BackendClient.GetStudentStatus(seatId);
            //   detailPanelUI.SetContent(status.studentName, status.focusScore, status.distractionCount, ...);
            //
            // 目前先用座位編號本身佔位，方便先跑通「視線觸發流程」，
            // 等後端資料格式確定後再回來這裡替換成真實資料。
            Debug.Log($"[眼動模組] Dwell 觸發，開啟座位 {seatId} 的詳細介面（佔位版本，尚未接後端資料）");
        }
    }
}