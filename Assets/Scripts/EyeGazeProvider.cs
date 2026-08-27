using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// 眼動追蹤模組 - 視線資料提供者
/// 負責從 HoloLens 2 的眼動裝置讀取「視線起點 + 視線方向」，
/// 並把結果整理成一條 Ray，供其他腳本（如 Dwell 偵測、Raycast 判斷）使用。
///
/// 使用前提：
/// 1. Package Manager 已安裝 OpenXR Plugin + Mixed Reality OpenXR Plugin
/// 2. Project Settings > XR Plug-in Management > OpenXR
///    > Enabled Interaction Profiles 已加入 Eye Gaze Interaction Profile
/// 3. Project Settings > Player > Publishing Settings > Capabilities
///    已勾選 Gaze Input
/// （以上三項未完成時，本腳本會一直找不到裝置，deviceFound 永遠是 false）
/// </summary>
public class EyeGazeProvider : MonoBehaviour
{
    // 快取住的眼動裝置，避免每一幀都重新搜尋一次（效能考量）
    private InputDevice eyeTrackingDevice;

    // 裝置是否已經成功找到並可用
    private bool deviceFound = false;

    void Start()
    {
        TryInitializeEyeTrackingDevice();
    }

    void Update()
    {
        // 如果一開始沒找到裝置（例如系統慢一步才啟動眼動硬體），
        // 每幀嘗試重新尋找，直到成功為止
        if (!deviceFound)
        {
            TryInitializeEyeTrackingDevice();
            return;
        }

        if (TryGetGazeRay(out Vector3 gazeOrigin, out Vector3 gazeDirection))
        {
            Debug.Log($"視線起點: {gazeOrigin}, 視線方向: {gazeDirection}");

            // ↓↓↓ 之後 Dwell 偵測 / Physics.Raycast 會接在這裡 ↓↓↓
        }
    }

    /// <summary>
    /// 步驟 1：用「特徵」向系統詢問是否存在支援眼動追蹤的裝置
    /// </summary>
    private void TryInitializeEyeTrackingDevice()
    {
        var devices = new System.Collections.Generic.List<InputDevice>();

        // 只搜尋具備「EyeTracking」特徵的裝置，而不是隨便一個裝置
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.EyeTracking, devices);

        if (devices.Count > 0)
        {
            eyeTrackingDevice = devices[0];
            deviceFound = true;
        }
    }

    /// <summary>
    /// 步驟 2 + 3：直接向裝置詢問「視線起點位置」與「視線方向」這兩個特徵值
    /// （HoloLens 2 的 Eye Gaze Interaction Profile 是用 Pose 的方式提供資料，
    ///   跟一般雙眼追蹤裝置用的 Eyes struct 是不同系統，這裡改用正確的 EyeTrackingUsages）
    /// 回傳 true 代表這一幀成功拿到有效、且「已被系統追蹤中」的視線資料
    /// </summary>
    public bool TryGetGazeRay(out Vector3 origin, out Vector3 direction)
    {
        origin = Vector3.zero;
        direction = Vector3.zero;

        // 先確認這一幀的視線資料是否「有效追蹤中」（使用者可能眨眼、視線暫時遺失等情況）
        bool isTracked = eyeTrackingDevice.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked;
        if (!isTracked)
        {
            return false;
        }

        // 步驟 2：視線起點（貼近眼睛的位置）
        bool gotPosition = eyeTrackingDevice.TryGetFeatureValue(
        UnityEngine.XR.OpenXR.Features.Interactions.EyeTrackingUsages.gazePosition, out Vector3 localOrigin);

        // 步驟 3：視線方向（四元數，需轉成向量才能餵給 Physics.Raycast）
        bool gotRotation = eyeTrackingDevice.TryGetFeatureValue(
        UnityEngine.XR.OpenXR.Features.Interactions.EyeTrackingUsages.gazeRotation, out Quaternion localRotation);

        // 步驟 3：視線方向（四元數，需轉成向量才能餵給 Physics.Raycast）

        if (!gotPosition || !gotRotation)
        {
            return false;
        }

        // 【關鍵修正】OpenXR 回報的是本地座標(相對於 XR Origin/ 追蹤原點),
        // 必須用 Main Camera 的 Transform 轉換成世界座標,才能跟場景裡的
         // AR_Panel(世界座標)放在同一個空間比較,Raycast 才有意義。
        Transform cameraTransform = Camera.main.transform;
        origin = cameraTransform.TransformPoint(localOrigin);
        direction = cameraTransform.TransformDirection(localRotation * Vector3.forward);

        return true;
    }
}