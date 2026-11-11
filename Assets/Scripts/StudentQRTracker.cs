using UnityEngine;
using Microsoft.MixedReality.OpenXR;
using TMPro;

public class StudentQRTracker : MonoBehaviour
{
    [SerializeField] private ARMarkerManager markerManager;
    [SerializeField] private StudentDatabase studentDatabase;
    [SerializeField] private StudentDetailData studentDetailData; // 用來取得紅黃綠燈 (scoreColor)

    [Header("燈號設定")]
    [Tooltip("拖入用 Universal Render Pipeline/Unlit 建立的材質")]
    [SerializeField] private Material indicatorMaterial;

    [Tooltip("燈號球心在 QR 碼上緣再往上多少公尺（MarkerColliderInjector 會自動讀這個值）")]
    [SerializeField] private float lightOffset = 0.05f;

    [Tooltip("燈號球直徑（公尺）")]
    [SerializeField] private float lightSize = 0.04f;

    [Tooltip("白邊直徑相對於燈號球的倍率")]
    [SerializeField] private float outlineScale = 1.4f;

    [Header("姓名設定")]
    [Tooltip("總開關：關掉就完全不顯示姓名")]
    [SerializeField] private bool showNameLabel = true;

    [Tooltip("比這個距離近就不顯示名字（太近直接看得到人）")]
    [SerializeField] private float nameMinDistance = 1.2f;

    [Tooltip("比這個距離遠就不顯示名字（太遠畫面會太雜）")]
    [SerializeField] private float nameMaxDistance = 3.5f;

    [Tooltip("緩衝距離：已顯示的名字要超出區間這麼多才隱藏，避免在邊界附近一直閃")]
    [SerializeField] private float nameHideMargin = 0.1f;

    [Tooltip("姓名字體大小（原本 Prefab 是 36）")]
    [SerializeField] private float nameFontSize = 36f;

    [Tooltip("姓名中心在燈號球心上方多少公尺")]
    [SerializeField] private float nameAboveLight = 0.06f;

    [Tooltip("距離越遠字越大，讓遠處的名字看起來跟近處差不多大")]
    [SerializeField] private bool scaleWithDistance = false;

    [Tooltip("scaleWithDistance 的基準距離：在這個距離時字是原始大小")]
    [SerializeField] private float referenceDistance = 1.5f;

    // Prefab 上 Text (TMP) 原本的縮放
    private const float BaseTextScale = 0.015f;

    /// <summary>燈號高度，給 MarkerColliderInjector 讀取，避免兩邊數值不一致</summary>
    public float LightOffset => lightOffset;

    private void OnEnable()
    {
        markerManager.markersChanged += OnMarkersChanged;
        studentDatabase.OnDataChanged += RefreshAllLabels;
        if (studentDetailData != null)
        {
            studentDetailData.OnDataChanged += RefreshAllLabels;
        }
    }

    private void OnDisable()
    {
        markerManager.markersChanged -= OnMarkersChanged;
        studentDatabase.OnDataChanged -= RefreshAllLabels;
        if (studentDetailData != null)
        {
            studentDetailData.OnDataChanged -= RefreshAllLabels;
        }
    }

    private void OnMarkersChanged(ARMarkersChangedEventArgs args)
    {
        foreach (var marker in args.added)
        {
            SetupMarkerVisuals(marker);
        }
        foreach (var marker in args.updated)
        {
            SetupMarkerVisuals(marker);
        }
    }

    private void RefreshAllLabels()
    {
        foreach (var marker in markerManager.trackables)
        {
            SetupMarkerVisuals(marker);
        }
    }

    /// <summary>
    /// 每幀處理姓名：距離在區間內才顯示，並讓姓名在燈號正上方、面向配戴者。
    /// </summary>
    private void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 camPos = cam.transform.position;

        foreach (var marker in markerManager.trackables)
        {
            var text = marker.GetComponentInChildren<TextMeshPro>(true);
            if (text == null) continue;

            Vector3 lightPos = GetLightWorldPosition(marker);
            float dist = Vector3.Distance(camPos, lightPos);

            bool visible = false;
            if (showNameLabel)
            {
                bool wasVisible = text.gameObject.activeSelf;
                // 已顯示 → 超出區間＋緩衝才隱藏；未顯示 → 進入區間才顯示
                visible = wasVisible
                    ? dist >= nameMinDistance - nameHideMargin && dist <= nameMaxDistance + nameHideMargin
                    : dist >= nameMinDistance && dist <= nameMaxDistance;
            }

            if (text.gameObject.activeSelf != visible) text.gameObject.SetActive(visible);
            if (!visible) continue;

            Transform t = text.transform;

            // 位置：燈號正上方（世界座標的上方）
            t.position = lightPos + Vector3.up * nameAboveLight;

            // 朝向：面向配戴者
            Vector3 lookDir = t.position - camPos;
            if (lookDir.sqrMagnitude > 0.0001f)
                t.rotation = Quaternion.LookRotation(lookDir, Vector3.up);

            // 大小：可選擇距離越遠字越大
            float scale = scaleWithDistance ? BaseTextScale * (dist / referenceDistance) : BaseTextScale;
            t.localScale = Vector3.one * scale;
        }
    }

    private Vector3 GetLightWorldPosition(ARMarker marker)
    {
        return marker.transform.position + Vector3.up * (marker.size.y / 2f + lightOffset);
    }

    private void SetupMarkerVisuals(ARMarker marker)
    {
        string qrCode = marker.GetDecodedString();

        // 1. 姓名文字：只放名字、單行（顯示與位置交給 LateUpdate）
        var text = marker.GetComponentInChildren<TextMeshPro>(true);
        if (text != null)
        {
            text.text = GetNameOnly(qrCode);
            text.fontSize = nameFontSize;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.alignment = TextAlignmentOptions.Center;
        }

        // 2. 燈號
        Transform indicatorTransform = marker.transform.Find("StatusIndicatorLight");
        Renderer indicatorRenderer;

        if (indicatorTransform == null)
        {
            GameObject lightObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lightObj.name = "StatusIndicatorLight";
            lightObj.transform.SetParent(marker.transform, false);
            lightObj.transform.localScale = Vector3.one * lightSize;

            Collider col = lightObj.GetComponent<Collider>();
            if (col != null) Destroy(col); // 視線判定由 MarkerColliderInjector 的方塊負責

            indicatorRenderer = lightObj.GetComponent<Renderer>();
            ApplyIndicatorMaterial(indicatorRenderer);

            CreateOutline(lightObj.transform);
            indicatorTransform = lightObj.transform;
        }
        else
        {
            indicatorRenderer = indicatorTransform.GetComponent<Renderer>();
        }

        // 用世界座標正上方定位（實機上 QR 碼本地 +Y 是朝下的）
        Vector3 localUp = marker.transform.InverseTransformDirection(Vector3.up);
        indicatorTransform.localPosition = localUp * (marker.size.y / 2f + lightOffset);

        // 3. 依 scoreColor 改顏色
        if (indicatorRenderer != null && studentDetailData != null)
        {
            var detailRecord = studentDetailData.GetRecord(qrCode);
            Color c = Color.gray; // 資料未到或狀態不明
            if (detailRecord != null)
            {
                switch (detailRecord.scoreColor?.ToLower())
                {
                    case "red": c = Color.red; break;
                    case "yellow": c = Color.yellow; break;
                    case "green": c = Color.green; break;
                }
            }
            indicatorRenderer.material.color = c;
        }
    }

    /// <summary>
    /// 只取姓名（原本的 GetDisplayText 會多一行「(學生)」）
    /// </summary>
    private string GetNameOnly(string qrCode)
    {
        if (!studentDatabase.IsLoaded) return "載入中...";
        var record = studentDatabase.GetRecord(qrCode);
        return record != null ? record.name : $"未登錄: {qrCode}";
    }

    private void CreateOutline(Transform lightTransform)
    {
        GameObject outline = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        outline.name = "IndicatorOutline";
        outline.transform.SetParent(lightTransform, false);
        outline.transform.localPosition = Vector3.zero;
        outline.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        outline.transform.localScale = new Vector3(outlineScale, 0.01f, outlineScale);

        Collider col = outline.GetComponent<Collider>();
        if (col != null) Destroy(col);

        Renderer r = outline.GetComponent<Renderer>();
        ApplyIndicatorMaterial(r);
        r.material.color = Color.white;
    }

    private void ApplyIndicatorMaterial(Renderer r)
    {
        if (indicatorMaterial != null)
        {
            r.material = new Material(indicatorMaterial);
        }
        else
        {
            Debug.LogWarning("[StudentQRTracker] indicatorMaterial 未指定，改用預設材質。");
        }
    }
}