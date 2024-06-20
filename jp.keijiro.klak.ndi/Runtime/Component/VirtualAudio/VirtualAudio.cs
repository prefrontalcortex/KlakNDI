using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Events;
using Debug = UnityEngine.Debug;

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
        
        internal class AudioSourceData : IDisposable
        {
            internal int id;
            
            internal NativeArray<float> audioData;
            public AudioSourceSettings settings;
            
            internal float[] currentWeights;
            internal float[] smoothedWeights;

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
            
            internal void UpdateSmoothingWeights()
            {
                if (currentWeights == null || currentWeights.Length != _virtualListeners.Count)
                    return;
            
                if (smoothedWeights == null ||
                    smoothedWeights.Length != currentWeights.Length)
                {
                    smoothedWeights = new float[currentWeights.Length];
                    Array.Copy(currentWeights, smoothedWeights, currentWeights.Length);
                }
            
                float dspDelta = (float)_dspBufferSize / (float)_sampleRate;
            
                for (int i = 0; i < currentWeights.Length; i++)
                    smoothedWeights[i] = Mathf.Lerp(smoothedWeights[i], currentWeights[i], dspDelta * 4f);
            }
            
            internal void ResetData()
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

        internal class ListenerData : IDisposable
        {
            public NativeArray<float> audioData;
            
            internal void ResetAudioData(int sampleSize)
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
        
        internal class VirtualListener 
        {
            public Vector3 position;
            public float volume;

            public SphericalCoordinate sphericalCoordinate;
        }

     
        [BurstCompile]
        [StructLayout(LayoutKind.Sequential)]
        internal struct SphericalCoordinate
        {
            public float azimuth;
            public float elevation;
            public float distance;

            /// <summary>
            /// Returns the relative angle position based on the other azimuth
            /// </summary>
            /// <param name="otherAzimuth"></param>
            /// <returns></returns>
            public float AzimuthAngleDifferenceFrom(float otherAzimuth)
            {
                float diff = azimuth - otherAzimuth;
                if (diff < -180)
                    diff += 360;
                if (diff > 180)
                    diff -= 360;
                return diff;
            }
        }
        
        internal static bool UseVirtualAudio
        {
            get => _useVirtualAudio;
            set
            {
                _useVirtualAudio = value;
                OnVirtualAudioStateChanged.Invoke(_useVirtualAudio);
            }
        }

        internal static bool PlayCenteredAudioSourceOnAllListeners
        {
            get => _centeredAudioSourceOnAllListeners;
            set => _centeredAudioSourceOnAllListeners = value;
        }
        
        private static bool _useVirtualAudio = false;
        private static bool _virtualListenersChanged = false;
        
        public static readonly UnityEvent<bool> OnVirtualAudioStateChanged = new UnityEvent<bool>();
        
        internal static bool objectBasedAudio = false;
        private static int _audioSourceNextId = 0;
        private static bool _testMode = false;
        private static int _currentTestChannel = 0;
        private static bool _allListenersAreOnSameHeight = false;
        
        private static readonly Dictionary<int, AudioSourceData> _audioSourcesData = new Dictionary<int, AudioSourceData>();
        private static readonly List<VirtualListener> _virtualListeners = new List<VirtualListener>();
        private static readonly List<ListenerData> _listenerDatas = new List<ListenerData>();

        private static readonly object _listenerLockObject = new object();
        private static readonly object _audioSourceLockObject = new object();
        private static readonly object _listenerDataLockObject = new object();
        
        private static NativeArray<float> _audioSendStream;
        
        private static NativeArray<float> _testAudioData;
        private static readonly object _testAudioLockObj = new object();
        private static double _testAudioDspStartTime = -1;
        private static bool _centeredAudioSourceOnAllListeners = true;
        
        private static int _dspBufferSize;
        private static int _sampleRate;
        
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            Application.quitting += OnApplicationQuit;
            
            DisposeAllAudioSourceData();
            ClearAllVirtualSpeakerListeners();
            
            AudioSettings.GetDSPBufferSize(out _dspBufferSize, out int _);
            _sampleRate = AudioSettings.outputSampleRate;
        }
        
        public static void ActivateAudioTestMode(bool active)
        {
            lock (_testAudioLockObj)
            {
                _testMode = active;
                _testAudioDspStartTime = AudioSettings.dspTime;
            }
        }

        public static void SetAudioTestChannel(int channel)
        {
            lock (_testAudioLockObj)
            {
                _currentTestChannel = channel;
                _testAudioDspStartTime = AudioSettings.dspTime;
            }
        }

        private static void OnApplicationQuit()
        {
            if (_audioSendStream.IsCreated)
                _audioSendStream.Dispose();
        }

        internal static void ClearAllVirtualSpeakerListeners()
        {
            lock (_listenerDataLockObject)
            {
                foreach (var l in _listenerDatas)
                    l.Dispose();
                _listenerDatas.Clear();
            }
                
            lock (_listenerLockObject)
            {
                
                _virtualListeners.Clear();
                _virtualListenersChanged = true;
            }
        }
        
        public static Vector3[] GetListenersPositions()
        {
            lock (_listenerLockObject)
            {
                return _virtualListeners.Select( l => l.position).ToArray();
            }
        }
        
        public static float[] GetListenersVolume()
        {
            lock (_listenerLockObject)
            {
                return _virtualListeners.Select( l => l.volume).ToArray();
            }
        }

        internal static void AddListener(Vector3 relativePosition, float volume = 1f)
        {
            var newData = new VirtualListener
            {
                position = relativePosition,
                volume = volume,
            };

            lock (_listenerLockObject)
            {
                _virtualListeners.Add(newData);
                _virtualListenersChanged = true;
            }

            lock (_listenerDataLockObject)
            {
                _listenerDatas.Add(new ListenerData
                {
                    audioData = new NativeArray<float>(_dspBufferSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory)
                });
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
        
        private static float GetAvgListenerDistanceFromCamera(Vector3 cameraPosition)
        {
            float distanceFromCameraAvg = 0;
            foreach (var listener in _virtualListeners)
            {
                float d = Vector3.Distance(cameraPosition + listener.position, cameraPosition);
                distanceFromCameraAvg += d;
            }

            distanceFromCameraAvg /= _virtualListeners.Count;
            return distanceFromCameraAvg;
        }
        
        internal static bool GetObjectBasedAudio(out NativeArray<float> stream,  out int samples, List<NativeArray<float>> channels, List<Vector3> positions, List<float> gains, int maxObjectBasedChannels)
        {
            lock (_audioSourcesData)
            lock (_listenerLockObject)
            {
                samples = 0;

                if (_testMode)
                {
                    // Begin test mode
                    samples = _dspBufferSize;
                    if (!_testAudioData.IsCreated || _testAudioData.Length != samples)
                    {
                        if (_testAudioData.IsCreated)
                            _testAudioData.Dispose();
                        _testAudioData = new NativeArray<float>(samples, Allocator.Persistent);
                    }
                    
                    if (!_audioSendStream.IsCreated || _audioSendStream.Length != maxObjectBasedChannels * samples)
                        _audioSendStream = new NativeArray<float>(maxObjectBasedChannels * samples, Allocator.Persistent);

                    unsafe
                    {
                        UnsafeUtility.MemClear(_audioSendStream.GetUnsafePtr(), _audioSendStream.Length * sizeof(float));
                    }
                    
                    channels.Clear();
                    positions.Clear();
                    gains.Clear();
                    for (int i = 0; i < maxObjectBasedChannels; i++)
                    {
                        if (i == _currentTestChannel)
                        {
                            GenerateTestSound(_testAudioData, 0, _testAudioData.Length);
                            channels.Add( _testAudioData);
                            positions.Add(Vector3.zero);
                            gains.Add(1f);
                            NativeArray<float>.Copy(channels[i], 0, _audioSendStream, i * samples, samples);
                            
                        }
                        else
                        {
                            channels.Add( new NativeArray<float>());
                            positions.Add(Vector3.zero);
                            gains.Add(0f);
                        }
                    }

                    stream = _audioSendStream;
                    return true;
                    // End test mode
                }
                
                if (channels.Count != maxObjectBasedChannels || positions.Count != maxObjectBasedChannels)
                {
                    channels.Clear();
                    positions.Clear();
                    gains.Clear();
                    for (int i = 0; i < maxObjectBasedChannels; i++)
                    {
                        channels.Add( new NativeArray<float>());
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

        private static void GenerateTestSound(NativeArray<float> destination, int startIndex, int samples)
        {
            double offset;
            lock (_testAudioLockObj)
            {
                if (_testAudioDspStartTime < 0)
                    _testAudioDspStartTime = AudioSettings.dspTime;
                offset = _testAudioDspStartTime;
            }
            for (int i = 0; i < samples; i++)
            {
                // Create a sinus wave
                float v = 2f * Mathf.Sin(((float)(AudioSettings.dspTime-offset) + (float)i / (float)(_sampleRate)));
                destination[startIndex + i] = v;
            }
        }

        private static float GetSpatialBlend(AudioSourceSettings audioSourceSettings, float audioSourceAttenuation)
        {
            float spatialBlend = audioSourceSettings.spatialBlend *
                                 audioSourceSettings.spatialBlendCurve.Evaluate(
                                     Mathf.Clamp01(Mathf.Lerp(audioSourceSettings.minDistance,
                                         audioSourceSettings.maxDistance, audioSourceAttenuation)));
            return spatialBlend;
        }

        private static void ApplySpatialBlendToWeights(float[] weights, float spatialBlend)
        {
            for (int i = 0; i < weights.Length; i++)
            {
                float weight = weights[i];
                float spatial = Mathf.Lerp(weight, 1f, 1f - spatialBlend);
                weights[i] = spatial;
            }
        }

        private static void UpdateVirtualListeners()
        {
            if (!_virtualListenersChanged)
                return;

            lock (_listenerLockObject)
            {
                _virtualListenersChanged = false;

                if (_virtualListeners.Count == 0)
                    return;

                float height = _virtualListeners[0].position.y;
                _allListenersAreOnSameHeight = true;

                unsafe
                {
                    for (int i = 0; i < _virtualListeners.Count(); i++)
                    {
                        BurstMethods.GetSphericalCoordinates(out _virtualListeners[i].sphericalCoordinate, _virtualListeners[i].position);

                        if (Math.Abs(height - _virtualListeners[i].position.y) > 0.01f)
                            _allListenersAreOnSameHeight = false;
                    }
                }
            }
        }

        internal static void UpdateAudioSourceToListenerWeights(Vector3 cameraPosition, bool useCameraPosForAttenuation = false)
        {
            UpdateVirtualListeners();
            float distanceFromCameraAvg = GetAvgListenerDistanceFromCamera(cameraPosition);
            
            lock (_audioSourcesData)
            lock (_listenerLockObject)
            {
                foreach (var audioSourceKVP in _audioSourcesData)
                {
                    var audioSource = audioSourceKVP.Value;
                    var audioSourceSettings = audioSource.settings;
                    
                    if (audioSource.currentWeights == null || audioSource.currentWeights.Length != _virtualListeners.Count)
                        audioSource.currentWeights = new float[_virtualListeners.Count];
                    
                    if (!audioSource.audioData.IsCreated)
                    {
                        Array.Fill(audioSource.currentWeights, 0f);        
                        continue;
                    }
                    
                    float cameraDistanceToAudioSource = Vector3.Distance(audioSourceSettings.position, cameraPosition);

                    Array.Fill(audioSource.currentWeights, 0f);
                    
                    var usedDistance = useCameraPosForAttenuation
                        ? cameraDistanceToAudioSource
                        : Mathf.Max(0, cameraDistanceToAudioSource - distanceFromCameraAvg);
                    var audioSourceAttenuation = GetDistanceAttenuation(usedDistance, audioSourceSettings);
                    var spatialBlend = GetSpatialBlend(audioSourceSettings, audioSourceAttenuation);
                    var blendToCenter = _centeredAudioSourceOnAllListeners ? 
                        Mathf.Pow(Mathf.Clamp01(Mathf.InverseLerp( distanceFromCameraAvg, 1f, cameraDistanceToAudioSource)), 2f)
                        : 0;

                    void ApplyDistanceAttenuationAndSourceVolumeToWeights()
                    {
                        var volume = audioSourceAttenuation * audioSourceSettings.volume;
                        for (int i = 0; i < audioSource.currentWeights.Length; i++)
                            audioSource.currentWeights[i] *= volume;
                    }
                    
                    if (_allListenersAreOnSameHeight)
                    {
                        // Using simple azimuth based panning
                        CalculateWeightsBasedOnSimplePlanarAzimuthPanning(audioSource.currentWeights, audioSourceSettings, blendToCenter);
                    }
                    else
                    {
                        // TODO: height panning
                    } 
     
                    ApplyDistanceAttenuationAndSourceVolumeToWeights();
                    ApplySpatialBlendToWeights(audioSource.currentWeights, spatialBlend);
                }
            }
        }
        
        /// <summary>
        /// Get the final mixed audio channels
        /// </summary>
        /// <param name="stream">returns the mixed audio stream. Dispose should not be called on this NativeArray.</param>
        /// <param name="samples">returns the sample amount</param>
        /// <param name="cameraPosition">Current camera position</param>
        /// <param name="useCameraPosForAttenuation">When false, the volume attenuations are based on speaker to audiosource distance</param>
        /// <returns></returns>
        internal static List<NativeArray<float>> GetMixedAudio(out NativeArray<float> stream, out int samples, out float[] vus)
        {
            samples = 0;
            stream = _audioSendStream;

            lock (_audioSourcesData)
            lock (_listenerDataLockObject)
            {
                List<NativeArray<float>> result = new List<NativeArray<float>>(_listenerDatas.Count);
                samples = 0;

                if (_virtualListeners.Count == 0 || _audioSourcesData.Count == 0)
                {
                    vus = new float[_listenerDatas.Count];
                    return result;
                }
                
                foreach (var audioSource in _audioSourcesData)
                {
                    if (samples < audioSource.Value.audioData.Length)
                        samples = audioSource.Value.audioData.Length;
                }
                
                int streamSize = _listenerDatas.Count * samples; 
                if (!_audioSendStream.IsCreated || _audioSendStream.Length != streamSize)
                    _audioSendStream = new NativeArray<float>(streamSize, Allocator.Persistent);

                unsafe
                {
                    UnsafeUtility.MemClear(_audioSendStream.GetUnsafePtr(), streamSize * sizeof(float));
                }
                stream = _audioSendStream;
                
                if (_testMode)
                {
                    if (!_testAudioData.IsCreated || _testAudioData.Length != samples)
                    {
                        if (_testAudioData.IsCreated)
                            _testAudioData.Dispose();
                        _testAudioData = new NativeArray<float>(samples, Allocator.Persistent);
                    }
                    GenerateTestSound(_testAudioData, 0, _testAudioData.Length);

                    for (int iListener = 0; iListener < _listenerDatas.Count; iListener++)
                    {
                        var listener = _listenerDatas[iListener];
                        listener.ResetAudioData(samples);
                        result.Add(listener.audioData);
                        if (iListener == _currentTestChannel)
                        {
                            //BurstMethods.MixArrays();
                            NativeArray<float>.Copy(_testAudioData, 0, listener.audioData, 0, samples);
                        }
                        NativeArray<float>.Copy(listener.audioData, 0, _audioSendStream, iListener * samples, samples);
                    }
                    
                    vus = new float[_listenerDatas.Count];
                    vus[_currentTestChannel] = 1f;
                    return result;
                }
                
                for (int iSpeaker = 0; iSpeaker < _listenerDatas.Count; iSpeaker++)
                    _listenerDatas[iSpeaker].ResetAudioData(samples);
                
                for (int i = 0; i < _audioSourcesData.Count; i++)
                {
                    var audioSource = _audioSourcesData.ElementAt(i).Value;
                    int forceToChannel =  audioSource.settings.forceToChannel;

                    if (forceToChannel != -1)
                    {
                        if (forceToChannel > _listenerDatas.Count - 1)
                        {
                            Debug.LogError("Can't force AudioSource to channel. Wrong channel is set. OutOfBound");
                        }
                        else
                        {
                            var listener = _listenerDatas[forceToChannel];
                            unsafe
                            {
                                var inputPtr = (float*)listener.audioData.GetUnsafeReadOnlyPtr();
                                var outputPtr = (float*)audioSource.audioData.GetUnsafePtr();
                            
                                BurstMethods.MixArrays(inputPtr, outputPtr, listener.audioData.Length,  audioSource.settings.volume);
                            }
                            continue;
                        }
                    }
                    
                    audioSource.UpdateSmoothingWeights();
                    MixAudioSourceToListeners(audioSource, samples);
                }

                CopyListenerDataToSendStream();
                vus = new float[_listenerDatas.Count];
                unsafe
                {
                    for (int i = 0; i < _listenerDatas.Count; i++)
                    {
                        var inputPtr = (float*)_listenerDatas[i].audioData.GetUnsafeReadOnlyPtr();
                        BurstMethods.GetVU(inputPtr, _listenerDatas[i].audioData.Length, out var vu);
                        vus[i] = vu;
                        
                        result.Add(_listenerDatas[i].audioData);
                    }
                }
                
                return result;
            }
        }
        
        private static void MixAudioSourceToListeners(AudioSourceData audioSource, int samples)
        {
            if ( audioSource.currentWeights == null || audioSource.audioData.Length == 0 || audioSource.currentWeights.Length != _listenerDatas.Count)
                return;

            for (int iListener = 0; iListener < _listenerDatas.Count; iListener++)
            {
                var listener = _listenerDatas[iListener];

                if (listener.audioData.Length != audioSource.audioData.Length)
                    // This should never happen!
                    Debug.LogError("Channel data length does not match audio source data length!");
                
                unsafe
                {
                    var listenerPtr = (float*)listener.audioData.GetUnsafeReadOnlyPtr();
                    var audioSourcePtr = (float*)audioSource.audioData.GetUnsafePtr();
                            
                    BurstMethods.MixArrays(listenerPtr, audioSourcePtr, listener.audioData.Length,  audioSource.smoothedWeights[iListener]);
                }
            }
        }

        private static void CopyListenerDataToSendStream()
        {
            for (int iListener = 0; iListener < _listenerDatas.Count; iListener++)
            {
                var listData = _listenerDatas[iListener];
                NativeArray<float>.Copy(listData.audioData, 0, _audioSendStream, iListener * listData.audioData.Length, listData.audioData.Length);
            }
        }

        private static void CalculateWeightsBasedOnSimplePlanarAzimuthPanning(float[] weights, AudioSourceSettings audioSourceSettings, float centerBlend)
        {
            BurstMethods.GetSphericalCoordinates(out var spherical, audioSourceSettings.position);
            // Find the left and right listeners from the audioSource by the angle 

            FindLeftAndRightListenerBasedOnAzimuth(spherical,
                out var leftAngle, out var leftListener,
                out var rightAngle, out var rightListener,
                out var angleBetweenLeftAndRight);

            float sum = 0;
            float activeListeners = 0f;
            for (int i = 0; i < _virtualListeners.Count; i++)
                if (_virtualListeners[i].volume > 0f)
                    activeListeners++;
            
            for (int i = 0; i < _virtualListeners.Count; i++)
            {
                float w = 0;
                if (_virtualListeners[i] == leftListener)
                {
                    w = 1f-Mathf.InverseLerp(0, angleBetweenLeftAndRight, Mathf.Abs(leftAngle));
                }
                else if (_virtualListeners[i] == rightListener)
                {
                    w = 1f-Mathf.InverseLerp(0, angleBetweenLeftAndRight, Mathf.Abs(rightAngle));
                }
                
                w = Mathf.Lerp(w, 1f / activeListeners, centerBlend);
                
                if (w > 0f)
                    sum += (w * w);
                weights[i] = w;
            }

            sum = Mathf.Sqrt(sum);
            float a = Mathf.Pow(10f, -6f / 20f) * 2f;
            float k = a * sum;
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] == 0f)
                    continue;
                float amp = (weights[i] * a) / k;
                weights[i] = amp;
            }
            
        }

        private static void FindLeftAndRightListenerBasedOnAzimuth(SphericalCoordinate source, out float leftAngle,
            out VirtualListener leftListener, out float rightAngle, out VirtualListener rightListener, out float angleBetweenLeftAndRight)
        {
            leftAngle = 0;
            rightAngle = 0;
            leftListener = null;
            rightListener = null;
            angleBetweenLeftAndRight = 0;
            for (int i = 0; i < _virtualListeners.Count; i++)
            {
                var currentListener = _virtualListeners[i];
                if (_virtualListeners[i].volume == 0)
                    continue;
                            
                var listenerSpherical = currentListener.sphericalCoordinate;
                var listenerAnglePosFromSource =
                    listenerSpherical.AzimuthAngleDifferenceFrom(source.azimuth);

                if (listenerAnglePosFromSource < 0 && (listenerAnglePosFromSource > leftAngle || leftListener == null))
                {
                    leftListener = currentListener;
                    leftAngle = listenerAnglePosFromSource;
                }

                if (listenerAnglePosFromSource > 0 && (listenerAnglePosFromSource < rightAngle || rightListener == null))
                {
                    rightListener = currentListener;
                    rightAngle = listenerAnglePosFromSource;
                }
            }


            if (leftListener != null && rightListener != null)
            {
                angleBetweenLeftAndRight = Mathf.Abs(leftListener.sphericalCoordinate.AzimuthAngleDifferenceFrom(rightListener.sphericalCoordinate.azimuth));
            }
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