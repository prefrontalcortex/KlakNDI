using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
using IntPtr = System.IntPtr;
using Object = UnityEngine.Object;

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
    
    public static void UpdateVUMeter(ref float[] vuMeter, float[] data, int channels)
    {
        if (vuMeter == null || vuMeter.Length != channels)
        {
            vuMeter = new float[channels];
        }
        Array.Fill(vuMeter, 0f);
				
        for (int i = 0; i < data.Length; i += channels)
        {
            for (int c = 0; c < channels; c++)
            {
                float sample =  Mathf.Abs(data[i + c]);
                if (sample > vuMeter[c])
                    vuMeter[c] += sample;
            }
        }
    }
    
    public static void UpdateVUMeterSingleChannel(ref float[] vuMeter, float[] channelData, int channels, int channelIndex)
    {
        if (vuMeter == null || vuMeter.Length != channels)
        {
            vuMeter = new float[channels];
            Array.Fill(vuMeter, 0f);
        }

        if (vuMeter.Length == 0)
            return;
        
        vuMeter[channelIndex] = 0f;
        for (int i = 0; i < channelData.Length; i ++)
        {
                float sample =  Mathf.Abs(channelData[i]);
                if (sample > vuMeter[channelIndex])
                    vuMeter[channelIndex] += sample;
        }
    }    
    
    public static void UpdateVUMeter(ref float[] vuMeter, List<NativeArray<float>> channelsData)
    {
        if (vuMeter == null || vuMeter.Length != channelsData.Count)
        {
            vuMeter = new float[channelsData.Count];
        }
        Array.Fill(vuMeter, 0f);

        for (int c = 0; c < channelsData.Count; c++)
        {
            var data = channelsData[c];
            for (int i = 0; i < data.Length; i ++)
            {
                    float sample =  Mathf.Abs(data[i]);
                    if (sample > vuMeter[c])
                        vuMeter[c] += sample;
            }
        }
        
    }

    public static void UpdateVUMeter(ref float[] vuMeter, List<float[]> channelsData)
    {
        if (vuMeter == null || vuMeter.Length != channelsData.Count)
        {
            vuMeter = new float[channelsData.Count];
        }

        Array.Fill(vuMeter, 0f);

        for (int c = 0; c < channelsData.Count; c++)
        {
            var data = channelsData[c];
            for (int i = 0; i < data.Length; i++)
            {
                float sample = Mathf.Abs(data[i]);
                if (sample > vuMeter[c])
                    vuMeter[c] += sample;
            }
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

        if (view.Length == 0)
        {
            Debug.Log("View.length == 0");
            
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Release(safety);
#endif
            return;
        }
        
        var bufferArray = buffer.BeginWrite<byte>(0, view.Length);
        NativeArray<byte>.Copy(view, bufferArray, view.Length);
        buffer.EndWrite<byte>(view.Length);
       // buffer.SetData(view);

        #if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle.Release(safety);
        #endif
    }
}

} // namespace Klak.Ndi
