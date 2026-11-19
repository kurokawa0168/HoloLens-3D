using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 一個學生的狀態（對應後端 /api/class-status 回傳的 students[]）。
/// 顯示用的文字由後端組好（例如 "2/3（67%）"，沒資料是 "—"），這裡直接顯示，不需要換算。
/// </summary>
[Serializable]
public class StudentDetailRecord
{
    public string id;               // = 帳號 = QR 碼內容
    public string account;
    public string name;
    public string light;            // 燈號："gray" / "red" / "yellow" / "green"
    public string state;            // 狀態："not_started" / "idle" / "normal"
    public string status_text;      // 未開始 / 觀看中 / 暫停中 / 作答中 / 閒置 N 分鐘
    public string progress_text;    // 例如 "04:00 / 10:00（40%）"
    public string answer_rate_text; // 做題率，例如 "2/3（67%）"
    public string error_rate_text;  // 錯題率，例如 "1/2（50%）"
    public string repeat_text;      // 重看，例如 "1 段"
    public string late_text;        // 例如 "晚到 3 分鐘"，沒有晚到是空字串

    // 舊欄位名稱：燈號顏色。StudentQRTracker 的燈號小球讀這個欄位，所以保留，值等於 light
    [NonSerialized] public string scoreColor;
}

[Serializable]
public class ClassStatusResponse
{
    public bool success;
    public List<StudentDetailRecord> students;
}

/// <summary>
/// 定時向後端讀取全班狀態（與教師端共用同一份資料）。
/// </summary>
public class StudentDetailData : MonoBehaviour
{
    [Tooltip("全班狀態 API。注意：改這裡的預設值不會更新場景中已存在的物件，請到 Inspector 確認網址")]
    [SerializeField] private string apiUrl = "https://hololens-723683201885.asia-east1.run.app/api/class-status";

    [Tooltip("幾秒重新抓一次")]
    [SerializeField] private float refreshInterval = 3f;

    private readonly Dictionary<string, StudentDetailRecord> lookup = new Dictionary<string, StudentDetailRecord>();
    public bool IsLoaded { get; private set; } = false;

    public event Action OnDataChanged;

    private void Start()
    {
        StartCoroutine(PollLoop());
    }

    private IEnumerator PollLoop()
    {
        while (true)
        {
            yield return StartCoroutine(FetchClassStatus());
            yield return new WaitForSeconds(refreshInterval);
        }
    }

    private IEnumerator FetchClassStatus()
    {
        using (UnityWebRequest request = UnityWebRequest.Get(apiUrl))
        {
            request.timeout = 10; // 秒；伺服器沒回應就放棄這一次，等下一輪
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"讀取全班狀態失敗: {request.error}");
                yield break;
            }

            ClassStatusResponse response = JsonUtility.FromJson<ClassStatusResponse>(request.downloadHandler.text);
            if (response == null || response.students == null) yield break;

            lookup.Clear();
            foreach (var record in response.students)
            {
                if (string.IsNullOrEmpty(record.id) || lookup.ContainsKey(record.id)) continue;
                record.scoreColor = record.light; // 給燈號小球用
                lookup.Add(record.id, record);
            }

            IsLoaded = true;
            OnDataChanged?.Invoke();
        }
    }

    public StudentDetailRecord GetRecord(string qrCode)
    {
        lookup.TryGetValue(qrCode, out var record);
        return record;
    }
}