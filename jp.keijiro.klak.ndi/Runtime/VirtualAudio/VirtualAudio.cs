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
        
        internal class AudioSourceData
        {
            internal int id;
            
            internal NativeArray<float> audioStreamDestination;
   
            public AudioSourceSettings settings;
            
            internal float[] currentWeights;
            internal float[] smoothedWeights;

            public int objectBasedChannel = -1;
            
            internal void CheckWeightsArray(int count)
            {
                if (currentWeights == null || currentWeights.Length != count)
                {
                    currentWeights = new float[count];
                    Array.Fill(currentWeights, 0);
                }
            }
            
            internal void UpdateSmoothingWeights()
            {
                if (smoothedWeights == null ||
                    smoothedWeights.Length != currentWeights.Length)
                {
                    smoothedWeights = new float[currentWeights.Length];
                    Array.Copy(currentWeights, smoothedWeights, currentWeights.Length);
                }
            
                float dspDelta = (float)_dspBufferSize / (float)_sampleRate;

                for (int i = 0; i < currentWeights.Length; i++)
                {
                    smoothedWeights[i] = Mathf.Lerp(smoothedWeights[i], currentWeights[i], dspDelta * 4f);
                    if (smoothedWeights[i] < 0.001f)
                        smoothedWeights[i] = 0f;
                }
            }
        }

        internal class ListenerData
        {
            public NativeArray<float> audioStreamDestination;
        }
        
        internal class VirtualListener
        {
            public Vector3 rawPosition;

            public Vector3 TransformedPosition
            {
                get
                {
                    return ApplyOrientationTransform(rawPosition);
                }
            }
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
        
        public static bool UseVirtualAudio
        {
            get => _useVirtualAudio;
            set
            {
                _useVirtualAudio = value;
                if (!value)
                {
                    ClearAllVirtualSpeakerListeners();
                }
                OnVirtualAudioStateChanged.Invoke(_useVirtualAudio);
            }
        }

        public static bool PlayCenteredAudioSourceOnAllListeners
        {
            get => _centeredAudioSourceOnAllListeners;
            set => _centeredAudioSourceOnAllListeners = value;
        }
        
        private static object _listenerOrientationLockObject = new object();
        private static Pose _listenerOrientation = Pose.identity;

        public static Pose AudioOrigin
        {
            get
            {
                lock (_listenerOrientationLockObject)
                    return _listenerOrientation;
            }
            set
            {
                lock (_listenerOrientationLockObject)
                    _listenerOrientation = value;
            }
        } 
        
        private static bool _useVirtualAudio = false;
        private static bool _virtualListenersChanged = false;
        
        public static readonly UnityEvent<bool> OnVirtualAudioStateChanged = new UnityEvent<bool>();
        
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

        public static bool MuteAudioOutput = false;
        
        private static AudioSourceData _testAudioData = new AudioSourceData();
        private static readonly object _testAudioLockObj = new object();
        private static double _testAudioDspStartTime = -1;
        
        private static bool _centeredAudioSourceOnAllListeners = true;
        
        private static int _dspBufferSize;
        private static int _sampleRate;
        private static double _lastCheckSetupTime = -1;
        private static List<int> _usedObjectBasedChannels = new List<int>(32);

        /// <summary>
        /// Be aware: It's called from the Audio Thread! 
        /// </summary>
        public static UnityEvent<NativeArray<float>, int> OnAudioStreamUpdated = new UnityEvent<NativeArray<float>, int>();
        
        public static Vector3 ApplyOrientationTransform(Vector3 position)
        {
            return AudioOrigin.position + (AudioOrigin.rotation * position);
        }
        
        public static bool ObjectBasedAudio 
        {
            get => _objectBasedAudio;
        }
        
        private static bool _objectBasedAudio = false;
        private static int _maxObjectBasedChannels = 16;

        public static int MaxObjectBasedChannels
        {
            get => _maxObjectBasedChannels;
        }
        
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            Application.quitting += OnApplicationQuit;
            
            ClearAllAudioSourceData();
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
        
        public static void SetMaxObjectBasedChannels(int count)
        {
            _maxObjectBasedChannels = count;
        }
        
        public static void ActivateObjectBasedAudio(bool objectBased, int channelCount = 16)
        {
            _objectBasedAudio = objectBased;
            if (objectBased)
            {
                _maxObjectBasedChannels = channelCount;
            }
        }

        public static void UpdateListenerPosition(int channel, Vector3 newPosition)
        {
            lock (_listenerLockObject)
            {
                if (channel < 0 || channel >= _virtualListeners.Count)
                    return;
                _virtualListeners[channel].rawPosition = newPosition;
                _virtualListenersChanged = true;
            }
        }

        public static void UpdateListenerVolume(int channel, float volume)
        {
            lock (_listenerLockObject)
            {
                if (channel < 0 || channel >= _virtualListeners.Count)
                    return;
                _virtualListeners[channel].volume = volume;
                _virtualListenersChanged = true;
            }
        }

        
        public static void SetAudioTestChannel(int channel)
        {
            lock (_testAudioLockObj)
            {
                _currentTestChannel = channel;
            }
        }

        private static void OnApplicationQuit()
        {
            if (_audioSendStream.IsCreated)
                _audioSendStream.Dispose();
        }

        public static void ClearAllVirtualSpeakerListeners()
        {
            lock (_listenerDataLockObject)
            {
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
                if (!_useVirtualAudio)
                    return null;
                return _virtualListeners.Select( l => l.TransformedPosition).ToArray();
            }
        }

        public static void SetListenerVolume(int channelIndex, float volume)
        {
            lock (_listenerLockObject)
            {
                if (channelIndex < 0 || channelIndex >= _virtualListeners.Count)
                    return;
                _virtualListeners[channelIndex].volume = volume;
                _virtualListenersChanged = true;
            }
        }
        
        public static float[] GetListenersVolume()
        {
            lock (_listenerLockObject)
            {
                if (!_useVirtualAudio)
                    return null;
                
                return _virtualListeners.Select( l => l.volume).ToArray();
            }
        }

        public static void AddListener(Vector3 relativePosition, float volume = 1f)
        {
            var newData = new VirtualListener
            {
                rawPosition = relativePosition,
                volume = volume,
            };

            lock (_listenerLockObject)
            {
                _virtualListeners.Add(newData);
                _virtualListenersChanged = true;
            }

            lock (_listenerDataLockObject)
            {
                _listenerDatas.Add(new ListenerData());
            }
        }

        public static void RemoveListener(int index)
        {
            lock (_listenerLockObject)
            {
                if (index < 0 || index >= _virtualListeners.Count)
                    return;
                _virtualListeners.RemoveAt(index);
                _virtualListenersChanged = true;
            }

            lock (_listenerDataLockObject)
            {
                if (index < 0 || index >= _listenerDatas.Count)
                    return;
                _listenerDatas.RemoveAt(index);
            }
        }

        internal static AudioSourceData RegisterAudioSourceChannel(AudioSourceSettings settings)
        {
            var newData = new AudioSourceData
            {
                id = _audioSourceNextId++,
                settings = settings
            };

            lock (_audioSourceLockObject)
                _audioSourcesData.Add(newData.id, newData);

            return newData;
        }

        private static void ClearAllAudioSourceData()
        {
            lock (_audioSourceLockObject)
            {
                foreach (var a in _audioSourcesData)
                    if (a.Value != null)
                        a.Value.id = -1;
                _audioSourcesData.Clear();
            }
        }
        
        internal static void UnregisterAudioSource(AudioSourceData audioSourceData)
        {
            lock (_audioSourceLockObject)
            {
                _audioSourcesData.Remove(audioSourceData.id);
            }
        }
        
        internal static void CheckSetup()
        {
            bool CheckAudioStreamSize(int size)
            {
                if (!_audioSendStream.IsCreated || _audioSendStream.Length != size)
                {
                    if (_audioSendStream.IsCreated)
                        _audioSendStream.Dispose();
                    _audioSendStream = new NativeArray<float>(size, Allocator.Persistent);
                    return false;
                }

                return true;
            }
            
            if (_objectBasedAudio)
            {
                
                int streamSize = _maxObjectBasedChannels * _dspBufferSize;
                if (CheckAudioStreamSize(streamSize))
                {
                    lock (_audioSourcesData)
                    {
                        for (int i = 0; i < _audioSourcesData.Count; i++)
                        {
                            var audioSourceData = _audioSourcesData.ElementAt(i).Value;
                            if (audioSourceData.objectBasedChannel != -1)
                                audioSourceData.audioStreamDestination = _audioSendStream.GetSubArray(audioSourceData.objectBasedChannel * _dspBufferSize, _dspBufferSize);
                        }
                    }
                }
                AssignObjectBasedChannelsToAudioSources();
            
            }
            else
            {
                lock (_listenerDataLockObject)
                {
                    int streamSize = _listenerDatas.Count * _dspBufferSize;
                    CheckAudioStreamSize(streamSize);
                    CreateListenersSubArrays();
                }
            }

            if (_lastCheckSetupTime < AudioSettings.dspTime)
            {
                _lastCheckSetupTime = AudioSettings.dspTime;
                ClearAudioStream();
            }
        }

        private static void ClearAudioStream()
        {
            unsafe
            {
                UnsafeUtility.MemClear(_audioSendStream.GetUnsafePtr(), _audioSendStream.Length * sizeof(float));
            }
        }
        
        private static void AssignObjectBasedChannelsToAudioSources()
        {
            _usedObjectBasedChannels.Clear();
            foreach (var audioSource in _audioSourcesData)
            {
                if (audioSource.Value.objectBasedChannel != -1)
                    _usedObjectBasedChannels.Add(audioSource.Value.objectBasedChannel);
            }
            
            foreach (var audioSource in _audioSourcesData)
            {
                if (audioSource.Value.objectBasedChannel == -1)
                {
                    // Find free channel:
                    for (int i = 0; i < _maxObjectBasedChannels; i++)
                    {
                        if (_usedObjectBasedChannels.Contains(i))
                            continue;

                        audioSource.Value.objectBasedChannel = i;
                        audioSource.Value.audioStreamDestination = _audioSendStream.GetSubArray(i * _dspBufferSize, _dspBufferSize);
                        _usedObjectBasedChannels.Add(i);
                        break;
                    }
                        
                    // No free channel found
                }
            }
        }
        
        internal static bool GetObjectBasedAudio(out NativeArray<float> stream,  out int samples, List<NativeArray<float>> channels, List<Vector3> positions, List<float> gains)
        {
            lock (_audioSourcesData)
            lock (_listenerLockObject)
            {
                samples = _dspBufferSize;

                if (_testMode)
                {
                    // Begin test mode
                    CheckSetup();
                    
                    AddTestSoundToAudioStream();

                    channels.Clear();
                    positions.Clear();
                    gains.Clear();
                    for (int i = 0; i < _maxObjectBasedChannels; i++)
                    {
                        channels.Add( _testAudioData.audioStreamDestination);
                        positions.Add(Vector3.zero);
                        gains.Add(i == _currentTestChannel ? 1 : 0);
                    }
                    
                    stream = _audioSendStream;
                    return true;
                    // End test mode
                }
                
                if (channels.Count != _maxObjectBasedChannels || positions.Count != _maxObjectBasedChannels)
                {
                    channels.Clear();
                    positions.Clear();
                    gains.Clear();
                    for (int i = 0; i < _maxObjectBasedChannels; i++)
                    {
                        channels.Add( new NativeArray<float>());
                        positions.Add(Vector3.zero);
                        gains.Add(0f);
                    }
                }
                
                if (_audioSourcesData.Count == 0)
                {
                    stream = _audioSendStream;
                    return false;
                }

                stream = _audioSendStream;
                
                foreach (var audioSource in _audioSourcesData)
                {
                    var channel = audioSource.Value.objectBasedChannel;
                    if (channel == -1)
                        continue;
                    channels[channel] = audioSource.Value.audioStreamDestination;
                    positions[channel] = audioSource.Value.settings.position;
                    gains[channel] = audioSource.Value.settings.volume;
                }

                // Reset Data for not used channels
                for (int i = 0; i < _maxObjectBasedChannels; i++)
                {
                    if (!_usedObjectBasedChannels.Contains(i))
                    {
                        channels[i] = new NativeArray<float>();
                        positions[i] = Vector3.zero;
                        gains[i] = 0f;
                    }
                }
            }

            return true;
        }
        
        public static void SetAudioDataFromSource(int id, float[] data, int channelCount)
        {
            if (!_useVirtualAudio)
            {
                if (VirtualAudio.MuteAudioOutput)
                    Array.Fill(data, 0f);
                return;
            }
            
            lock (_audioSourceLockObject)
            {
                if (!_audioSourcesData.TryGetValue(id, out var audioSourceData))
                {
                    if (VirtualAudio.MuteAudioOutput)
                        Array.Fill(data, 0f);
                    return;
                }
                
                CheckSetup();
                
                unsafe
                {
                    var dataPtr = UnsafeUtility.PinGCArrayAndGetDataAddress(data, out var handle);
                    var nativeData =
                        NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<float>(dataPtr, data.Length,
                            Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    var safety = AtomicSafetyHandle.Create();
                    NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref nativeData, safety);
#endif
                    
                    var inputPtr = (float*)dataPtr;

                    if (_objectBasedAudio)
                    {
                        var outputPtr = (float*)audioSourceData.audioStreamDestination.GetUnsafePtr();
                        BurstMethods.MixToMono(inputPtr, data.Length, outputPtr, channelCount);
                    }
                    else
                    {
                        lock (_listenerDataLockObject)
                        {
                            audioSourceData.CheckWeightsArray(_listenerDatas.Count);

                            int forceToChannel = audioSourceData.settings.forceToChannel;
                            if (forceToChannel != -1)
                            {
                                if (forceToChannel > _listenerDatas.Count - 1)
                                {
                                    Debug.LogError(
                                        "Can't force AudioSource to channel. Wrong channel is set. OutOfBound");
                                }
                                else
                                {
                                    for (int j = 0; j < audioSourceData.currentWeights.Length; j++)
                                        audioSourceData.currentWeights[j] = forceToChannel == j ? 1 : 0;
                                }
                            }
                            
                            audioSourceData.UpdateSmoothingWeights();
                            DownMixToMonoAndMixDataToListeners(nativeData, channelCount,
                                audioSourceData.smoothedWeights);
                        }
                    }
                    

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    AtomicSafetyHandle.Release(safety);
#endif
                    UnsafeUtility.ReleaseGCObject(handle);
                }
            }
            if (VirtualAudio.MuteAudioOutput)
                Array.Fill(data, 0f);
        }
        
#region Test Sound
        
        private static int _textSound_nextTick = 0;
        private static float _testSound_amp = 0.0F;
        private static float _testSound_phase = 0.0F;
        private static int _testSound_accent = 0;
        
        private static void AddTestSoundToAudioStream()
        {
            ClearAudioStream();

            if (!_testAudioData.audioStreamDestination.IsCreated || _testAudioData.audioStreamDestination.Length != _dspBufferSize)
            {
                if (_testAudioData.audioStreamDestination.IsCreated)
                    _testAudioData.audioStreamDestination.Dispose();
                _testAudioData.audioStreamDestination = new NativeArray<float>(_dspBufferSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }
                    
            GenerateTestSound(_testAudioData.audioStreamDestination, 0, _testAudioData.audioStreamDestination.Length);
            
            unsafe
            {
                if (_objectBasedAudio)
                {
                    _testAudioData.CheckWeightsArray(_maxObjectBasedChannels);
                    for (int i = 0; i < _maxObjectBasedChannels; i++)
                        _testAudioData.currentWeights[i] = i == _currentTestChannel ? 1f : 0f;
                    _testAudioData.UpdateSmoothingWeights();
                    
                    for (int i = 0; i < _maxObjectBasedChannels; i++)
                    {
                        BurstMethods.MixArrays((float*)_audioSendStream.GetUnsafePtr(), i * _dspBufferSize, (float*)_testAudioData.audioStreamDestination.GetUnsafePtr(),
                            0, _dspBufferSize, _testAudioData.smoothedWeights[i]);
                    }
                }
                else
                {
                    CreateListenersSubArrays();
                    
                    _testAudioData.CheckWeightsArray(_listenerDatas.Count);
                    for (int i = 0; i < _listenerDatas.Count; i++)
                        _testAudioData.currentWeights[i] =  i == _currentTestChannel ? 1f : 0f;
                    _testAudioData.UpdateSmoothingWeights();
                    
                    MixDataToListeners(_testAudioData.audioStreamDestination, _testAudioData.smoothedWeights);
                }
            }
        }
        
        private static void GenerateTestSound(NativeArray<float> destination, int startIndex, int length)
        {
            const int bpm = 140;
            const float gain = 0.5F;
            const int signatureHi = 4;
            const int signatureLo = 4;
        
            int samplesPerTick = _sampleRate * 60 / bpm * 4 / signatureLo;
            
            lock (_testAudioLockObj)
            {
                if (_testAudioDspStartTime < 0)
                {
                    _testAudioDspStartTime = AudioSettings.dspTime;
                    _testSound_accent = signatureHi;
                    _textSound_nextTick = samplesPerTick;
                }
            }
        
            for (int n = 0; n < length; n++)
            {
                float x = gain * _testSound_amp * Mathf.Sin(_testSound_phase);
                destination[n + startIndex] = x;
                _textSound_nextTick--;
                if (_textSound_nextTick <= 0)
                {
                    _textSound_nextTick = samplesPerTick;
                    _testSound_amp = 1.0F;
                    if (++_testSound_accent > signatureHi)
                    {
                        _testSound_accent = 1;
                        _testSound_amp *= 2.0F;
                        _testSound_phase = 0;
                    }
                }
                _testSound_phase += _testSound_amp * 0.3F;
                _testSound_amp *= 0.993F;
            }
        }
#endregion

        private static void UpdateVirtualListeners()
        {
            if (!_virtualListenersChanged)
                return;

            lock (_listenerLockObject)
            {
                _virtualListenersChanged = false;

                if (_virtualListeners.Count == 0)
                    return;

                float height = _virtualListeners[0].TransformedPosition.y;
                _allListenersAreOnSameHeight = true;

                unsafe
                {
                    for (int i = 0; i < _virtualListeners.Count(); i++)
                    {
                        BurstMethods.GetSphericalCoordinates(out _virtualListeners[i].sphericalCoordinate, _virtualListeners[i].TransformedPosition);

                        if (Math.Abs(height - _virtualListeners[i].TransformedPosition.y) > 0.01f)
                            _allListenersAreOnSameHeight = false;
                    }
                }
            }
        }

        internal static void UpdateAudioSourceToListenerWeights(Vector3 cameraPosition, bool useCameraPosForAttenuation = false)
        {
            UpdateVirtualListeners();

            if (ObjectBasedAudio)
                return;
            
            lock (_audioSourcesData)
            lock (_listenerLockObject)
            {
                float avgListenerDistanceFromCamera = GetAvgListenerDistanceFromCamera(cameraPosition);
                var transformedCameraPosition = ApplyOrientationTransform(cameraPosition);
                foreach (var audioSourceKVP in _audioSourcesData)
                {
                    var audioSource = audioSourceKVP.Value;
                    var audioSourceSettings = audioSource.settings;
                    
                    audioSource.CheckWeightsArray(_virtualListeners.Count);
                    
                    float cameraDistanceToAudioSource = Vector3.Distance(audioSourceSettings.position, transformedCameraPosition);
                    
                    Array.Fill(audioSource.currentWeights, 0f);
                    
                    var usedDistance = useCameraPosForAttenuation
                        ? cameraDistanceToAudioSource
                        : Mathf.Max(0, cameraDistanceToAudioSource - avgListenerDistanceFromCamera);
                    var audioSourceAttenuation = GetDistanceAttenuation(usedDistance, audioSourceSettings);
                    var spatialBlend = GetSpatialBlend(audioSourceSettings, audioSourceAttenuation);
                    var blendToCenter = _centeredAudioSourceOnAllListeners ? 
                        Mathf.Pow(Mathf.Clamp01(Mathf.InverseLerp( avgListenerDistanceFromCamera, 1f, cameraDistanceToAudioSource)), 2f)
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
                        CalculateWeightsBasedOnSimplePlanarAzimuthPanning(audioSource.currentWeights, audioSourceSettings, blendToCenter, spatialBlend);
                    }
                    else
                    {
                        // TODO: height panning !! Currently we ignore the height of the listeners
                        CalculateWeightsBasedOnSimplePlanarAzimuthPanning(audioSource.currentWeights, audioSourceSettings, blendToCenter, spatialBlend);
                    } 
     
                    ApplyDistanceAttenuationAndSourceVolumeToWeights();
                }
            }
        }

        private static void CreateListenersSubArrays()
        {
            for (int i = 0; i < _listenerDatas.Count(); i++)
            {
                _listenerDatas[i].audioStreamDestination = _audioSendStream.GetSubArray(i * _dspBufferSize, _dspBufferSize);
            }
        }
        
        /// <summary>
        /// Get the final mixed audio channels
        /// </summary>
        /// <param name="stream">returns the mixed audio stream. Dispose should not be called on this NativeArray.</param>
        /// <param name="samples">returns the sample amount</param>
        /// <returns></returns>
        internal static List<NativeArray<float>> GetMixedAudio(out NativeArray<float> stream, out int samples, out float[] vus)
        {
            samples = _dspBufferSize;
            lock (_audioSourcesData)
            lock (_listenerDataLockObject)
            {
                List<NativeArray<float>> result = new List<NativeArray<float>>(_listenerDatas.Count);

                CheckSetup();
                stream = _audioSendStream;
                
                if (_virtualListeners.Count == 0)
                {
                    vus = new float[_listenerDatas.Count];
                    OnAudioStreamUpdated.Invoke(_audioSendStream, 0);
                    return result;
                }
                
                if (_testMode)
                {
                    for (int iListener = 0; iListener < _listenerDatas.Count; iListener++)
                    {
                        var listener = _listenerDatas[iListener];
                        result.Add(listener.audioStreamDestination);
                    }
                    AddTestSoundToAudioStream();
                    
                    vus = new float[_listenerDatas.Count];
                    vus[_currentTestChannel] = 1f;
                    
                    OnAudioStreamUpdated.Invoke(_audioSendStream, _listenerDatas.Count);

                    return result;
                }
                
                vus = new float[_listenerDatas.Count];
                unsafe
                {
                    for (int i = 0; i < _listenerDatas.Count; i++)
                    {
                        var inputPtr = (float*)_listenerDatas[i].audioStreamDestination.GetUnsafeReadOnlyPtr();
                        BurstMethods.GetVU(inputPtr, _listenerDatas[i].audioStreamDestination.Length, out var vu);
                        vus[i] = vu;
                        
                        result.Add(_listenerDatas[i].audioStreamDestination);
                    }
                }
                
                OnAudioStreamUpdated.Invoke(_audioSendStream, _listenerDatas.Count);
                return result;
            }
        }
        
        private static void MixDataToListeners(NativeArray<float> data, float[] weights)
        {
            if (weights == null || data.Length == 0 || weights.Length != _listenerDatas.Count)
                return;

            for (int iListener = 0; iListener < _listenerDatas.Count; iListener++)
            {
                var listener = _listenerDatas[iListener];

                if (listener.audioStreamDestination.Length != data.Length)
                    // This should never happen!
                    Debug.LogError("Channel data length does not match audio source data length!");
                
                unsafe
                {
                    var listenerPtr = (float*)listener.audioStreamDestination.GetUnsafeReadOnlyPtr();
                    var audioSourcePtr = (float*)data.GetUnsafePtr();
                            
                    BurstMethods.MixArrays(listenerPtr, audioSourcePtr, listener.audioStreamDestination.Length, weights[iListener]);
                }
            }
        }
        
        private static void DownMixToMonoAndMixDataToListeners(NativeArray<float> data, int dataChannels, float[] weights)
        {
            if (weights == null || data.Length == 0 || weights.Length != _listenerDatas.Count)
                return;

            for (int iListener = 0; iListener < _listenerDatas.Count; iListener++)
            {
                var listener = _listenerDatas[iListener];
                if (weights[iListener] <= 0.001f)
                    continue;

                if (listener.audioStreamDestination.Length != data.Length / dataChannels)
                    // This should never happen!
                    Debug.LogError("Channel data length does not match audio source data length!");
                
                unsafe
                {
                    var listenerPtr = (float*)listener.audioStreamDestination.GetUnsafeReadOnlyPtr();
                    var audioSourcePtr = (float*)data.GetUnsafePtr();
                            
                    BurstMethods.MixArraysWithDownMixToMono(listenerPtr, audioSourcePtr, listener.audioStreamDestination.Length, dataChannels, weights[iListener]);
                }
            }
        }
        
        private static void CalculateWeightsBasedOnSimplePlanarAzimuthPanning(float[] weights, AudioSourceSettings audioSourceSettings, float centerBlend, float spatialBlend)
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
                
                w = Mathf.Lerp(w, 1f, 1f - spatialBlend);
                w *= _virtualListeners[i].volume;
                
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

#region Helpers
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

        private static float GetAvgListenerDistanceFromCamera(Vector3 cameraPosition)
        {
            cameraPosition = ApplyOrientationTransform(cameraPosition);
            
            float distanceFromCameraAvg = 0;
            foreach (var listener in _virtualListeners)
            {
                float d = Vector3.Distance(cameraPosition + listener.TransformedPosition, cameraPosition);
                distanceFromCameraAvg += d;
            }

            distanceFromCameraAvg /= _virtualListeners.Count;
            return distanceFromCameraAvg;
        }
        
        private static float GetSpatialBlend(AudioSourceSettings audioSourceSettings, float audioSourceAttenuation)
        {
            if (audioSourceSettings.spatialBlendCurve == null)
                return 0;
            
            float spatialBlend = audioSourceSettings.spatialBlend *
                                 audioSourceSettings.spatialBlendCurve.Evaluate(
                                     Mathf.Clamp01(Mathf.Lerp(audioSourceSettings.minDistance,
                                         audioSourceSettings.maxDistance, audioSourceAttenuation)));
            return spatialBlend;
        }

        private static void ApplySpatialBlendToWeights(float[] weights, float spatialBlend)
        {
            var w = new float[weights.Length];
            Array.Fill(w, 0);
            float sum = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                w[i] = _virtualListeners[i].volume * weights[i];
                sum += 0;
            }
            
            
            for (int i = 0; i < weights.Length; i++)
            {
                float weight = weights[i];
                float spatial = Mathf.Lerp(weight, 1f, 1f - spatialBlend);
                weights[i] = spatial;
            }
        }
#endregion

    }
}