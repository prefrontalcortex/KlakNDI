using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Klak.Ndi.Interop {

public class Send : SafeHandleZeroOrMinusOneIsInvalid
{
    #region SafeHandle implementation

    Send() : base(true) {}

    protected override bool ReleaseHandle()
    {
        _Destroy(handle);
        return true;
    }

    #endregion

    #region Public methods

    public static Send Create(string name)
      => _Create(new Settings { NdiName = name });

    public static Send Create(string name, bool clockVideo, bool clockAudio)
      => _Create(new Settings { NdiName = name, ClockVideo = clockVideo, ClockAudio = clockAudio });

    public void SendVideo(in VideoFrame data)
      => _SendVideo(this, data);

    public void SendVideoAsync(in VideoFrame data)
      => _SendVideoAsync(this, data);

    public void SendVideoAsync()
      => _SendVideoAsync(this, IntPtr.Zero);

    public bool SetTally(out Tally tally, uint timeout)
      => _SetTally(this, out tally, timeout);

    public void SendAudio(in AudioFrame data)
      => _SendAudio(this, data);

    public void SendAudioV3(in AudioFrameV3 data)
      => _SendAudioV3(this, data);
    
    #endregion

    #region Unmanaged interface

    // Constructor options (equivalent to NDIlib_send_create_t)
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct Settings 
    {
        [MarshalAs(UnmanagedType.LPStr)] public string NdiName;
        [MarshalAs(UnmanagedType.LPStr)] public string Groups;
        [MarshalAs(UnmanagedType.U1)] public bool ClockVideo;
        [MarshalAs(UnmanagedType.U1)] public bool ClockAudio;
    }

    [DllImport(Config.DllName, EntryPoint = "NDIlib_send_create")]
    static extern Send _Create(in Settings settings);

    [DllImport(Config.DllName, EntryPoint = "NDIlib_send_destroy")]
    static extern void _Destroy(IntPtr send);

    [DllImport(Config.DllName, EntryPoint = "NDIlib_send_send_video_v2")]
    static extern void _SendVideo(Send send, in VideoFrame data);

    [DllImport(Config.DllName, EntryPoint = "NDIlib_send_send_video_async_v2")]
    static extern void _SendVideoAsync(Send send, in VideoFrame data);

    [DllImport(Config.DllName, EntryPoint = "NDIlib_send_send_video_async_v2")]
    static extern void _SendVideoAsync(Send send, IntPtr data);

    [DllImport(Config.DllName, EntryPoint = "NDIlib_send_get_tally")]
    [return: MarshalAs(UnmanagedType.U1)]
    static extern bool _SetTally(Send send, out Tally tally, uint timeout);

    [DllImport(Config.DllName, EntryPoint = "NDIlib_send_send_audio_v2")]
    static extern void _SendAudio(Send send, in AudioFrame data);
    
    [DllImport(Config.DllName, EntryPoint = "NDIlib_send_send_audio_v3")]
    static extern void _SendAudioV3(Send send, in AudioFrameV3 data);
    

    [DllImport(Config.DllName, EntryPoint = "NDIlib_util_send_send_audio_interleaved_32f")]
    static extern void _SendAudioInterleaved(Send send, in AudioFrameInterleaved data);

    #endregion
}

} // namespace Klak.Ndi.Interop
