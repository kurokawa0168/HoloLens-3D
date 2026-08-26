using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class UserRecord
{
    public string U_id;
    public string user_id;
    public string name;
    public string account;   // 對應QR code內容,例如 "s001"
    public string role;      // "student" 或 "teacher"
}

[Serializable]
public class UserListResponse
{
    public bool success;
    public List<UserRecord> users;
}

public class StudentDatabase : MonoBehaviour
{
    [SerializeField] private string apiUrl = "https://hololens-723683201885.asia-east1.run.app/api/admin/users";

    private Dictionary<string, UserRecord> lookup = new Dictionary<string, UserRecord>();
    public bool IsLoaded { get; private set; } = false;
    public string LastError { get; private set; } = null;

    public event Action OnDataChanged;

    private void Start()
    {
        StartCoroutine(FetchUsers());
    }

    private IEnumerator FetchUsers()
    {
        using (UnityWebRequest request = UnityWebRequest.Get(apiUrl))
        {
            var operation = request.SendWebRequest();
            float elapsed = 0f;
            float manualTimeout = 10f;

            while (!operation.isDone)
            {
                elapsed += Time.deltaTime;
                if (elapsed >= manualTimeout)
                {
                    request.Abort();
                    LastError = "手動逾時中斷(伺服器無回應超過10秒)";
                    Debug.LogError(LastError);
                    OnDataChanged?.Invoke();
                    yield break;
                }
                yield return null;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                LastError = $"{request.result} | {request.error} | Code:{request.responseCode}";
                Debug.LogError($"讀取學生資料失敗: {LastError}");
                OnDataChanged?.Invoke();
                yield break;
            }

            string json = request.downloadHandler.text;
            UserListResponse response = JsonUtility.FromJson<UserListResponse>(json);

            lookup.Clear();
            foreach (var user in response.users)
            {
                if (!string.IsNullOrEmpty(user.account) && !lookup.ContainsKey(user.account))
                {
                    lookup.Add(user.account, user);
                }
            }

            IsLoaded = true;
            Debug.Log($"成功載入 {lookup.Count} 筆學生資料");
            OnDataChanged?.Invoke();
        }
    }

    public string GetDisplayText(string qrCode)
    {
        if (!IsLoaded)
        {
            return LastError != null ? $"連線失敗:\n{LastError}" : "資料載入中...";
        }
        if (lookup.TryGetValue(qrCode, out var user))
        {
            string roleText = user.role == "teacher" ? "教師" : "學生";
            return $"{user.name}\n({roleText})";
        }
        return $"未登錄: {qrCode}";
    }
}