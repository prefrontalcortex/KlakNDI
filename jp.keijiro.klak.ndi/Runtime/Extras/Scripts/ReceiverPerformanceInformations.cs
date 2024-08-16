using Klak.Ndi;
using TMPro;
using UnityEngine;

public class ReceiverPerformanceInformations : MonoBehaviour
{
    public NdiReceiver receiver;

    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI _performance_audioFrames;
    [SerializeField] private TextMeshProUGUI _performance_videoFrames;
    
    [SerializeField] private TextMeshProUGUI _dropped_audioFrames;
    [SerializeField] private TextMeshProUGUI _dropped_videoFrames;
    
    [SerializeField] private TextMeshProUGUI _queue_audioFrames;
    [SerializeField] private TextMeshProUGUI _queue_videoFrames;
    
    void Update()
    {
        _performance_audioFrames.text = "Audio Frames: " + receiver.PerformanceStatistic.audio_frames;
        _performance_videoFrames.text = "Video Frames: " + receiver.PerformanceStatistic.video_frames;
        
        _dropped_audioFrames.text = "Dropped Audio Frames: " + receiver.DroppedStatistic.audio_frames;
        _dropped_videoFrames.text = "Dropped Video Frames: " + receiver.DroppedStatistic.video_frames;
        
        _queue_audioFrames.text = "Queue Audio Frames: " + receiver.QueueStatistic.audio_frames;
        _queue_videoFrames.text = "Queue Video Frames: " + receiver.QueueStatistic.video_frames;
    }
}
