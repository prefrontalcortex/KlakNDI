using System;
using NAudio.Wave;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Klak.Ndi.Audio.NAudio
{
    public class VirtualAudioSampleProvider : ISampleProvider, IDisposable
    {
        private object _lockObj = new object();

        public unsafe int Read(float[] buffer, int offset, int count)
        {
            lock (_lockObj)
            {
                count = Mathf.Min(count, _audioBuffer.Length);

                if (count == 0)
                    return count;

                var ptr = _audioBuffer.GetUnsafePtr();
                var destPtr = UnsafeUtility.PinGCArrayAndGetDataAddress(buffer, out var handle);

                destPtr = (float*)destPtr + offset;

                UnsafeUtility.MemCpy(destPtr, ptr, count * 4);
                UnsafeUtility.ReleaseGCObject(handle);

                _audioBuffer.RemoveRange(0, count);
            }

            return count;
        }

        public WaveFormat WaveFormat
        {
            get => _waveFormat;
        }

        private WaveFormat _waveFormat;
        private global::NAudio.Wave.AsioOut _asioOut;

        private NativeList<float> _audioBuffer;
        private int audioDataChannels;

        public VirtualAudioSampleProvider(global::NAudio.Wave.AsioOut asioOut)
        {
            _asioOut = asioOut;
            _asioOut.DriverResetRequest += AsioOutOnDriverResetRequest;
            _audioBuffer = new NativeList<float>(Allocator.Persistent);

            VirtualAudio.OnAudioStreamUpdated.AddListener(AudioStreamUpdated);
            _waveFormat =
                WaveFormat.CreateIeeeFloatWaveFormat(AudioSettings.outputSampleRate, _asioOut.DriverOutputChannelCount);
        }

        private void AsioOutOnDriverResetRequest(object sender, EventArgs e)
        {
            lock (_lockObj)
                _audioBuffer.Clear();
        }

        private unsafe void AudioStreamUpdated(NativeArray<float> data, int channels)
        {
            if (channels == 0)
                return;

            lock (_lockObj)
            {
                if (_audioBuffer.Length > data.Length * 4f)
                    _audioBuffer.RemoveRange(0, _audioBuffer.Length - data.Length * 4);

                audioDataChannels = channels;
                int destIndex = _audioBuffer.Length;
                int samples = data.Length / channels;
                int addedLength = samples * _asioOut.DriverOutputChannelCount;
                _audioBuffer.Resize(_audioBuffer.Length + addedLength, NativeArrayOptions.UninitializedMemory);

                BurstMethods.PlanarToInterleaved((float*)data.GetUnsafePtr(), 0, channels,
                    (float*)_audioBuffer.GetUnsafePtr(), destIndex, _asioOut.DriverOutputChannelCount, samples);
            }
        }

        public void Dispose()
        {
            lock (_lockObj)
                _audioBuffer.Dispose();
            VirtualAudio.OnAudioStreamUpdated.RemoveListener(AudioStreamUpdated);
        }
    }
}