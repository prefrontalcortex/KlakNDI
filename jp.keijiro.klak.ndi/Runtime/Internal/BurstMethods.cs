using System.Runtime.CompilerServices;
using Klak.Ndi.Audio;
using Unity.Burst;
using Unity.Mathematics;

namespace Klak.Ndi
{
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
                        destination[i * destinationChannelCo + c] = monoSource[i + sourceStartIndex];
            }
            
            [BurstCompile]
            public static unsafe void UpMixMonoWithDestination(float* monoSource, int sourceStartIndex, float* destination, int destOffset, int destinationChannelCo,
                int length)
            {
                for (int i = 0; i < length; i++)
                    for (int c = 0; c < destinationChannelCo; c++)
                        destination[i * destinationChannelCo + c] = monoSource[i + sourceStartIndex] * math.abs(destination[i * destinationChannelCo + c]);
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
            [MethodImpl(MethodImplOptions.AggressiveInlining)] 
            internal static float MixSample(float sample1, float sample2)
            {
                float s1 = math.sign(sample1);
                return sample1 + sample2 + (((sample1 * sample2) * s1 * -1f) * ((s1 + math.sign(sample2) / 2f * s1)));
            }

            [BurstCompile]
            internal static unsafe void MixArrays(float* destination, float* source, int length, float volume)
            {
                for (int i = 0; i < length; i++)
                {
                    destination[i] = MixSample(destination[i], source[i] * volume);
                }
            }

            [BurstCompile]
            internal static unsafe void GetVU(float* source, int length, out float vu)
            {
                vu = 0f;
                for (int i = 0; i < length; i++)
                    vu = math.max(vu, math.abs(source[i]));
            }
            
            [BurstCompile]
            internal static unsafe void GetVUs(float* source, int length, int channels, float* vus)
            {
                for (int i = 0; i < channels; i++)
                    vus[i] = 0;
                
                for (int i = 0; i < length; i += channels)
                    for (int c = 0; c < channels; c++)
                        vus[c] =  math.max(vus[c], math.abs(source[i + c]));
            }
    
            [BurstCompile]
            internal static unsafe void GetSphericalCoordinates(out VirtualAudio.SphericalCoordinate sphericalOut, in float3 pos, bool return360Range = true)
            {
                var azimuth = math.degrees(math.atan2(pos.x, pos.z));
                
                if (return360Range && azimuth < 0)
                    azimuth += 360 ;
                
                var elevation = math.degrees(math.atan2(pos.y, math.sqrt(pos.x * pos.x + pos.z * pos.z))) ;
                var distance = math.length(pos);

                sphericalOut.azimuth = azimuth;
                sphericalOut.elevation = elevation;
                sphericalOut.distance = distance;
            }
        }

}