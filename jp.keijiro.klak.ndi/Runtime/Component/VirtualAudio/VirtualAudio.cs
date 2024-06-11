using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace Klak.Ndi.Audio
{
    public static class VirtualAudio
    {
        public struct AudioSourceSettings
        {
            public Vector3 position;
            public float spatialBlend;
            public float volume;
            public float minDistance;
            public float maxDistance;
            public AudioRolloffMode rolloffMode;
            public AnimationCurve customRolloffCurve;
            public AnimationCurve spatialBlendCurve;
            public int forceToChannel;
        }
        
        public class AudioSourceData : IDisposable
        {
            internal int id;
            public NativeArray<float> audioData;

            public AudioSourceSettings settings;

            public float[] lastListenerWeights;

            public int objectBasedChannel = -1;
            
            public void CheckDataSize(int sampleSize)
            {
                if (!audioData.IsCreated || audioData.Length < sampleSize)
                {
                    if (audioData.IsCreated)
                        audioData.Dispose();
                    audioData = new NativeArray<float>(sampleSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                }
            }

            public void ResetData()
            {
                if (!audioData.IsCreated)
                    return;
                unsafe
                {
                    UnsafeUtility.MemClear(audioData.GetUnsafePtr(), audioData.Length * sizeof(float));
                }
            }

            public void Dispose()
            {
                audioData.Dispose();
            }
        }

        private class ListenerData : IDisposable
        {
            public Vector3 position;
            public float volume;
            public float directionDotSubtract = 0.5f;

            public NativeArray<float> audioData;

            public void ResetAudioData(int sampleSize)
            {
                if (!audioData.IsCreated || audioData.Length < sampleSize)
                {
                    if (audioData.IsCreated)
                        audioData.Dispose();
                    audioData = new NativeArray<float>(sampleSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                }
                
                unsafe
                {
                    UnsafeUtility.MemClear(audioData.GetUnsafePtr(), sampleSize * sizeof(float));
                }
            }

            public void Dispose()
            {
                audioData.Dispose();
            }
        }

        [BurstCompile]
        internal static class BurstMethods
        {
            [BurstCompile]
            public static unsafe void MixToMono(float* rawAudioData, int rawLength, float* finalAudioData, int channels)
            {
                int sampleIndex = 0;
                for (int i = 0; i < rawLength; i += channels)
                {
                    // Mix all channels into one
                
                    float v = 0;
                    int nonNullChannels = 0;
                    for (int c = 0; c < channels; c++)
                    {
                        v += rawAudioData[i + c];
                        if (rawAudioData[i + c] != 0)
                            nonNullChannels++;
                    }

                    if (nonNullChannels == 0)
                        v = 0f;
                    else
                        v /= nonNullChannels;
                    
                    finalAudioData[sampleIndex] = v;
                
                    sampleIndex++;
                }
            }

            [BurstCompile]
            public static unsafe void UpMixMono(float* monoSource, int sourceStartIndex, float* destination, int destOffset, int destinationChannelCo,
                int length)
            {
                for (int i = 0; i < length; i++)
                    for (int c = 0; c < destinationChannelCo; c++)
                        destination[i * destinationChannelCo + c] = monoSource[i];
            }
            
            [BurstCompile]
            public static unsafe void UpMixMonoWithDestination(float* monoSource, int sourceStartIndex, float* destination, int destOffset, int destinationChannelCo,
                int length)
            {
                for (int i = 0; i < length; i++)
                    for (int c = 0; c < destinationChannelCo; c++)
                        destination[i * destinationChannelCo + c] = monoSource[i] * math.abs(destination[i * destinationChannelCo + c]);
            }

            [BurstCompile]
            public static unsafe void ResampleAudioData(float* sourceData, float* destData, int sourceSampleCount, int sourceSampleRate,
                int destSampleRate, int destSampleCount, int channelCount)
            {
                float ratio = (float) sourceSampleRate / destSampleRate;
                float step = 1.0f / ratio;
                float position = 0;
                for (int i = 0; i < sourceSampleCount; i++)
                {
                    for (int c = 0; c < channelCount; c++)
                    {
                        int destIndex = i * channelCount + c;
                        if (destIndex >= 0 && destIndex < destSampleCount)
                            destData[destIndex] = sourceData[(int) position * channelCount + c];
                    }

                    position += step;
                }
            }
            
            [BurstCompile]
            public static unsafe void PlanarToInterleaved(float* planarData, int planarOffset, float* destData, int destOffset, int channels, int length)
            {
                for (int i = 0; i < length; i++)
                    for (int c = 0; c < channels; c++)
                        destData[destOffset + (i * channels + c)] = planarData[planarOffset + i + c];
            }
            
            [BurstCompile]
            internal static float LogAttenuation(float distance, float minDistance, float maxDistance)
            {
                float ratio = distance / minDistance;
                return distance <= minDistance ? 1 : (1.0f / (1.0f + 2f * math.log(ratio)));
            }

            [BurstCompile]
            internal static float MixSample(float sample1, float sample2)
            {
                float s1 = math.sign(sample1);
                return sample1 + sample2 + (((sample1 * sample2) * s1 * -1f) * ((s1 + math.sign(sample2) / 2f * s1)));
            }

            [BurstCompile]
            internal static unsafe void MixArrays(float* destination, float* source, int length, float volume)
            {
                for (int i = 0; i < length; i++)
                    destination[i] = MixSample(destination[i], source[i] * volume);
            }   
        }

        internal static bool useVirtualAudio = false;
        internal static bool objectBasedAudio = false;
        private static int _audioSourceNextId = 0;

        private static readonly Dictionary<int, AudioSourceData> _audioSourcesData = new Dictionary<int, AudioSourceData>();
        private static readonly List<ListenerData> _listenerDatas = new List<ListenerData>();

        private static NativeList<float> _attenuationWeights;
        private static NativeList<float> _spatialBlends;
        private static NativeList<float> _speakerDotProducts;
        private static NativeList<float> _weights;
        private static NativeList<float> _distances;

        private static readonly object _speakerLockObject = new object();
        private static readonly object _audioSourceLockObject = new object();
        private static NativeArray<float> _audioSendStream;
        
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            Application.quitting += OnApplicationQuit;
            
            DisposeAllAudioSourceData();
            ClearAllVirtualSpeakerListeners();
            
            _attenuationWeights = new NativeList<float>(50, AllocatorManager.Persistent);
            _spatialBlends = new NativeList<float>(50, AllocatorManager.Persistent);
            _speakerDotProducts = new NativeList<float>(50, AllocatorManager.Persistent);
            _weights = new NativeList<float>(50, AllocatorManager.Persistent);
            _distances = new NativeList<float>(50, AllocatorManager.Persistent);
        }

        private static void OnApplicationQuit()
        {
            _attenuationWeights.Dispose();
            _spatialBlends.Dispose();
            _speakerDotProducts.Dispose();
            _weights.Dispose();
            _distances.Dispose();
            if (_audioSendStream.IsCreated)
                _audioSendStream.Dispose();
        }

        internal static void ClearAllVirtualSpeakerListeners()
        {
            lock (_speakerLockObject)
            {
                foreach (var l in _listenerDatas)
                    l.Dispose();
                _listenerDatas.Clear();
            }
        }
        
        public static Vector3[] GetListenersPositions()
        {
            lock (_speakerLockObject)
            {
                return _listenerDatas.Select( l => l.position).ToArray();
            }
        }
        
        public static float[] GetListenersVolume()
        {
            lock (_speakerLockObject)
            {
                return _listenerDatas.Select( l => l.volume).ToArray();
            }
        }

        internal static void AddListener(Vector3 relativePosition, float dotDirectionAdjust = 0.5f, float volume = 1f)
        {
            var newData = new ListenerData
            {
                position = relativePosition,
                volume = volume,
                directionDotSubtract = dotDirectionAdjust,
            };

            lock (_speakerLockObject)
            {
                _listenerDatas.Add(newData);
            }
        }

        internal static AudioSourceData RegisterAudioSourceChannel()
        {
            var newData = new AudioSourceData
            {
                id = _audioSourceNextId++
            };

            lock (_audioSourceLockObject)
                _audioSourcesData.Add(newData.id, newData);

            return newData;
        }

        private static void DisposeAllAudioSourceData()
        {
            lock (_audioSourceLockObject)
            {
                foreach (var audioSource in _audioSourcesData)
                {
                    audioSource.Value.Dispose();
                }
                _audioSourcesData.Clear();
            }
        }
        
        internal static void UnRegisterAudioSource(AudioSourceData audioSourceData)
        {
            lock (_audioSourceLockObject)
            {
                audioSourceData.Dispose();
                _audioSourcesData.Remove(audioSourceData.id);
            }
        }
        
        private static float GetCameraAvgDistance(Vector3 cameraPosition)
        {
            float distanceFromCameraAvg = 0;
            foreach (var speaker in _listenerDatas)
            {
                float d = Vector3.Distance(cameraPosition + speaker.position, cameraPosition);
                _distances.Add(d);
                distanceFromCameraAvg += d;
            }

            distanceFromCameraAvg /= _listenerDatas.Count;
            return distanceFromCameraAvg;
        }
        
        internal static bool GetObjectBasedAudio(out NativeArray<float> stream,  out int samples, List<NativeArray<float>> channels, List<Vector3> positions, List<float> gains, int maxObjectBasedChannels)
        {
            lock (_audioSourcesData)
            lock (_speakerLockObject)
            {
                samples = 0;

                if (channels.Count != maxObjectBasedChannels || positions.Count != maxObjectBasedChannels)
                {
                    channels.Clear();
                    positions.Clear();
                    gains.Clear();
                    for (int i = 0; i < maxObjectBasedChannels; i++)
                    {
                        channels.Add( new NativeArray<float>(0, Allocator.Persistent));
                        positions.Add(Vector3.zero);
                        gains.Add(0f);
                    }
                }

                var usedChannels = new List<int>(maxObjectBasedChannels);

                if (_audioSourcesData.Count == 0)
                {
                    stream = _audioSendStream;
                    return false;
                }

                // Collecting which channels are used 
                foreach (var audioSource in _audioSourcesData)
                {
                    if (audioSource.Value.objectBasedChannel != -1)
                        usedChannels.Add(audioSource.Value.objectBasedChannel);
                }
                
                // Preparing: Find max sample size and assign free channels when needed
                foreach (var audioSource in _audioSourcesData)
                {
                    if (samples < audioSource.Value.audioData.Length)
                        samples = audioSource.Value.audioData.Length;

                    if (audioSource.Value.objectBasedChannel == -1)
                    {
                        // Find free channel:
                        for (int i = 0; i < maxObjectBasedChannels; i++)
                        {
                            if (usedChannels.Contains(i))
                                continue;

                            audioSource.Value.objectBasedChannel = i;
                            usedChannels.Add(i);
                            break;
                        }
                        
                        // No free channel found
                    }
                }
                
                int streamSize = maxObjectBasedChannels * samples; 
                if (!_audioSendStream.IsCreated || _audioSendStream.Length != streamSize)
                    _audioSendStream = new NativeArray<float>(streamSize, Allocator.Persistent);

                unsafe
                {
                    UnsafeUtility.MemClear(_audioSendStream.GetUnsafePtr(), streamSize * sizeof(float));
                }
                stream = _audioSendStream;
                
                // Assign AudioSource Data to channels
                foreach (var audioSource in _audioSourcesData)
                {
                    var channel = audioSource.Value.objectBasedChannel;
                    if (channel == -1)
                        continue;
                    channels[channel] = audioSource.Value.audioData;
                    positions[channel] = audioSource.Value.settings.position;
                    gains[channel] = audioSource.Value.settings.volume;
                }

                // Reset Data for not used channels
                for (int i = 0; i < maxObjectBasedChannels; i++)
                {
                    if (!usedChannels.Contains(i))
                    {
                        channels[i] = new NativeArray<float>();
                        positions[i] = Vector3.zero;
                        gains[i] = 0f;
                    }
                    else
                       NativeArray<float>.Copy(channels[i], 0, _audioSendStream, i * samples, samples);
                }
            }

            return true;
        }
        
        /// <summary>
        /// Get the final mixed audio channels
        /// </summary>
        /// <param name="stream">returns the mixed audio stream. Dispose should not be called on this NativeArray.</param>
        /// <param name="samples">returns the sample amount</param>
        /// <param name="cameraPosition">Current camera position</param>
        /// <param name="useCameraPosForAttenuation">When false, the volume attenuations are based on speaker to audiosource distance</param>
        /// <returns></returns>
        internal static List<NativeArray<float>> GetMixedAudio(out NativeArray<float> stream, out int samples, Vector3 cameraPosition,
            bool useCameraPosForAttenuation = false)
        {
            _attenuationWeights.Clear();
            _spatialBlends.Clear();
            _speakerDotProducts.Clear();
            _distances.Clear();
            stream = _audioSendStream;

            List<NativeArray<float>> result = new List<NativeArray<float>>();

            lock (_audioSourcesData)
            lock (_speakerLockObject)
            {
                samples = 0;

                if (_listenerDatas.Count == 0 || _audioSourcesData.Count == 0)
                    return result;


                float distanceFromCameraAvg = GetCameraAvgDistance(cameraPosition);

                foreach (var audioSource in _audioSourcesData)
                {
                    if (samples < audioSource.Value.audioData.Length)
                        samples = audioSource.Value.audioData.Length;
                }

                for (int iSpeaker = 0; iSpeaker < _listenerDatas.Count; iSpeaker++)
                {
                    var speaker = _listenerDatas[iSpeaker];
                    speaker.ResetAudioData(samples);
                }
                
                int streamSize = _listenerDatas.Count * samples; 
                if (!_audioSendStream.IsCreated || _audioSendStream.Length != streamSize)
                    _audioSendStream = new NativeArray<float>(streamSize, Allocator.Persistent);

                unsafe
                {
                    UnsafeUtility.MemClear(_audioSendStream.GetUnsafePtr(), streamSize * sizeof(float));
                }
                stream = _audioSendStream;
                
                
                foreach (var audioSource in _audioSourcesData)
                {
                    int forceToChannel = audioSource.Value.settings.forceToChannel;

                    if (forceToChannel != -1)
                    {
                        if (forceToChannel > _listenerDatas.Count - 1)
                        {
                            Debug.LogError("Can't force AudioSource to channel. Wrong channel is set. OutOfBound");
                        }
                        else
                        {
                            var speaker = _listenerDatas[forceToChannel];
                            unsafe
                            {
                                var inputPtr = (float*)speaker.audioData.GetUnsafeReadOnlyPtr();
                                var outputPtr = (float*)audioSource.Value.audioData.GetUnsafePtr();
                            
                                BurstMethods.MixArrays(inputPtr, outputPtr, speaker.audioData.Length,  audioSource.Value.settings.volume);
                            }
                            continue;
                        }
                    }

                    Vector3 centerToSourceDir = (audioSource.Value.settings.position - cameraPosition).normalized;

                    float distanceFromCamera = Vector3.Distance(audioSource.Value.settings.position, cameraPosition);
                    float centerWeight = 1f - Mathf.InverseLerp(0, distanceFromCameraAvg, distanceFromCamera);
                    centerWeight = Mathf.Pow(centerWeight, 2f);

                    _speakerDotProducts.Clear();
                    _attenuationWeights.Clear();
                    _spatialBlends.Clear();
                    _weights.Clear();

                    var audioSourceSettings = audioSource.Value.settings;

                    foreach (var speaker in _listenerDatas)
                    {
                        Vector3 centerToSpeakerDir = speaker.position.normalized;
                        // Dot product for speaker direction weighting
                        float dot = Vector3.Dot(centerToSourceDir, centerToSpeakerDir);
                        _speakerDotProducts.Add(Mathf.Clamp01(dot - speaker.directionDotSubtract + centerWeight));

                        float distanceAS_SPK =
                            Vector3.Distance(
                                useCameraPosForAttenuation ? cameraPosition : (cameraPosition + speaker.position),
                                audioSourceSettings.position);

                        float distanceRollOffWeight = GetDistanceAttenuation(distanceAS_SPK, audioSourceSettings);

                        float spatialBlend = audioSourceSettings.spatialBlend *
                                             audioSourceSettings.spatialBlendCurve.Evaluate(
                                                 Mathf.Clamp01(Mathf.Lerp(audioSourceSettings.minDistance,
                                                     audioSourceSettings.maxDistance, distanceAS_SPK)));

                        _spatialBlends.Add(spatialBlend);
                        _attenuationWeights.Add((distanceRollOffWeight));
                    }

                    
                    if (distanceFromCamera < 4f)
                    {
                        var blendCenter = Mathf.InverseLerp( 2f, 4f, distanceFromCamera);
                        
                        for (int i = 0; i < _speakerDotProducts.Length; i++)
                        {
                            _speakerDotProducts[i] = Mathf.Lerp( _listenerDatas[i].volume, _speakerDotProducts[i], blendCenter);
                        }
                    }
                    
                    float dotSum = 0f;
                    for (int i = 0; i < _speakerDotProducts.Length; i++)
                        dotSum += _speakerDotProducts[i];
                    
                    for (int i = 0; i < _speakerDotProducts.Length; i++)
                        _speakerDotProducts[i] = Mathf.Clamp01(_speakerDotProducts[i] / dotSum);

                    for (int i = 0; i < _attenuationWeights.Length; i++)
                    {
                        float volume = _attenuationWeights[i] * audioSourceSettings.volume;
                        float dotWeight = volume * _speakerDotProducts[i];
                        float weight = Mathf.Clamp01(dotWeight);
                        float spatial = Mathf.Lerp(weight, 1f, 1f - _spatialBlends[i]);
                        _weights.Add(spatial * _listenerDatas[i].volume * 1f);
                    }
                    
                    if (audioSource.Value.lastListenerWeights == null || audioSource.Value.lastListenerWeights.Length != _weights.Length)
                        audioSource.Value.lastListenerWeights = new float[_weights.Length];
                    NativeArray<float>.Copy(_weights, audioSource.Value.lastListenerWeights);
                    
                    
                    for (int iSpeaker = 0; iSpeaker < _listenerDatas.Count; iSpeaker++)
                    {
                        var speaker = _listenerDatas[iSpeaker];

                        if (speaker.audioData.Length != audioSource.Value.audioData.Length)
                        {
                            // This should never happen!
                            Debug.LogError("Channel data length does not match audio source data length!");
                        }

                        unsafe
                        {
                            var listenerPtr = (float*)speaker.audioData.GetUnsafeReadOnlyPtr();
                            var audioSourcePtr = (float*)audioSource.Value.audioData.GetUnsafePtr();
                            
                            BurstMethods.MixArrays(listenerPtr, audioSourcePtr, speaker.audioData.Length,  _weights[iSpeaker]);
                        }
                        NativeArray<float>.Copy(speaker.audioData, 0, _audioSendStream, iSpeaker * samples, samples);
                    }
                }

                result = _listenerDatas.Select(s => s.audioData).ToList();
            }

            return result;
        }
        
        #region Helpers
        private static float GetDistanceAttenuation(float distance, AudioSourceSettings audioSettings)
        {
            switch (audioSettings.rolloffMode)
            {
                case AudioRolloffMode.Logarithmic:
                    return BurstMethods.LogAttenuation(distance, audioSettings.minDistance,
                        audioSettings.maxDistance);
                case AudioRolloffMode.Linear:
                    return Mathf.Clamp01(Mathf.Lerp(1f, 0f,
                        Mathf.InverseLerp(audioSettings.minDistance, audioSettings.maxDistance, distance)));
                case AudioRolloffMode.Custom:
                    return
                        audioSettings.customRolloffCurve.Evaluate(distance / audioSettings.maxDistance);
                default:
                    return 0;
            }
        }

        #endregion
    }
}