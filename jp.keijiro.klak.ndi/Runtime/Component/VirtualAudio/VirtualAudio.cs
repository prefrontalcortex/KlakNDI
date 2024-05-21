using System;
using System.Collections.Generic;
using System.Linq;
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
        }
        
        public class AudioSourceData
        {
            internal int id;
            public float[] data;

            public int forceToChannel = -1;
            
            public AudioSourceSettings settings;
        }

        private class SpeakerData
        {
            public Vector3 position;
            public float volume;
            public float speakerDotSubtract = 0.5f;
            public float[] audioData;

            public void ResetAudioData(int sampleSize)
            {
                if (audioData == null || audioData.Length < sampleSize)
                {
                    audioData = new float[sampleSize];
                }
                
                Array.Fill(audioData, 0, 0, audioData.Length);
            }
        }

        public static bool useVirtualAudio = false;

        private static int _audioSourceNextId = 0;

        private static readonly Dictionary<int, AudioSourceData> _audioSourcesData = new Dictionary<int, AudioSourceData>();
        private static readonly List<SpeakerData> _speakersData = new List<SpeakerData>();

        private static readonly List<float> _attenuationWeights = new List<float>();
        private static readonly List<float> _spatialBlends = new List<float>();
        private static readonly List<float> _speakerDotProducts = new List<float>();
        private static readonly List<float> _weights = new List<float>();
        private static readonly List<float> _distances = new List<float>();
        
        private static readonly object _speakerLockObject = new object();
        private static readonly object _audioSourceLockObject = new object();


        
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            _audioSourcesData.Clear();
            _speakersData.Clear();
        }

        public static void ClearAllVirtualSpeakerListeners()
        {
            lock (_speakerLockObject)
            {
                _speakersData.Clear();
            }
        }

        public static void AddSpeaker(Vector3 relativePosition, float dotDirectionAdjust = 0.5f, float volume = 1f)
        {
            var newData = new SpeakerData
            {
                position = relativePosition,
                volume = volume,
                speakerDotSubtract = dotDirectionAdjust,
                audioData = Array.Empty<float>()
            };

            lock (_speakerLockObject)
            {
                _speakersData.Add(newData);
            }
        }

        public static AudioSourceData RegisterAudioSourceChannel(int forceToChannel = -1)
        {
            var newData = new AudioSourceData
            {
                id = _audioSourceNextId++
            };
            newData.forceToChannel = forceToChannel;

            lock (_audioSourceLockObject)
                _audioSourcesData.Add(newData.id, newData);

            return newData;
        }
        
        public static void UnRegisterAudioSource(AudioSourceData audioSourceData)
        {
            lock (_audioSourceLockObject)
                _audioSourcesData.Remove(audioSourceData.id);
        }
        
        private static float GetCameraAvgDistance(Vector3 cameraPosition)
        {
            float distanceFromCameraAvg = 0;
            foreach (var speaker in _speakersData)
            {
                float d = Vector3.Distance(cameraPosition + speaker.position, cameraPosition);
                _distances.Add(d);
                distanceFromCameraAvg += d;
            }

            distanceFromCameraAvg /= _speakersData.Count;
            return distanceFromCameraAvg;
        }
        
        /// <summary>
        /// Get the final mixed audio channels
        /// </summary>
        /// <param name="samples">returns the sample amount</param>
        /// <param name="cameraPosition">Current camera position</param>
        /// <param name="useCameraPosForAttenuation">When false, the volume attenuations are based on speaker to audiosource distance</param>
        /// <returns></returns>
        public static List<float[]> GetMixedAudio(out int samples, Vector3 cameraPosition,
            bool useCameraPosForAttenuation = false)
        {
            _attenuationWeights.Clear();
            _spatialBlends.Clear();
            _speakerDotProducts.Clear();
            _weights.Clear();
            _distances.Clear();

            List<float[]> result;

            lock (_audioSourcesData)
            lock (_speakerLockObject)
            {
                samples = 0;

                if (_speakersData.Count == 0 || _audioSourcesData.Count == 0)
                    return new List<float[]>(0);


                float distanceFromCameraAvg = GetCameraAvgDistance(cameraPosition);

                foreach (var audioSource in _audioSourcesData)
                {
                    if (samples < audioSource.Value.data.Length)
                        samples = audioSource.Value.data.Length;
                }

                for (int iSpeaker = 0; iSpeaker < _speakersData.Count; iSpeaker++)
                {
                    var speaker = _speakersData[iSpeaker];
                    speaker.ResetAudioData(samples);
                }


                foreach (var audioSource in _audioSourcesData)
                {
                    int forceToChannel = audioSource.Value.forceToChannel;

                    if (forceToChannel != -1)
                    {
                        if (forceToChannel > _speakersData.Count - 1)
                        {
                            Debug.LogError("Can't force AudioSource to channel. Wrong channel is set. OutOfBound");
                        }
                        else
                        {
                            var speaker = _speakersData[forceToChannel];
                            for (int j = 0; j < audioSource.Value.data.Length; j++)
                                speaker.audioData[j] = MixSample(speaker.audioData[j], speaker.volume * audioSource.Value.data[j]);
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

                    var audioSourceSettings = audioSource.Value.settings;

                    foreach (var speaker in _speakersData)
                    {
                        Vector3 centerToSpeakerDir = speaker.position.normalized;
                        // Dot product for speaker direction weighting
                        float dot = Vector3.Dot(centerToSourceDir, centerToSpeakerDir);
                        _speakerDotProducts.Add(Mathf.Clamp01(dot - speaker.speakerDotSubtract));

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

                    float dotSum = _speakerDotProducts.Sum();
                    for (int i = 0; i < _speakerDotProducts.Count; i++)
                        _speakerDotProducts[i] = Mathf.Clamp01(_speakerDotProducts[i] / dotSum);

                    for (int i = 0; i < _attenuationWeights.Count; i++)
                    {
                        float volume = _attenuationWeights[i] * audioSourceSettings.volume;
                        float dotWeight = volume * _speakerDotProducts[i];
                        float weight = Mathf.Clamp01(dotWeight + centerWeight);
                        float spatial = Mathf.Lerp(weight, 1f, 1f - _spatialBlends[i]);
                        _weights.Add(spatial * _speakersData[i].volume * 2f);
                    }

                    for (int iSpeaker = 0; iSpeaker < _speakersData.Count; iSpeaker++)
                    {
                        var speaker = _speakersData[iSpeaker];

                        if (speaker.audioData.Length != audioSource.Value.data.Length)
                        {
                            // This should never happen!
                            Debug.LogError("Channel data length does not match audio source data length!");
                        }

                        for (int j = 0; j < audioSource.Value.data.Length; j++)
                        {
                            var audioValue = audioSource.Value.data[j] * _weights[iSpeaker];
                            audioValue = MixSample(speaker.audioData[j], audioValue);

                            speaker.audioData[j] = audioValue;
                        }
                    }
                }

                result = _speakersData.Select(s => s.audioData).ToList();
            }

            return result;
        }
        
        #region Helpers
        private static float GetDistanceAttenuation(float distance, AudioSourceSettings audioSettings)
        {
            switch (audioSettings.rolloffMode)
            {
                case AudioRolloffMode.Logarithmic:
                    return LogAttenuation(distance, audioSettings.minDistance,
                        audioSettings.maxDistance);
                    break;
                case AudioRolloffMode.Linear:
                    return Mathf.Clamp01(Mathf.Lerp(1f, 0f,
                        Mathf.InverseLerp(audioSettings.minDistance, audioSettings.maxDistance, distance)));
                    break;
                case AudioRolloffMode.Custom:
                    return
                        audioSettings.customRolloffCurve.Evaluate(distance / audioSettings.maxDistance);
                    break;
                default:
                    return 0;
            }
        }

        private static float LogAttenuation(float distance, float minDistance, float maxDistance)
        {
            float ratio = distance / minDistance;
            return distance <= minDistance ? 1 : (1.0f / (1.0f + 2f * Mathf.Log(ratio)));
        }

        internal static float MixSample(float sample1, float sample2)
        {
            if (sample1 > 0 && sample2 > 0)
            {
                return Mathf.Max(sample1, sample2);
            }
            else if (sample1 < 0 && sample2 < 0)
            {
                return Mathf.Min(sample1, sample2);
            }
            else
            {
                return sample1 + sample2;
            }
        }
        #endregion
    }
}