using UnityEngine;

namespace Klak.Ndi.Audio
{
	internal class VirtualSpeakers
	{
		public AudioSource speakerAudio;
		public Vector3 relativePosition;
		private AudioSourceBridge _audioSourceBridge;

		public void UpdateParameters(int channelNo, int maxChannels, int systemChannels, bool objectBasedAudio = false)
		{
			SetAudioSourceParameters(objectBasedAudio);

			speakerAudio.gameObject.SetActive(true);
			_audioSourceBridge.Init(true, systemChannels, channelNo, maxChannels, !string.IsNullOrEmpty(AudioSettings.GetSpatializerPluginName()));
			
		}
		
		private void SetAudioSourceParameters(bool objectBasedAudio = false)
		{
			if (speakerAudio == null)
				return;

			if (objectBasedAudio)
			{
				speakerAudio.spatialBlend = 1f;
				speakerAudio.minDistance = 5f;
				speakerAudio.rolloffMode = AudioRolloffMode.Logarithmic;
			}
			else
			{
				speakerAudio.rolloffMode = AudioRolloffMode.Custom;
				speakerAudio.SetCustomCurve( AudioSourceCurveType.CustomRolloff, AnimationCurve.Linear(0, 1, 1, 1) );
				speakerAudio.maxDistance = 500;
				speakerAudio.spatialBlend = 1f;
			}
		}
		
		public void CreateGameObjectWithAudioSource(Transform parent, Vector3 localPosition, bool objectBasedAudio = false)
		{
			var go = new GameObject();
			go.transform.parent = parent;
			go.transform.localPosition = localPosition;
			go.transform.rotation = localPosition == Vector3.zero ? Quaternion.identity : Quaternion.LookRotation( -localPosition.normalized);
			go.name = "VirtualSpeaker";
			go.SetActive(false);
			go.hideFlags = HideFlags.NotEditable | HideFlags.DontSave;
			speakerAudio = go.AddComponent<AudioSource>();
			speakerAudio.loop = true;
			SetAudioSourceParameters(objectBasedAudio);

			if (!string.IsNullOrEmpty(AudioSettings.GetSpatializerPluginName()))
			{
				speakerAudio.spatialize = true;
				speakerAudio.spatializePostEffects = true;
			}
			
			go.SetActive(true);
			
			speakerAudio.Play();
		}
		
		public void CreateAudioSourceBridge(NdiReceiver handle, int channelNo, int maxChannels, int systemChannels)
		{
			if (speakerAudio == null)
				return;

			if (_audioSourceBridge != null)
				return;

			speakerAudio.gameObject.name = $"VirtualSpeaker {channelNo}";
			_audioSourceBridge = speakerAudio.gameObject.AddComponent<AudioSourceBridge>();
			_audioSourceBridge._handler = handle;
			_audioSourceBridge.Init(true, systemChannels, channelNo, maxChannels, !string.IsNullOrEmpty(AudioSettings.GetSpatializerPluginName()));
		}
		
		public void DestroyAudioSourceBridge()
		{
			if (_audioSourceBridge == null)
				return;

			_audioSourceBridge._handler = null;

			if(_audioSourceBridge._isDestroyed == false)
				GameObject.DestroyImmediate(_audioSourceBridge);

			_audioSourceBridge = null;
		}
	}
}