using UnityEngine;

/// <summary>
/// 掛在 DetailPanel(詳細資訊介面)上的腳本。
/// 負責這個介面「自己」的生命週期:
///   1. 每一幀問 EyeGazeProvider 拿目前的視線 Ray
///   2. 用這條 Ray 發 Raycast,判斷視線有沒有打中「自己」的 Collider
///   3. 打中 → alpha 往上升(變清楚);沒打中 → alpha 往下降(變透明)
///   4. alpha 低於閾值 → 自我銷毀(Destroy自己)
///
/// 設計理念(對照《進度更新》文件的關鍵決策):
///   詳細介面是「完全獨立管理生命週期」的物件,不依賴 GazeDwellManager
///   每幀記得來管它。GazeDwellManager 只負責「生成」,生成之後這顆腳本
///   自己負責「活多久、什麼時候該死」。
/// </summary>
public class DetailPanelController : MonoBehaviour
{
    [Header("視線來源")]
    [Tooltip("拖入場景裡的 EyeTrackingManager 物件(掛著 EyeGazeProvider)。" +
             "不自己重新問 OpenXR API,而是問這個共用的 Provider," +
             "避免同一幀裡不同腳本拿到的視線資料不一致。")]
    public EyeGazeProvider eyeGazeProvider;

    [Header("透明度控制(對應計畫書第二階段)")]
    [Tooltip("每秒 alpha 變化的速度。數值越大,淡入/淡出越快。")]
    public float fadeSpeed = 1.0f;

    [Tooltip("alpha 低於這個值時,介面直接自我銷毀。對應計畫書『低於20%關閉』。")]
    [Range(0f, 1f)]
    public float destroyThreshold = 0.2f;

    [Tooltip("視線暫時沒打中面板時，可以容忍幾秒鐘不要馬上開始變透明（避免眼動雜訊誤判）。")]
public float missTolerance = 0.3f;

private float missTimer = 0f;

    // 這個物件自己身上的 CanvasGroup,用來讀寫 alpha。
    // 不用 public 拖曳,因為它一定是掛在同一個物件上,Start() 時自己抓就好。
    private CanvasGroup canvasGroup;

    [Header("保護時間（修正：避免剛生成就因視線還沒移過來而消失）")]
    [Tooltip("面板生成後這段時間內，就算視線還沒看到它，也不會開始淡出，讓使用者有時間把視線從QR碼移過來")]
    public float graceSeconds = 1.2f;

    private float spawnTime;

    [Header("內容顯示（第6步新增）")]
    [Tooltip("拖入 DetailPanel 底下的 StudentName 文字物件")]
    public TMPro.TMP_Text studentNameText;

    [Tooltip("拖入 DetailPanel 底下的 StatusDuration 文字物件")]
    public TMPro.TMP_Text statusText;

    void Start()
    {
        // GetComponent<T>():向自己這個 GameObject 要「T 這種元件」的參照。
        // 這一步是必要的——宣告變數只是留了一個空位,
        // 要透過 GetComponent 才會真的指向場景中那個實體元件。
        canvasGroup = GetComponent<CanvasGroup>();

        spawnTime = Time.time;

        if (canvasGroup == null)
        {
            // 防呆:如果忘記在 Prefab 上加 CanvasGroup,這裡會提早在 Console 報錯,
            // 而不是等到 Update() 執行時才因為 null 而整個當掉,方便除錯。
            Debug.LogError("[DetailPanelController] 找不到 CanvasGroup 元件,請確認 Prefab 設定。");
        }

        if (eyeGazeProvider == null)
        {
            Debug.LogError("[DetailPanelController] eyeGazeProvider 尚未指定,請在 Inspector 拖入。");
        }
    }

    void Update()
    {
        // 防呆:缺任何一個依賴就不執行邏輯,避免 NullReferenceException 讓遊戲整個停住。
        if (canvasGroup == null || eyeGazeProvider == null)
        {
            return;
        }

        // GazeCheckResult 有三種可能:
        //   HitSelf   → 視線打中自己 → alpha 上升
        //   HitOther / Missed → 視線沒打中自己(打到別的東西,或完全沒打到任何東西) → alpha 下降
        //   NoGazeData → 這一幀視線資料本身就無效(眨眼、暫時遺失追蹤) → 維持原狀,不升不降
        // 特別處理 NoGazeData,是為了避免使用者只是眨了一下眼,
        // 介面就被誤判成「沒在看」而開始閃爍變淡。
        GazeCheckResult result = CheckGazeAgainstSelf();

        // Time.deltaTime = 上一幀到這一幀經過的秒數。
        // 用「速度 × deltaTime」而不是直接加減固定值,
        // 是為了讓 alpha 變化速度跟畫面更新快慢(FPS)無關,
        // 不會因為裝置效能不同而淡入淡出速度忽快忽慢。
        bool inGracePeriod = (Time.time - spawnTime) < graceSeconds;

        if (result == GazeCheckResult.HitSelf)
        {
            canvasGroup.alpha += fadeSpeed * Time.deltaTime;
        }
        else if (result == GazeCheckResult.Missed && !inGracePeriod)
        {
            // 保護時間內即使沒被看到，也先不淡出，給使用者反應時間
            canvasGroup.alpha -= fadeSpeed * Time.deltaTime;
        }
        // result == GazeCheckResult.NoGazeData 時,什麼都不做,alpha 維持原值。

        // Mathf.Clamp01:把 alpha 強制夾在 0~1 之間,
        // 避免因為連續好幾幀往同一個方向加/減,數值衝出合理範圍(例如變成 -0.4 或 1.7)。
        canvasGroup.alpha = Mathf.Clamp01(canvasGroup.alpha);

        // 對應計畫書:「透明度低於20% → 直接刪除/關閉介面」
        if (canvasGroup.alpha <= destroyThreshold)
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 這一幀視線判定的三種可能結果。
    /// 拆成三態(而不是單純的 true/false),是為了把「沒打中我」跟
    /// 「這一幀根本沒有有效視線資料(眨眼/暫時遺失追蹤)」分開處理——
    /// 後者不該被當成「使用者不想看了」。
    /// </summary>
    private enum GazeCheckResult
    {
        HitSelf,      // 視線打中自己
        Missed,       // 視線有效,但沒打中自己(打到別的東西,或什麼都沒打到)
        NoGazeData    // 這一幀 EyeGazeProvider 給不出有效視線資料
    }

    /// <summary>
    /// 向 EyeGazeProvider 要這一幀的視線 Ray,再拿這條 Ray 去發 Raycast,
    /// 判斷有沒有打中「自己」的 Collider。
    ///
    /// 注意:這裡沒有做 Dwell 計時累加,因為那是 GazeDwellManager 在
    /// 「第一階段開介面」時已經做過的事。這裡只回答「這一幀的狀態」,
    /// 由 Update() 直接對應成 alpha 該往哪個方向走一小步、還是維持原值。
    /// </summary>
    private GazeCheckResult CheckGazeAgainstSelf()
    {
        // EyeGazeProvider 給的是 TryGetGazeRay(out origin, out direction),
        // 回傳 bool 代表「這一幀視線資料是否有效追蹤中」——
        // 不是原本猜測的 GetGazeRay() 直接回傳 Ray。
        bool hasValidGaze = eyeGazeProvider.TryGetGazeRay(out Vector3 origin, out Vector3 direction);

        if (!hasValidGaze)
        {
            // 使用者可能眨眼,或視線暫時遺失追蹤,這一幀不代表「沒在看」。
            return GazeCheckResult.NoGazeData;
        }

        Ray gazeRay = new Ray(origin, direction);

        RaycastHit hit;
        if (Physics.Raycast(gazeRay, out hit))
        {
            // 場景裡可能同時存在燈號面板、其他物件的 Collider,
            // 這裡要確認 Raycast 真正打中的物件「就是我自己」,不是別的東西。
            if (hit.collider.gameObject == this.gameObject)
            {
                return GazeCheckResult.HitSelf;
            }
        }

        return GazeCheckResult.Missed;
    }

    /// <summary>
    /// 由 GazeDwellManager 在生成這個面板後呼叫，把學生資料填進去。
    /// </summary>
    public void SetContent(string studentName, string status)
    {
        if (studentNameText != null) studentNameText.text = studentName;
        if (statusText != null) statusText.text = status;
    }
}