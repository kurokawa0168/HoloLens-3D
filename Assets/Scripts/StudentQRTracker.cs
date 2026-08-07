using UnityEngine;
using Microsoft.MixedReality.OpenXR;
using TMPro;

public class StudentQRTracker : MonoBehaviour
{
    [SerializeField] private ARMarkerManager markerManager;
    [SerializeField] private StudentDatabase studentDatabase;

    private void OnEnable() => markerManager.markersChanged += OnMarkersChanged;
    private void OnDisable() => markerManager.markersChanged -= OnMarkersChanged;

    private void OnMarkersChanged(ARMarkersChangedEventArgs args)
    {
        foreach (var marker in args.added)
        {
            UpdateLabelText(marker);
        }

        foreach (var marker in args.updated)
        {
            UpdateLabelText(marker);
        }
    }

    private void UpdateLabelText(ARMarker marker)
    {
        string qrCode = marker.GetDecodedString();
        var text = marker.GetComponentInChildren<TextMeshPro>();
        if (text != null)
        {
            text.text = studentDatabase.GetDisplayText(qrCode);
        }
    }
}