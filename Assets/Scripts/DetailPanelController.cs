using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 掛在 DetailPanel 上，負責面板自己的位置、外觀與生命週期。
///   - 位置：跟著 QR 碼走，中心對準燈號，往攝影機方向拉近，蓋在燈號前面
///   - QR 碼被系統移除時，面板停在最後位置，交給「離開畫面」規則處理
///   - 右上角自動產生關閉鈕，由 GazeDwellManager 判斷視線 Dwell 後呼叫 Close()
///   - 存活：在畫面內就維持；可見面積不足超過 offscreenSeconds 秒才淡出
///   - 淡出：快速降低透明度，到 destroyThreshold 直接刪除（沿用原版的俐落消失感）
/// </summary>
public class DetailPanelController : MonoBehaviour
{
    [Header("視線來源（保留欄位，由 GazeDwellManager 指定）")]
    public EyeGazeProvider eyeGazeProvider;

    [Header("存活判斷（畫面內維持）")]
    [Tooltip("可見面積比例低於這個值就算離開畫面。0.25 ≈ 3/4 跑出畫面")]
    [Range(0f, 1f)]
    public float minVisibleFraction = 0.25f;

    [Tooltip("離開畫面連續超過幾秒才開始淡出")]
    public float offscreenSeconds = 5f;

    [Range(0f, 1f)]
    public float visibleAlpha = 0.9f;

    [Header("淡入淡出")]
    [Tooltip("淡入速度（每秒 alpha 變化量）")]
    public float fadeSpeed = 2f;

    [Tooltip("淡出速度（每秒 alpha 變化量），比淡入快，消失比較俐落")]
    public float fadeOutSpeed = 3f;

    [Tooltip("淡出到這個透明度就直接刪除（原版做法：20% 直接消失）")]
    [Range(0f, 1f)]
    public float destroyThreshold = 0.2f;

    [Header("關閉鈕（Canvas 單位，面板是 300x400）")]
    public float closeButtonSize = 60f;
    public Color closeButtonColor = new Color(0f, 0f, 0f, 0.55f);

    [Header("背景（跟燈號變色）")]
    [Tooltip("沒拖會自動找名為 Image 的子物件")]
    public Image backgroundImage;
    public Color redColor = new Color(0.60f, 0.12f, 0.12f, 1f);
    public Color yellowColor = new Color(0.60f, 0.48f, 0.05f, 1f);
    public Color greenColor = new Color(0.10f, 0.45f, 0.18f, 1f);
    public Color unknownColor = new Color(0.25f, 0.28f, 0.45f, 1f);

    [Header("內容顯示")]
    public TMPro.TMP_Text studentNameText;
    public TMPro.TMP_Text statusText;
    public TMPro.TMP_Text participationText;
    public TMPro.TMP_Text errorRateText;
    public TMPro.TMP_Text repeatWatchText;
    public TMPro.TMP_Text quizParticipationText;

    /// <summary>GazeDwellManager 用來判斷視線是否打中關閉鈕</summary>
    public Collider CloseButtonCollider { get; private set; }
    public bool IsClosing => closing;

    private CanvasGroup canvasGroup;
    private RectTransform rectTransform;
    private readonly Vector3[] corners = new Vector3[4];
    private float offscreenTimer = 0f;
    private bool closing = false;

    // 跟隨用
    private Transform anchor;
    private float heightAboveAnchor;
    private float forwardOffset;
    private Vector3 lastBasePosition;
    private bool hasBasePosition = false;

    void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        rectTransform = GetComponent<RectTransform>();

        if (backgroundImage == null)
        {
            Transform bg = transform.Find("Image");
            if (bg != null) backgroundImage = bg.GetComponent<Image>();
        }

        if (canvasGroup == null)
            Debug.LogError("[DetailPanelController] 找不到 CanvasGroup 元件，請確認 Prefab 設定。");

        CreateCloseButton();
    }

    /// <summary>
    /// 由 GazeDwellManager 生成面板後呼叫。
    /// </summary>
    public void Initialize(Transform markerTransform, float heightAboveMarkerCenter, float towardCameraOffset)
    {
        anchor = markerTransform;
        heightAboveAnchor = heightAboveMarkerCenter;
        forwardOffset = towardCameraOffset;
        UpdatePose(); // 生成當幀就放到正確位置，避免閃一下
    }

    /// <summary>
    /// 開始淡出（眼動關閉鈕或離開畫面太久都會呼叫）
    /// </summary>
    public void Close()
    {
        closing = true;
        if (CloseButtonCollider != null) CloseButtonCollider.enabled = false;
    }

    void LateUpdate()
    {
        if (canvasGroup == null) return;
        Camera cam = Camera.main;
        if (cam == null) return;

        UpdatePose();

        if (!closing)
        {
            bool inView = GetVisibleFraction(cam) >= minVisibleFraction;

            if (inView)
            {
                offscreenTimer = 0f;
                canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, visibleAlpha, fadeSpeed * Time.deltaTime);
                return;
            }

            offscreenTimer += Time.deltaTime;
            if (offscreenTimer >= offscreenSeconds) Close();
            return;
        }

        // 快速淡出，到門檻直接刪除
        canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, 0f, fadeOutSpeed * Time.deltaTime);
        if (canvasGroup.alpha <= destroyThreshold) Destroy(gameObject);
    }

    /// <summary>
    /// 位置＝燈號位置（QR 中心往世界正上方）＋往攝影機拉近；朝向＝面對攝影機
    /// </summary>
    private void UpdatePose()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        if (anchor != null)
        {
            lastBasePosition = anchor.position + Vector3.up * heightAboveAnchor;
            hasBasePosition = true;
        }
        // anchor 被系統移除（== null）→ 沿用最後位置

        if (hasBasePosition)
        {
            Vector3 toCam = cam.transform.position - lastBasePosition;
            if (toCam.sqrMagnitude > 0.0001f)
                transform.position = lastBasePosition + toCam.normalized * forwardOffset;
        }

        Vector3 lookDir = transform.position - cam.transform.position;
        if (lookDir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
    }

    /// <summary>
    /// 在面板右上角產生關閉鈕：半透明黑底 + X + BoxCollider。
    /// Collider 比面板本體更靠近攝影機、也更厚，確保視線先打中它。
    /// </summary>
    private void CreateCloseButton()
    {
        GameObject btn = new GameObject("CloseButton", typeof(RectTransform));
        btn.transform.SetParent(transform, false);

        RectTransform rt = btn.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.sizeDelta = new Vector2(closeButtonSize, closeButtonSize);
        rt.anchoredPosition3D = new Vector3(-10f, -10f, -15f); // 負 Z = 朝攝影機方向

        Image img = btn.AddComponent<Image>();
        img.color = closeButtonColor;

        GameObject textGo = new GameObject("X", typeof(RectTransform));
        textGo.transform.SetParent(btn.transform, false);
        RectTransform trt = textGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;

        var tmp = textGo.AddComponent<TMPro.TextMeshProUGUI>();
        if (studentNameText != null) tmp.font = studentNameText.font;
        tmp.text = "X";
        tmp.fontSize = closeButtonSize * 0.6f;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.color = Color.white;

        BoxCollider col = btn.AddComponent<BoxCollider>();
        col.size = new Vector3(closeButtonSize, closeButtonSize, 40f);
        col.center = Vector3.zero;
        CloseButtonCollider = col;
    }

    private float GetVisibleFraction(Camera cam)
    {
        if (rectTransform == null)
        {
            Vector3 v = cam.WorldToViewportPoint(transform.position);
            return (v.z > 0 && v.x >= 0 && v.x <= 1 && v.y >= 0 && v.y <= 1) ? 1f : 0f;
        }

        rectTransform.GetWorldCorners(corners);

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        for (int i = 0; i < 4; i++)
        {
            Vector3 v = cam.WorldToViewportPoint(corners[i]);
            if (v.z <= 0f) return 0f;
            minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x);
            minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y);
        }

        float area = (maxX - minX) * (maxY - minY);
        if (area <= 0f) return 0f;

        float overlapW = Mathf.Max(0f, Mathf.Min(maxX, 1f) - Mathf.Max(minX, 0f));
        float overlapH = Mathf.Max(0f, Mathf.Min(maxY, 1f) - Mathf.Max(minY, 0f));
        return (overlapW * overlapH) / area;
    }

    public void SetStatusColor(string scoreColor)
    {
        if (backgroundImage == null) return;

        switch (scoreColor?.ToLower())
        {
            case "red": backgroundImage.color = redColor; break;
            case "yellow": backgroundImage.color = yellowColor; break;
            case "green": backgroundImage.color = greenColor; break;
            default: backgroundImage.color = unknownColor; break;
        }
    }

    public void SetContent(string studentName, string status, float participation, float errorRate, float repeatWatch, float quizParticipation)
    {
        if (studentNameText != null) studentNameText.text = studentName;
        if (statusText != null) statusText.text = status;
        if (participationText != null) participationText.text = $"參與度: {participation * 100f:F0}%";
        if (errorRateText != null) errorRateText.text = $"錯題率: {errorRate * 100f:F0}%";
        if (repeatWatchText != null) repeatWatchText.text = $"重複觀看: {repeatWatch * 100f:F0}%";
        if (quizParticipationText != null) quizParticipationText.text = $"測驗參與率: {quizParticipation * 100f:F0}%";
    }
}