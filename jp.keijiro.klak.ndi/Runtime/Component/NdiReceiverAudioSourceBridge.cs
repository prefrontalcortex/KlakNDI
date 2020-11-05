using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Klak.Ndi
{
	public sealed partial class NdiReceiver : MonoBehaviour
	{
		[RequireComponent(typeof(AudioSource))]
		private class AudioSourceBridge : MonoBehaviour
		{
			internal bool _isDestroyed;
			internal NdiReceiver _handler;

			private void Awake()
			{
				hideFlags = HideFlags.NotEditable;

				// Workaround for external AudioSources: Stop playback because otherwise volume and all it's other properties do not get applied.
				AudioSource audioSource = GetComponent<AudioSource>();
				audioSource.Stop();
				audioSource.Play();
			}

			// Automagically called by Unity when an AudioSource component is present on the same GameObject
			private void OnAudioFilterRead(float[] data, int channels)
			{
				_handler.HandleAudioFilterRead(data, channels);
			}

			private void OnDestroy()
			{
				if (_isDestroyed)
					return;

				_isDestroyed = true;

				_handler?.HandleAudioSourceBridgeOnDestroy();
			}
		}
	}
}