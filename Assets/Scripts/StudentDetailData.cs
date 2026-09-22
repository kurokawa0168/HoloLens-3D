using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class StudentDetailRecord
{
    public string id;                       // = QR碼帳號(account)
    public string name;
    public string role;
    public float quizAccuracy;
    public float quizParticipation;
    public float effectiveParticipationRate;
    public float repeatWatchRatio;
    public float comprehensionScore;
    public string scoreColor;               // "red" / "yellow" / "green"
}

[Serializable]
public class DashboardResponse
{
    public bool success;
    public List<StudentDetailRecord> metrics;
}

public class StudentDetailData : MonoBehaviour
{
    [SerializeField] private string apiUrl = "貼組員給你的dashboard網址";
    [SerializeField] private float refreshInterval = 5f; // 幾秒重新抓一次

    private Dictionary<string, StudentDetailRecord> lookup = new Dictionary<string, StudentDetailRecord>();
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
            yield return StartCoroutine(FetchMetrics());
            yield return new WaitForSeconds(refreshInterval);
        }
    }

    private IEnumerator FetchMetrics()
    {
        using (UnityWebRequest request = UnityWebRequest.Get(apiUrl))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"讀取專注度資料失敗: {request.error}");
                yield break;
            }

            string json = request.downloadHandler.text;
            DashboardResponse response = JsonUtility.FromJson<DashboardResponse>(json);
            if (response?.metrics == null) yield break;

            lookup.Clear();
            foreach (var record in response.metrics)
            {
                if (!string.IsNullOrEmpty(record.id) && !lookup.ContainsKey(record.id))
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