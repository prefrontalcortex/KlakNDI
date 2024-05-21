using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
using IntPtr = System.IntPtr;

namespace Klak.Ndi {

// Small utility functions
static class Util
{
    public static int FrameDataSize(int width, int height, bool alpha)
      => width * height * (alpha ? 3 : 2);

    public static bool HasAlpha(Interop.FourCC fourCC)
      => fourCC == Interop.FourCC.UYVA;

    public static bool InGammaMode
      => QualitySettings.activeColorSpace == ColorSpace.Gamma;

    public static bool UsingMetal
      => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Metal;

    public static void Destroy(Object obj)
    {
        if (obj == null) return;

        if (Application.isPlaying)
            Object.Destroy(obj);
        else
            Object.DestroyImmediate(obj);
    }

    public static int AudioChannels(AudioSpeakerMode speakerMode)
    {
        switch (speakerMode)
        {
            case AudioSpeakerMode.Mono: return 1;
            case AudioSpeakerMode.Stereo: return 2;
            case AudioSpeakerMode.Quad: return 4;
            case AudioSpeakerMode.Surround: return 5;
            case AudioSpeakerMode.Mode5point1: return 6;
            case AudioSpeakerMode.Mode7point1: return 8;
            default:
                return 2;
        }
    }
}

// Extension method to add IntPtr support to ComputeBuffer.SetData
static class ComputeBufferExtension
{
    public unsafe static void SetData
      (this ComputeBuffer buffer, IntPtr pointer, int count, int stride)
    {
        // NativeArray view for the unmanaged memory block
        var view =
          NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>
            ((void*)pointer, count * stride, Allocator.None);

        #if ENABLE_UNITY_COLLECTIONS_CHECKS
        var safety = AtomicSafetyHandle.Create();
        NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref view, safety);
        #endif

        buffer.SetData(view);

        #if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle.Release(safety);
        #endif
    }
}

} // namespace Klak.Ndi
