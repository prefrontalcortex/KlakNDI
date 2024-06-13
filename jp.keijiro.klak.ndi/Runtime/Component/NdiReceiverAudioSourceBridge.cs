using System;
using UnityEngine;

namespace Klak.Ndi
{
	[RequireComponent(typeof(AudioSource))]
	internal class AudioSourceBridge : MonoBehaviour
	{
		internal bool _isDestroyed;
		internal NdiReceiver _handler;
		private int _customChannel = -1;
		private int _maxChannels = -1;
		internal static double _lastFrameUpdate = -1;
		
		private static float[] _spatializedData;
		private static AudioClip _spatilizeHelperClip;
		private bool _noSpatializerPlugin = false;
		
		private void Awake()
		{
			hideFlags = HideFlags.NotEditable;
		}

		public void Init(bool isVirtualSpeaker, int maxSystemChannels, int virtualSpeakerChannel = -1, int maxVirtualSpeakerChannels = -1, bool usingSpatializerPlugin = false)
		{
			_customChannel = virtualSpeakerChannel;
			_maxChannels = maxVirtualSpeakerChannels;
			_noSpatializerPlugin = !usingSpatializerPlugin;
			
			// Workaround for external AudioSources: Stop playback because otherwise volume and all it's other properties do not get applied.
			AudioSource audioSource = GetComponent<AudioSource>();
			if (isVirtualSpeaker && !audioSource.spatialize)
			{
			
				if (!_spatilizeHelperClip)
				{
					var sampleRate = AudioSettings.GetConfiguration().sampleRate;
					var dspBufferSize = AudioSettings.GetConfiguration().dspBufferSize;
					_spatilizeHelperClip = AudioClip.Create("dummy", dspBufferSize, 1, sampleRate, false);
					_spatializedData = new float[dspBufferSize];
					Array.Fill(_spatializedData, 1f);
					for(int i = 0; i < _spatializedData.Length; i++)
					{
						_spatializedData[i] = 1;
					}
					_spatilizeHelperClip.SetData(_spatializedData, 0);
					//_isSpatialized = true;
				}
				
				audioSource.loop = true;
				audioSource.clip = _spatilizeHelperClip;
			}

			audioSource.dopplerLevel = 0;
			audioSource.Stop();
			audioSource.Play();				
		}
		
		// Automagically called by Unity when an AudioSource component is present on the same GameObject
		private void OnAudioFilterRead(float[] data, int channels)
		{
			if (!_handler)
				return;
			
			if (_customChannel != -1)
			{
				// We have multiple AudioSource to simulate multiple speakers,
				// in case Unity Audio channels does not match the received data
				if (_lastFrameUpdate < AudioSettings.dspTime)
				{
					//Debug.Log("AudioSourceBridge: Updating audio data. " + _lastFrameUpdate + " < " + AudioSettings.dspTime );
					if (!_handler.PullNextAudioFrame(data.Length, channels))
					{
						Array.Fill(data, 0f);
					}
					else
						_lastFrameUpdate = AudioSettings.dspTime;
				}

				if (!_handler.FillAudioChannelData(ref data, _customChannel, channels, _noSpatializerPlugin))
				{
					Array.Fill(data, 0f);
					return;
				}
			}
			else
			{
				if (!_handler.FillPassthroughData(ref data, channels))
					Array.Fill(data, 0f);
			}
		}

		private void OnDestroy()
		{
			if (_isDestroyed)
				return;

			_isDestroyed = true;

			if (_handler) _handler.HandleAudioSourceBridgeOnDestroy();
		}
	}
	
}