using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using UnityEngine;

namespace Klak.Ndi.Interop {

// Bandwidth enumeration (equivalent to NDIlib_recv_bandwidth_e)
public enum Bandwidth
{
    MetadataOnly = -10,
    AudioOnly = 10,
    Lowest = 0,
    Highest = 100
}

// Color format enumeration (equivalent to NDIlib_recv_color_format_e)
public enum ColorFormat
{
    BGRX_BGRA = 0,
    UYVY_BGRA = 1,
    RGBX_RGBA = 2,
    UYVY_RGBA = 3,
    BGRX_BGRA_Flipped = 200,
    Fastest = 100,
    Best = 101
}

public class Recv : SafeHandleZeroOrMinusOneIsInvalid
{
    #region SafeHandle implementation

    public bool FrameSyncEnabled { get; private set; }

    private IntPtr _frameSyncHandle;
    private bool _disposing = false;
    private object _audioLock;
    private object _videoLock;    

    Recv() : base(true) {}

    protected override bool ReleaseHandle()
    {
        _disposing = true;
        if (FrameSyncEnabled)
            _Framesync_destroy(_frameSyncHandle);
        _Destroy(handle);
        return true;
    }

    #endregion

    #region Public methods

    public static Recv Create(in Settings settings)
      => _Create(settings);

    public static Recv CreateWithFrameSync(in Settings settings)
    {
        var r = Create(settings); 
        r._frameSyncHandle = _Framesync_create(r);
        r.FrameSyncEnabled = true;
        return r;
    }

    public FrameType Capture
        (out VideoFrame video, out AudioFrame audio, out MetadataFrame metadata, uint timeout)
    {
        if (FrameSyncEnabled)
        {
            Debug.LogError("FrameSync is enabled, use CaptureFrameSyncVideo instead.");
            video = new VideoFrame();
            audio = new AudioFrame();
            metadata = new MetadataFrame();
            
            return FrameType.None;
        } 
        return _Capture(this, out video, out audio, out metadata, timeout);
    }

    public bool CaptureFrameSyncVideo(out VideoFrame video, FrameFormat field_type)
    {
        if (_disposing)
        {
            video = new VideoFrame();
            return false;
        }

        if (!FrameSyncEnabled)
        {
            Debug.LogError("FrameSync is not enabled, use Capture instead.");
            video = new VideoFrame();
            return false;
        }
        _Framesync_capture_video(_frameSyncHandle, out video, field_type);
        if (video.Data == IntPtr.Zero) return false;
        return true;
    }

    public void CaptureFrameSyncAudio(out AudioFrame audio, int no_samples)
    {
        if (_disposing)
        {
            audio = new AudioFrame();
            return;
        }
        
        if (!FrameSyncEnabled)
        {
            Debug.LogError("FrameSync is not enabled, use CaptureAudio instead.");
            audio = new AudioFrame();
            return;
        }
        
        _Framesync_capture_audio(_frameSyncHandle, out audio, 0, 0, no_samples);
    }

    public FrameType CaptureVideoAndMeta
        (out VideoFrame video, out MetadataFrame metadata, uint timeout)
        => _CaptureVideo(this, out video, IntPtr.Zero, out metadata, timeout);

    public bool CaptureAudio(out AudioFrame audio, uint timeout)
    {
        if (FrameSyncEnabled)
        {
            Debug.LogError("FrameSync is enabled, use CaptureFrameSyncAudio instead.");
            audio = new AudioFrame();
            return false;
        }
        var t = _CaptureAudio(this, IntPtr.Zero, out audio, IntPtr.Zero, timeout);
        return t == FrameType.Audio;
    }

    public void FreeVideoFrame(in VideoFrame frame)
    {
        if (FrameSyncEnabled)
            _Framesync_free_video(_frameSyncHandle, frame);
        else
            _FreeVideo(this, frame);
    }

    public void FreeAudioFrame(in AudioFrame frame)
    {
        if (FrameSyncEnabled)
            _Framesync_free_audio(_frameSyncHandle, frame);
        else
            _FreeAudio(this, frame);
    }
    
    public void FreeMetadataFrame(in MetadataFrame frame)
        => _FreeMetadata(this, frame);

    public void FreeString(IntPtr pointer)
        => _FreeString(this, pointer);

    public bool SetTally(in Tally tally)
        => _SetTally(this, tally);

    public void AudioFrameToInterleaved(ref AudioFrame source, ref AudioFrameInterleaved dest)
        => _AudioFrameToInterleaved(ref source, ref dest);

    public void AudioFrameFromInterleaved(ref AudioFrameInterleaved source, ref AudioFrame dest)
        => _AudioFrameFromInterleaved(ref source, ref dest);

    public void GetPerformance(ref ReceiverPerformance p_total, ref ReceiverPerformance p_dropped)
        => _Recv_get_performance(this, ref p_total, ref p_dropped);

    public void GetQueue(ref ReceiverQueue p_total)
        => _Recv_get_queue(this, ref p_total);
    
    #endregion

    #region Unmanaged interface

    // Constructor options (equivalent to NDIlib_recv_create_v3_t)
    [StructLayout(LayoutKind.Sequential)]
    public struct Settings
    {
        public Source Source;
        public ColorFormat ColorFormat;
        public Bandwidth Bandwidth;
        [MarshalAs(UnmanagedType.U1)] public bool AllowVideoFields;
        public IntPtr Name;
    }

    [DllImport(Config.DllName, EntryPoint = "NDIlib_recv_create_v3")]
    static extern Recv _Create(in Settings Settings);

    [DllImport(Config.DllName, EntryPoint = "NDIlib_recv_destroy")]
    static extern void _Destroy(IntPtr recv);

    [DllImport(Config.DllName, EntryPoint = "NDIlib_recv_capture_v2")]
    static extern FrameType _Capture(Recv recv,
        out VideoFrame video, out AudioFrame audio, out MetadataFrame metadata, uint timeout);

    [DllImport(Config.DllName, EntryPoint = "NDIlib_recv_capture_v2")]
    static extern FrameType _CaptureAudio(Recv recv,
        IntPtr p1, out AudioFrame audio, IntPtr p2, uint timeout);
    
    [DllImport(Config.DllName, EntryPoint = "NDIlib_recv_capture_v2")]
    static extern FrameType _CaptureVideo(Recv recv, out VideoFrame video, IntPtr p1, out MetadataFrame metadata, uint timeout);
    
    [DllImport(Config.DllName, EntryPoint = "NDIlib_recv_free_video_v2")]
    static extern void _FreeVideo(Recv recv, in VideoFrame data);

    [DllImport(Config.DllName, EntryPoint = "NDIlib_recv_free_audio_v2")]
    static extern void _FreeAudio(Recv recv, in AudioFrame data);
    
    [DllImport(Config.DllName, EntryPoint = "NDIlib_recv_free_metadata")]
    static extern void _FreeMetadata(Recv recv, in MetadataFrame data);

    [DllImport(Config.DllName, EntryPoint = "NDIlib_recv_free_string")]
    static extern void _FreeString(Recv recv, IntPtr pointer);

    [DllImport(Config.DllName, EntryPoint = "NDIlib_recv_set_tally")]
    [return: MarshalAs(UnmanagedType.U1)]
    static extern bool _SetTally(Recv recv, in Tally tally);

    [DllImport(Config.DllName, EntryPoint = "NDIlib_util_audio_to_interleaved_32f_v2")]
    static extern void _AudioFrameToInterleaved(ref AudioFrame src, ref AudioFrameInterleaved dst);

    [DllImport(Config.DllName, EntryPoint = "NDIlib_util_audio_from_interleaved_32f_v2")]
    static extern void _AudioFrameFromInterleaved(ref AudioFrameInterleaved src, ref AudioFrame dst);

    // Frame Sync

    [DllImport(Config.DllName, EntryPoint = "NDIlib_framesync_create")]
    internal static extern IntPtr _Framesync_create(Recv p_receiver);

    // framesync_destroy 
    [DllImport(Config.DllName, EntryPoint = "NDIlib_framesync_destroy")]
    internal static extern void _Framesync_destroy(IntPtr p_instance);


    // framesync_capture_audio
    [DllImport(Config.DllName, EntryPoint = "NDIlib_framesync_capture_audio")]
    internal static extern IntPtr _Framesync_capture_audio(IntPtr p_instance, out AudioFrame p_audio_data,
        int sample_rate, int no_channels, int no_samples);
    
    // framesync_free_audio
    [DllImport(Config.DllName, EntryPoint = "NDIlib_framesync_free_audio")]
    internal static extern void _Framesync_free_audio(IntPtr p_instance, in AudioFrame p_audio_data);

    // framesync_audio_queue_depth 
    [DllImport(Config.DllName, EntryPoint = "NDIlib_framesync_audio_queue_depth")]
    internal static extern int _Framesync_audio_queue_depth(IntPtr p_instance);

    // framesync_capture_video 
    [DllImport(Config.DllName, EntryPoint = "NDIlib_framesync_capture_video")]
    internal static extern void _Framesync_capture_video(IntPtr p_instance, out VideoFrame p_video_data,
        FrameFormat field_type);

    // framesync_free_video 
    [DllImport(Config.DllName, EntryPoint = "NDIlib_framesync_free_video")]
    internal static extern void _Framesync_free_video(IntPtr p_instance, in VideoFrame p_video_data);


    // recv_get_performance 
    [DllImport(Config.DllName, EntryPoint = "NDIlib_recv_get_performance")]
    internal static extern void _Recv_get_performance(Recv p_instance, ref ReceiverPerformance p_total, ref ReceiverPerformance p_dropped);

    // recv_get_queue 
    [DllImport(Config.DllName, EntryPoint = "NDIlib_recv_get_queue")]
    internal static extern void _Recv_get_queue(Recv p_instance, ref ReceiverQueue p_total);
    
    #endregion
}

} // namespace Klak.Ndi.Interop
