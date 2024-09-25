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
		private AudioSource _audioSource;
		
		private void Awake()
		{
			hideFlags = HideFlags.NotEditable;
			_audioSource = GetComponent<AudioSource>();
		}

		public void Init(bool isVirtualSpeaker, int maxSystemChannels, int virtualSpeakerChannel = -1, int maxVirtualSpeakerChannels = -1, bool usingSpatializerPlugin = false)
		{
			_customChannel = virtualSpeakerChannel;
			_maxChannels = maxVirtualSpeakerChannels;
			_noSpatializerPlugin = !usingSpatializerPlugin;
			
			// Workaround for external AudioSources: Stop playback because otherwise volume and all it's other properties do not get applied.
			if (!_audioSource)
				_audioSource = GetComponent<AudioSource>();
			if (isVirtualSpeaker && !_audioSource.spatialize)
			{
				var dspBufferSize = AudioSettings.GetConfiguration().dspBufferSize;
			
				if (!_spatilizeHelperClip || _spatilizeHelperClip.channels == 0 || _spatilizeHelperClip.samples != dspBufferSize)
				{
					if (_spatilizeHelperClip)
						Destroy(_spatilizeHelperClip);
					
					var sampleRate = AudioSettings.GetConfiguration().sampleRate;
					_spatilizeHelperClip = AudioClip.Create("dummy", dspBufferSize, 1, sampleRate, false);
					_spatializedData = new float[dspBufferSize];
					Array.Fill(_spatializedData, 1f);
			
					_spatilizeHelperClip.SetData(_spatializedData, 0);
				}
				
				_audioSource.loop = true;
				_audioSource.clip = _spatilizeHelperClip;
			}

			_audioSource.dopplerLevel = 0;
			_audioSource.Stop();
			_audioSource.Play();				
		}
		
		// Automagically called by Unity when an AudioSource component is present on the same GameObject
		private void OnAudioFilterRead(float[] data, int channels)
		{
			if (!_handler)
			{
				Array.Fill(data, 0f);	
				return;
			}
			
			if (_customChannel != -1)
			{
				// We have multiple AudioSource to simulate multiple speakers,
				// in case Unity Audio channels does not match the received data
				if (_lastFrameUpdate < AudioSettings.dspTime)
				{
					//Debug.Log("AudioSourceBridge: Updating audio data. " + _lastFrameUpdate + " < " + AudioSettings.dspTime );
					if (!_handler.PullNextAudioFrame(data.Length / channels, channels))
					{
						Array.Fill(data, 0f);
					}
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