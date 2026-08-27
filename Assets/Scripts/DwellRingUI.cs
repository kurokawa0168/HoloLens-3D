using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Dwell 進度圈（視覺呈現）
/// 職責單一：只負責「畫出一個空心圓，隨填滿度變化」，
/// 不做任何 Dwell 計時判斷 —— 判斷邏輯交給 GazeDwellManager。
///
/// 顯示方式：World Space Canvas + Image(Filled, Radial360)，
/// 貼圖用程式動態產生一張圓形，不需要額外美術素材。
/// </summary>
public class DwellRingUI : MonoBehaviour
{
    private Image ringImage;
    private Canvas canvas;

    private const float ringWorldSize = 0.15f; // 進度圈在世界空間的實際直徑（單位：公尺）
    // 【測試階段暫時放大】原本 0.03（3公分）太小，遠端串流下很難察覺，
    // 先放大到 0.15（15公分）方便肉眼確認效果，正式使用時再依實測手感縮小

    void Awake()
    {
        BuildRingVisual();
    }

    /// <summary>
    /// 用程式產生一張圓形貼圖，並組出 World Space 的 Canvas + Image
    /// </summary>
    private void BuildRingVisual()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        RectTransform rt = canvas.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(128, 128);
        transform.localScale = Vector3.one * (ringWorldSize / 128f);

        GameObject imgObj = new GameObject("RingImage", typeof(RectTransform));
        imgObj.transform.SetParent(transform, false);
        RectTransform imgRt = imgObj.GetComponent<RectTransform>();
        imgRt.sizeDelta = new Vector2(128, 128);
        imgRt.anchoredPosition = Vector2.zero;

        ringImage = imgObj.AddComponent<Image>();
        ringImage.sprite = GenerateRingSprite();
        ringImage.type = Image.Type.Filled;
        ringImage.fillMethod = Image.FillMethod.Radial360;
        ringImage.fillOrigin = (int)Image.Origin360.Top;
        ringImage.fillClockwise = true;
        ringImage.fillAmount = 0f;
        ringImage.color = new Color(1f, 0.2f, 0.2f, 1f); // 【測試階段】用鮮紅色，避免跟灰白背景混在一起看不清楚
    }

    /// <summary>
    /// 程式產生一張「環狀」貼圖（中間空心的圓環），純數學畫出來，不用外部圖檔
    /// </summary>
    private Sprite GenerateRingSprite()
    {
        int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float outerRadius = size / 2f - 2f;
        float innerRadius = outerRadius * 0.75f; // 環的厚度，數字越小環越粗

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                bool inRing = dist <= outerRadius && dist >= innerRadius;
                tex.SetPixel(x, y, inRing ? Color.white : new Color(1, 1, 1, 0));
            }
        }
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    /// <summary>
    /// 更新填滿度，0 = 剛開始注視，1 = Dwell 時間到
    /// </summary>
    public void SetFillAmount(float t)
    {
        if (ringImage != null) ringImage.fillAmount = Mathf.Clamp01(t);
    }

    /// <summary>
    /// 讓進度圈出現在視線與面板的碰撞點，並面向使用者
    /// 【修正】直接貼在碰撞點上會跟目標表面重疊，造成 Z-fighting（畫面閃爍/破圖）；
    /// 這裡把進度圈沿著「碰撞點→攝影機」的方向，往鏡頭這邊微微浮起一點距離，
    /// 讓它明確浮在目標表面前方，不再跟表面打架
    /// </summary>
    public void UpdateTransform(Vector3 worldPosition, Transform lookAtCamera)
    {
        const float surfaceOffset = 0.01f; // 往鏡頭方向浮起 1 公分，避免跟目標表面重疊

        if (lookAtCamera != null)
        {
            Vector3 towardCamera = (lookAtCamera.position - worldPosition).normalized;
            transform.position = worldPosition + towardCamera * surfaceOffset;
            transform.rotation = Quaternion.LookRotation(transform.position - lookAtCamera.position);
        }
        else
        {
            transform.position = worldPosition;
        }
    }

    public void Show() => gameObject.SetActive(true);
    public void Hide() => gameObject.SetActive(false);
}