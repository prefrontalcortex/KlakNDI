using System;
using Klak.Ndi.Interop;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Klak.Ndi
{
	internal class AudioFrameData : IDisposable
	{
		public int sampleRate;
		public int noChannels;
		public string meta;

		public int samplesPerChannel;
		private NativeArray<float> data;
		
		public Vector3[] speakerPositions;
		public bool isObjectBased = false;

		public int[] channelSamplesReaded;
		
		public NativeArray<float> GetChannelArray(int channel, int maxSamples = -1)
		{
			if (maxSamples == -1)
				return data.GetSubArray( samplesPerChannel * channel, samplesPerChannel );
			
			int samplesAvailable = samplesPerChannel - channelSamplesReaded[channel];
			if (maxSamples > samplesAvailable)
				maxSamples = samplesAvailable;
			
			return data.GetSubArray( samplesPerChannel * channel + channelSamplesReaded[channel], maxSamples );
		}

		public void Set(AudioFrame audio)
		{
			sampleRate = audio.SampleRate;
			noChannels = audio.NoChannels;
			meta = audio.Metadata;
			
			int sizeInBytes = audio.NoSamples * audio.NoChannels * sizeof(float);
			channelSamplesReaded = new int[noChannels];
			Array.Fill(channelSamplesReaded, 0);

			samplesPerChannel = audio.NoSamples;
			if (meta != null)
				speakerPositions = AudioMeta.GetSpeakerConfigFromXml(meta, out isObjectBased);
			
			int totalSamples = audio.NoSamples * audio.NoChannels;
			unsafe
			{
				void* audioDataPtr = audio.Data.ToPointer();

				if (audioDataPtr != null)
				{
					if (!data.IsCreated || data.Length < totalSamples)
					{
						if (data.IsCreated)
							data.Dispose();
						data = new NativeArray<float>(audio.NoSamples * audio.NoChannels, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
					}

					// Grab data from native array
					var tempSamplesPtr =data.GetUnsafePtr();
					//var tempSamplesPtr = UnsafeUtility.PinGCArrayAndGetDataAddress(data, out ulong tempSamplesHandle);
					UnsafeUtility.MemCpy(tempSamplesPtr, audioDataPtr, sizeInBytes);
					//UnsafeUtility.ReleaseGCObject(tempSamplesHandle);
				}
			}
		}

		public void Dispose()
		{
			data.Dispose();
		}
	}
}