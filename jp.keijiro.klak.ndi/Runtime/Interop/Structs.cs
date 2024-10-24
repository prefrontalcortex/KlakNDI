using System;
using System.Runtime.InteropServices;

namespace Klak.Ndi.Interop {

// This allows you determine the current performance levels of the receiving to be able to detect whether frames have been dropped
[Serializable]
[StructLayoutAttribute(LayoutKind.Sequential)]
public struct ReceiverPerformance
{
    // The number of video frames
    public Int64	video_frames;

    // The number of audio frames
    public Int64	audio_frames;

    // The number of metadata frames
    public Int64	metadata_frames;
}

// Get the current queue depths
[Serializable]
[StructLayoutAttribute(LayoutKind.Sequential)]
public struct ReceiverQueue
{
    // The number of video frames
    public int	video_frames;

    // The number of audio frames
    public int	audio_frames;

    // The number of metadata frames
    public int	metadata_frames;
}    
    
    
// Packet type enumeration (equivalent to NDIlib_frame_type_e)
public enum FrameType
{
    None = 0,
    Video = 1,
    Audio = 2,
    Metadata = 3,
    Error = 4,
    StatusChange = 100
}

// FourCC values for video/audio frames
public enum FourCC
{
    // Video
    UYVY = 0x59565955,
    UYVA = 0x41565955,
    P216 = 0x36313250,
    PA16 = 0x36314150,
    YV12 = 0x32315659,
    I420 = 0x30323449,
    NV12 = 0x3231564E,
    BGRA = 0x41524742,
    BGRX = 0x58524742,
    RGBA = 0x41424752,
    RGBX = 0x58424752,
    // Audio
    FLTp = 0x70544c46
}

// Frame format enumeration (equivalent to NDIlib_frame_format_type_e)
public enum FrameFormat
{
    Interleaved,
    Progressive,
    Field0,
    Field1
}

// NDI source descriptor (equivalent to NDIlib_source_t)
[StructLayout(LayoutKind.Sequential)]
public struct Source
{
    public IntPtr _NdiName;
    public IntPtr _UrlAddress;
    public string NdiName => Marshal.PtrToStringAnsi(_NdiName);
    public string UrlAddress => Marshal.PtrToStringAnsi(_UrlAddress);
}

// Video frame descriptor (equivalent to NDILib_video_frame_v2_t)
[StructLayout(LayoutKind.Sequential)]
public struct VideoFrame
{
    public int Width, Height;
    public FourCC FourCC;
    public int FrameRateN, FrameRateD;
    public float AspectRatio;
    public FrameFormat FrameFormat;
    public long Timecode;
    public IntPtr Data;
    public int LineStride;
    public IntPtr _Metadata;
    public long Timestamp;

    public string Metadata => RecvHelper.GetStringData(_Metadata);

    public bool HasData => Data != IntPtr.Zero;

    public override string ToString()
	{
		return $"{nameof(VideoFrame)}: [{Width},{Height}] Format: {FourCC} FrameRate: {FrameRateN / (float) FrameRateD} Aspect: {AspectRatio} Timecode: {Timecode} Timestamp: {Timestamp}";
	}
}

// Tally data structure (equivalent to NDIlib_tally_t)
[StructLayout(LayoutKind.Sequential)]
public struct Tally
{
    [MarshalAs(UnmanagedType.U1)] public bool OnProgram;
    [MarshalAs(UnmanagedType.U1)] public bool OnPreview;

    public override string ToString()
    {
        return $"{nameof(Tally)}: OnProgram: {OnProgram} OnPreview {OnPreview}";
    }
}

[StructLayoutAttribute(LayoutKind.Sequential)]
public struct AudioFrame // NDIlib_audio_frame_v2
    {
    public int SampleRate;
    public int NoChannels;
    public int NoSamples;
    public long Timecode;
    public IntPtr Data;
    public int ChannelStrideInBytes;
    public IntPtr _Metadata;
    public long Timestamp;

    public string Metadata => RecvHelper.GetStringData(_Metadata);

    public bool HasData => Data != IntPtr.Zero && NoSamples > 0;

    public override string ToString()
    {
        return $"{nameof(AudioFrame)}: SampleRate: {SampleRate} Channels: {NoChannels} Samples: {NoSamples} Timecode: {Timecode} ChannelStride: {ChannelStrideInBytes} Timestamp: {Timestamp}";
    }
}

// FourCC values for audio frames
public enum FourCC_audio_type_e
{
    // Planar 32-bit floating point. Be sure to specify the channel stride.
    FourCC_audio_type_FLTP = 0x70544c46,
    FourCC_type_FLTP = FourCC_audio_type_FLTP,

    // Ensure that the size is 32bits
    FourCC_audio_type_max = 0x7fffffff
}

// This describes an audio frame
[StructLayoutAttribute(LayoutKind.Sequential)]
public struct AudioFrameV3
{
    // The sample-rate of this buffer
    public int sample_rate;

    // The number of audio channels
    public int no_channels;

    // The number of audio samples per channel
    public int no_samples;

    // The timecode of this frame in 100ns intervals
    public long timecode;

    // What FourCC describing the type of data for this frame
    public FourCC_audio_type_e FourCC;

    // The audio data
    public IntPtr p_data;

    // If the FourCC is not a compressed type and the audio format is planar,
    // then this will be the stride in bytes for a single channel.
    // If the FourCC is a compressed type, then this will be the size of the
    // p_data buffer in bytes.
    public int channel_stride_in_bytes;

    // Per frame metadata for this frame. This is a NULL terminated UTF8 string that should be
    // in XML format. If you do not want any metadata then you may specify NULL here.
    public IntPtr p_metadata;

    // This is only valid when receiving a frame and is specified as a 100ns time that was the exact
    // moment that the frame was submitted by the sending side and is generated by the SDK. If this
    // value is NDIlib_recv_timestamp_undefined then this value is not available and is NDIlib_recv_timestamp_undefined.
    public Int64 timestamp;
}

[StructLayoutAttribute(LayoutKind.Sequential)]
public struct AudioFrameInterleaved // NDIlib_audio_frame_interleaved_32f
{
    public int SampleRate;
    public int NoChannels;
    public int NoSamples;
    public long Timecode;
    public IntPtr Data;
}

[StructLayoutAttribute(LayoutKind.Sequential)]
public struct MetadataFrame
{
    public int Length;
    public long Timecode;
    public IntPtr _Data;

    public string Data => RecvHelper.GetStringData(_Data);

    public bool HasData => _Data != IntPtr.Zero && Length > 0;

    public override string ToString()
    {
        return $"{nameof(MetadataFrame)}: Length: {Length} Timecode: {Timecode} Data: {Data}";
    }
}

} // namespace Klak.Ndi.Interop
