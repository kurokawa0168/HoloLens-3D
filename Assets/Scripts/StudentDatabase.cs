using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class StudentRecord
{
    public string qrCode;       // QR code 解碼出來的內容,例如 "s001"
    public string studentName;  // 顯示用姓名
    public string status;       // 狀態,例如 "已到"、"請假"
}

public class StudentDatabase : MonoBehaviour
{
    // 這個清單可以直接在 Unity Inspector 面板裡編輯,不用每次改都重新寫程式
    [SerializeField]
    private List<StudentRecord> records = new List<StudentRecord>
    {
        new StudentRecord{ qrCode = "s001", studentName = "王小明", status = "已到" },
        new StudentRecord{ qrCode = "s002", studentName = "李小華", status = "已到" },
        new StudentRecord{ qrCode = "s003", studentName = "陳小美", status = "請假" },
        new StudentRecord{ qrCode = "e001", studentName = "教師測試點", status = "-" },
    };

    private Dictionary<string, StudentRecord> lookup;

    private void Awake()
    {
        // 遊戲開始時,把清單轉成查詢用的字典,查詢速度比較快
        lookup = new Dictionary<string, StudentRecord>();
        foreach (var record in records)
        {
            if (!lookup.ContainsKey(record.qrCode))
                lookup.Add(record.qrCode, record);
        }
    }

    public string GetDisplayText(string qrCode)
    {
        if (lookup.TryGetValue(qrCode, out var record))
        {
            return $"{record.studentName}\n{record.status}";
        }
        // 掃到不在名單裡的QR code時,至少顯示原始內容方便除錯
        return $"未登錄QR: {qrCode}";
    }
}