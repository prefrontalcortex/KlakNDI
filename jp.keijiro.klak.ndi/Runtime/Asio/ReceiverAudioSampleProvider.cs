using System;
using Klak.Ndi.Interop;
using NAudio.Wave;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Klak.Ndi.Audio.NAudio
{

    public class ReceiverSampleProvider : ISampleProvider, IDisposable
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
        private NdiReceiver _receiver;

        public ReceiverSampleProvider(global::NAudio.Wave.AsioOut asioOut, NdiReceiver receiver)
        {
            _receiver = receiver;
            _asioOut = asioOut;
            _asioOut.DriverResetRequest += AsioOutOnDriverResetRequest;
            _audioBuffer = new NativeList<float>(Allocator.Persistent);

            receiver.OnAudioFrameReceived += OnAudioFrameReceived;
            _waveFormat =
                WaveFormat.CreateIeeeFloatWaveFormat(AudioSettings.outputSampleRate, _asioOut.DriverOutputChannelCount);
        }

        private unsafe void OnAudioFrameReceived(AudioFrame obj)
        {
            lock (_lockObj)
            {
                if (_audioBuffer.Length > obj.NoSamples * 4f)
                    _audioBuffer.RemoveRange(0, obj.NoSamples);

                int destIndex = _audioBuffer.Length;

                int addedLength = obj.NoSamples * _asioOut.DriverOutputChannelCount;
                _audioBuffer.Resize(_audioBuffer.Length + addedLength, NativeArrayOptions.UninitializedMemory);

                var dataPtr = obj.Data.ToPointer();

                BurstMethods.PlanarToInterleaved((float*)dataPtr, 0, obj.NoChannels,
                    (float*)_audioBuffer.GetUnsafePtr(),
                    destIndex, _asioOut.DriverOutputChannelCount, obj.NoSamples);
            }
        }

        private void AsioOutOnDriverResetRequest(object sender, EventArgs e)
        {
            lock (_lockObj)
                _audioBuffer.Clear();
        }

        public void Dispose()
        {
            lock (_lockObj)
                _audioBuffer.Dispose();
            _receiver.OnAudioFrameReceived += OnAudioFrameReceived;
        }
    }
}