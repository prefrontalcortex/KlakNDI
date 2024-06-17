using IntPtr = System.IntPtr;
using Marshal = System.Runtime.InteropServices.Marshal;

namespace Klak.Ndi {

// Small helper class for NDI recv interop
static class RecvHelper
{
    public static Interop.Source? FindSource(string sourceName)
    {
        foreach (var source in SharedInstance.Find.CurrentSources)
            if (source.NdiName == sourceName) return source;
        return null;
    }

    public static unsafe Interop.Recv TryCreateRecv(string sourceName, Interop.Bandwidth bandwidth)
    {
        var source = FindSource(sourceName);
        if (source == null) return null;

        var opt = new Interop.Recv.Settings
          { Source = (Interop.Source)source,
            ColorFormat = Interop.ColorFormat.Fastest,
            Bandwidth = bandwidth };

        return Interop.Recv.Create(opt);
    }

    public static Interop.VideoFrame? TryCaptureVideoFrame(Interop.Recv recv)
    {
        Interop.VideoFrame video;
        Interop.AudioFrame audio;
        Interop.MetadataFrame metadata;
        var type = recv.Capture(out video, out audio, out metadata, 0);
        if (type != Interop.FrameType.Video) return null;
        return (Interop.VideoFrame?)video;
    }

    public static string GetStringData(IntPtr dataPtr)
    {
        if (dataPtr == IntPtr.Zero)
        {
            return null;
        }

        return Marshal.PtrToStringAnsi(dataPtr);
    }
}

} // namespace Klak.Ndi
