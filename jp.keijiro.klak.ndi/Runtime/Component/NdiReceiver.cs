using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CircularBuffer;
using Klak.Ndi.Audio;
using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using IntPtr = System.IntPtr;

namespace Klak.Ndi {

// FIXME: re-enable the execute in edit mode (with on/off/mute toggle?)
//[ExecuteInEditMode]
public sealed partial class NdiReceiver : MonoBehaviour
{
    #region Receiver objects

	Interop.Recv _recv;
	FormatConverter _converter;
	MaterialPropertyBlock _override;

    void PrepareReceiverObjects()
    {
        if (_recv == null) _recv = RecvHelper.TryCreateRecv(ndiName);
        if (_converter == null) _converter = new FormatConverter(_resources);
        if (_override == null) _override = new MaterialPropertyBlock();
    }

    void ReleaseReceiverObjects()
    {
        _recv?.Dispose();
        _recv = null;

        _converter?.Dispose();
        _converter = null;

		if(m_aTempAudioPullBuffer.IsCreated)
			m_aTempAudioPullBuffer.Dispose();
	}

	#endregion

    internal void Restart() => ReleaseReceiverObjects();

	void Awake()
	{
		ndiName = _ndiName;

		mainThreadContext = SynchronizationContext.Current;

		if (_override == null) _override = new MaterialPropertyBlock();

		tokenSource = new CancellationTokenSource();
		cancellationToken = tokenSource.Token;

		Task.Run(ReceiveFrameTask, cancellationToken);

		UpdateAudioExpectations();
		AudioSettings.OnAudioConfigurationChanged += AudioSettings_OnAudioConfigurationChanged;
		CheckPassthroughAudioSource();
	}

	private void Update()
	{
		if (_settingsChanged)
		{
			
			//ReadAudioMetaData(audio.Metadata);
			ResetAudioSpeakerSetup();
			_settingsChanged = false;
		}
	}

	void OnDestroy()
	{
		tokenSource?.Cancel();
        ReleaseReceiverObjects();

		AudioSettings.OnAudioConfigurationChanged -= AudioSettings_OnAudioConfigurationChanged;
		DestroyAudioSourceBridge();
	}

    void OnDisable() => ReleaseReceiverObjects();

	#region Receiver implementation

    private CancellationTokenSource tokenSource;
    private CancellationToken cancellationToken;
    private static SynchronizationContext mainThreadContext;

    void ReceiveFrameTask()
	{
		try
		{
			// retrieve frames in a loop
			while (!cancellationToken.IsCancellationRequested)
			{
				PrepareReceiverObjects();

				if (_recv == null)
				{
					Thread.Sleep(100);
					continue;
				}

				Interop.VideoFrame video;
				Interop.AudioFrame audio;
				Interop.MetadataFrame metadata;

				var type = _recv.Capture(out video, out audio, out metadata, 5000);
				switch (type)
				{
					case Interop.FrameType.Audio:
						//Debug.Log($"received {type}: {audio}");
						FillAudioBuffer(audio);
						mainThreadContext.Post(ProcessAudioFrame, audio);
						break;
					case Interop.FrameType.Error:
						//Debug.Log($"received {type}: {video} {audio} {metadata}");
						mainThreadContext.Post(ProcessStatusChange, true);
						break;
					case Interop.FrameType.Metadata:
						//Debug.Log($"received {type}: {metadata}");
						mainThreadContext.Post(ProcessMetadataFrame, metadata);
						break;
					case Interop.FrameType.None:
						//Debug.Log($"received {type}");
						break;
					case Interop.FrameType.StatusChange:
						//Debug.Log($"received {type}: {video} {audio} {metadata}");
						mainThreadContext.Post(ProcessStatusChange, false);
						break;
					case Interop.FrameType.Video:
						//Debug.Log($"received {type}: {video}");
						mainThreadContext.Post(ProcessVideoFrame, video);
						break;
				}
			}
		}
		catch (System.Exception e)
		{
			Debug.LogException(e);
		}
	}

	void ProcessVideoFrame(System.Object data)
	{
		Interop.VideoFrame videoFrame = (Interop.VideoFrame)data;

		if (_recv == null) return;

		// Pixel format conversion
		var rt = _converter.Decode
			(videoFrame.Width, videoFrame.Height,
			Util.HasAlpha(videoFrame.FourCC), videoFrame.Data);

		// Copy the metadata if any.
		metadata = videoFrame.Metadata;

		// Free the frame up.
		_recv.FreeVideoFrame(videoFrame);

		if (rt == null) return;

		// Material property override
		if (_targetRenderer != null)
		{
			_targetRenderer.GetPropertyBlock(_override);
			_override.SetTexture(_targetMaterialProperty, rt);
			_targetRenderer.SetPropertyBlock(_override);
		}

		// External texture update
		if (_targetTexture != null)
			Graphics.Blit(rt, _targetTexture);
	}

	void ProcessAudioFrame(System.Object data)
	{
		Interop.AudioFrame audioFrame = (Interop.AudioFrame)data;

		if (_recv == null) return;

		_recv.FreeAudioFrame(audioFrame);
	}


	void ProcessMetadataFrame(System.Object data)
	{
		Interop.MetadataFrame metadataFrame = (Interop.MetadataFrame)data;

		if (_recv == null) return;

		// broadcast an event that new metadata has arrived?

		Debug.Log($"ProcessMetadataFrame: {metadataFrame.Data}");

		_recv.FreeMetadataFrame(metadataFrame);
	}

	void ProcessStatusChange(System.Object data)
	{
		bool error = (bool)data;

		// broadcast an event that we've received/lost stream?

		Debug.Log($"ProcessStatusChange error = {error}");
	}

	#endregion

	#region Audio implementation

	private readonly object					audioBufferLock = new object();
	private const int						BUFFER_SIZE = 1024 * 32;
	private CircularBuffer<float>			audioBuffer = new CircularBuffer<float>(BUFFER_SIZE);
	//
	private bool							m_bWaitForBufferFill = true;
	private const int						m_iMinBufferAheadFrames = 8;
	//
	private NativeArray<byte>				m_aTempAudioPullBuffer;
	private Interop.AudioFrameInterleaved	interleavedAudio = new Interop.AudioFrameInterleaved();
	//
	private float[]							m_aTempSamplesArray = new float[ 1024 * 32 ];
	
	private int _expectedAudioSampleRate;
	private int _systemAvailableAudioChannels;

	private int _receivedAudioSampleRate;
	private int _receivedAudioChannels;

	private bool _hasAudioSource;
	private AudioSourceBridge _audioSourceBridge;
	private bool _usingVirtualSpeakers = false;
	
	private List<VirtualSpeakers> _virtualSpeakers = new List<VirtualSpeakers>();
	private float[] _channelVisualisations;

	private int _virtualSpeakersCount = 0;
	private bool _settingsChanged = false;
	
	public void CheckPassthroughAudioSource()
	{
		if (Application.isPlaying == false) return;
		DestroyAudioSourceBridge();
		if (_usingVirtualSpeakers) return;


		if (!_audioSource)
		{
			// create a fallback AudioSource for passthrough of matching channel counts
			var newSource = new GameObject("Passthrough Audio Source", typeof(AudioSource)).GetComponent<AudioSource>();
			newSource.dopplerLevel = 0;
			newSource.spatialBlend = 0;
			newSource.bypassListenerEffects = true;
			newSource.transform.SetParent(transform, false);
			newSource.hideFlags = HideFlags.DontSave;
			_hasAudioSource = true;
			_audioSource = newSource;
		}
		
		// Make sure it is playing so OnAudioFilterRead gets called by Unity
		_audioSource.Play();

		if (_audioSource.gameObject == gameObject) return;
		if (_receivedAudioChannels == -1) return;
		
		// Create a bridge component if the AudioSource is not on this GameObject so we can feed audio samples to it.
		_audioSourceBridge = _audioSource.GetComponent<AudioSourceBridge>();
		if (!_audioSourceBridge)
			_audioSourceBridge = _audioSource.gameObject.AddComponent<AudioSourceBridge>();
		
		_audioSourceBridge.Init(false, _systemAvailableAudioChannels);
		_audioSourceBridge._handler = this;
	}

	private void DestroyAudioSourceBridge()
	{
		if (_audioSourceBridge == null)
			return;

		_audioSourceBridge._handler = null;

		if(_audioSourceBridge._isDestroyed == false)
			GameObject.DestroyImmediate(_audioSourceBridge);

		_audioSourceBridge = null;
	}

	private void AudioSettings_OnAudioConfigurationChanged(bool deviceWasChanged)
	{
		UpdateAudioExpectations();
	}

	private void UpdateAudioExpectations()
	{
		_expectedAudioSampleRate = AudioSettings.outputSampleRate;
		_systemAvailableAudioChannels = Util.AudioChannels(AudioSettings.driverCapabilities);
	}

	// Automagically called by Unity when an AudioSource component is present on the same GameObject
	void OnAudioFilterRead(float[] data, int channels)
	{
		if ((object)_audioSource == null)
			return;

		if ((object)_audioSourceBridge != null)
			return;

		if (channels != _receivedAudioChannels)
			return;
		
		if (!HandleAudioFilterRead(data, channels))
			Array.Fill(data, 0f);
	}

	internal void HandleAudioSourceBridgeOnDestroy()
	{
		_audioSource = null;

		DestroyAudioSourceBridge();
	}
	
	internal float[] GetChannelVisualisations()
	{
		lock (audioBufferLock)
		{
			return _channelVisualisations;
		}
	}
	
	internal bool HandleAudioFilterRead(float[] data, int channels)
	{
		//Debug.Log(" READ DATA: "+data.Length + " "+AudioSettings.dspTime);
		int length = data.Length;

		// STE: Waiting for enough read ahead buffer frames?
		if (m_bWaitForBufferFill)
		{
			// Are we good yet?
			// Should we be protecting audioBuffer.Size here?
			m_bWaitForBufferFill = ( audioBuffer.Size < (length * m_iMinBufferAheadFrames) );

			// Early out if not enough in the buffer still
			if (m_bWaitForBufferFill)
			{
				return false;
			}
		}

		bool bPreviousWaitForBufferFill = m_bWaitForBufferFill;
		int iAudioBufferSize = 0;
		int bufferCapacity = 0;

		// STE: Lock buffer for the smallest amount of time
		lock (audioBufferLock)
		{
			iAudioBufferSize = audioBuffer.Size;
			bufferCapacity = audioBuffer.Capacity;

			// If we do not have enough data for a single frame then we will want to buffer up some read-ahead audio data. This will cause a longer gap in the audio playback, but this is better than more intermittent glitches I think
			m_bWaitForBufferFill = (iAudioBufferSize < length);
			if( !m_bWaitForBufferFill )
			{
				audioBuffer.Front( ref data, length );
				audioBuffer.PopFront( length );

				Util.UpdateVUMeter(ref _channelVisualisations, data, channels);
			}
		}

		if ( m_bWaitForBufferFill && !bPreviousWaitForBufferFill )
		{
			Debug.LogWarning($"Audio buffer underrun: OnAudioFilterRead: data.Length = {data.Length} | audioBuffer.Size = {iAudioBufferSize} | audioBuffer.Capacity = {bufferCapacity}", this);
			return false;
		}

		return true;
	}
	
	void DestroyAllVirtualSpeakers()
	{
		_usingVirtualSpeakers = false;
		while (_virtualSpeakers.Count > 0)
		{
			_virtualSpeakers[0].DestroyAudioSourceBridge();
			Destroy(_virtualSpeakers[0].speakerAudio.gameObject);
			_virtualSpeakers.RemoveAt(0);
		}
	}

	void CreateVirtualSpeakerCircle(int channelCount)
	{
		float dist = virtualSpeakerDistances;
		for (int i = 0; i < channelCount; i++)
		{
			var speaker = new VirtualSpeakers();

			float angle = i * Mathf.PI * 2 / channelCount;
			float x = Mathf.Cos(angle) * dist;
			float z = Mathf.Sin(angle) * dist;
			
			speaker.CreateGameObjectWithAudioSource(transform, new Vector3(x, 0, z));
			speaker.CreateAudioSourceBridge(this, i, channelCount, _systemAvailableAudioChannels);
			_virtualSpeakers.Add(speaker);		
		}
	}
	
	void CreateVirtualSpeakersQuad()
	{
		float dist = virtualSpeakerDistances;
		for (int i = 0; i < 4; i++)
		{
			var speaker = new VirtualSpeakers();

			Vector3 position = Vector3.zero;
			switch (i)
			{
				case 0 : position = new Vector3(-dist, 0, dist); break;
				case 1 : position = new Vector3(dist, 0, dist); break;
				case 4 : position = new Vector3(-dist, 0, -dist); break;
				case 5 : position = new Vector3(dist, 0, -dist); break;
			}
			
			speaker.CreateGameObjectWithAudioSource(transform, position);
			speaker.CreateAudioSourceBridge(this, i, 4, _systemAvailableAudioChannels);
			_virtualSpeakers.Add(speaker);
		}		
	}	

	void CreateVirtualSpeakers5point1()
	{
		float dist = virtualSpeakerDistances;
		for (int i = 0; i < 6; i++)
		{
			var speaker = new VirtualSpeakers();

			Vector3 position = Vector3.zero;
			switch (i)
			{
				case 0 : position = new Vector3(-dist, 0, dist); break;
				case 1 : position = new Vector3(dist, 0, dist); break;
				case 2 : position = new Vector3(0, 0, dist); break;
				case 3 : position = new Vector3(0, 0, 0); break;
				case 4 : position = new Vector3(-dist, 0, -dist); break;
				case 5 : position = new Vector3(dist, 0, -dist); break;
			}
			
			speaker.CreateGameObjectWithAudioSource(transform, position);
			speaker.CreateAudioSourceBridge(this, i, 6, _systemAvailableAudioChannels);
			_virtualSpeakers.Add(speaker);
		}		
	}

	void CreateVirtualSpeakers7point1()
	{
		float dist = virtualSpeakerDistances;
		for (int i = 0; i < 8; i++)
		{
			var speaker = new VirtualSpeakers();

			Vector3 position = Vector3.zero;
			switch (i)
			{
				case 0 : position = new Vector3(-dist, 0, dist); break;
				case 1 : position = new Vector3(dist, 0, dist); break;
				case 2 : position = new Vector3(0, 0, dist); break;
				case 3 : position = new Vector3(0, 0, 0); break;
				case 4 : position = new Vector3(-dist, 0, 0); break;
				case 5 : position = new Vector3(dist, 0, 0); break;
				case 6 : position = new Vector3(-dist, 0, -dist); break;
				case 7 : position = new Vector3(dist, 0, -dist); break;
			}
			
			speaker.CreateGameObjectWithAudioSource(transform, position);
			speaker.CreateAudioSourceBridge(this, i, 8, _systemAvailableAudioChannels);
			_virtualSpeakers.Add(speaker);
		}
	}

	void ResetAudioSpeakerSetup()
	{
		DestroyAudioSourceBridge();
		DestroyAllVirtualSpeakers();
		var audioConfiguration = AudioSettings.GetConfiguration();
		if (_systemAvailableAudioChannels == 2 && _receivedAudioChannels == 2)
		{
			Debug.Log("Setting Speaker Mode to Stereo");
			audioConfiguration.speakerMode = AudioSpeakerMode.Stereo;
			AudioSettings.Reset(audioConfiguration);
			CheckPassthroughAudioSource();
			return;
		}
		if (_systemAvailableAudioChannels == 4 && _receivedAudioChannels == 4)
		{
			Debug.Log("Setting Speaker Mode to Quad");
			audioConfiguration.speakerMode = AudioSpeakerMode.Quad;
			AudioSettings.Reset(audioConfiguration);
			CheckPassthroughAudioSource();
			return;
		}
		if (_systemAvailableAudioChannels == 6 && _receivedAudioChannels == 4)
		{
			Debug.Log("Setting Speaker Mode to 5.1");
			audioConfiguration.speakerMode = AudioSpeakerMode.Mode5point1;
			AudioSettings.Reset(audioConfiguration);
			CheckPassthroughAudioSource();
			return;
		}
		if (_systemAvailableAudioChannels == 8 && _receivedAudioChannels == 4)
		{
			Debug.Log("Setting Speaker Mode to 7.1");
			audioConfiguration.speakerMode = AudioSpeakerMode.Mode7point1;
			AudioSettings.Reset(audioConfiguration);
			CheckPassthroughAudioSource();
			return;
		}

		if (!_createVirtualSpeakers && _systemAvailableAudioChannels < _receivedAudioChannels)
			Debug.Log("Received more audio channels than supported with the current audio device. Virtual Speakers will be created.");
		
		Debug.Log("Try setting Speaker Mode to Virtual Speakers. Received channel count: " + _receivedAudioChannels + ". System available channel count: " + _systemAvailableAudioChannels);

		CreateVirtualSpeakers(_receivedAudioChannels);
	}
	
	void CreateVirtualSpeakers(int channelNo)
	{
		DestroyAllVirtualSpeakers();

		_usingVirtualSpeakers = true;
		
		if (channelNo == 4)
			CreateVirtualSpeakersQuad();
		else if (channelNo == 6)
			CreateVirtualSpeakers5point1();
		else if (channelNo == 8)
			CreateVirtualSpeakers7point1();
		else
		{
			Debug.LogWarning($"No configuration found for {channelNo} channels. Creating virtual speaker circle arrangement.", this);
			CreateVirtualSpeakerCircle(channelNo);
		}

		_virtualSpeakersCount = _virtualSpeakers.Count;
	}
	
	void ReadAudioMetaData(string metadata)
	{
		/*
		var xmlMeta = new XmlDocument();
		xmlMeta.LoadXml(metadata);
		
		DestroyAllVirtualSpeakers();
		var xmlSpeakers = xmlMeta.GetElementById("VirtualSpeakers");
		if (xmlSpeakers != null)
		{
			foreach (XmlNode xmlSpeaker in xmlSpeakers.ChildNodes)
			{
				if (xmlSpeaker.Name == "Speaker")
				{
					var speaker = new VirtualSpeakers();
					speaker.speakerAudio = gameObject.AddComponent<AudioSource>();
					speaker.speakerAudio.spatialBlend = 1;
					speaker.speakerAudio.rolloffMode = AudioRolloffMode.Linear;
					speaker.speakerAudio.minDistance = 0.1f;
					speaker.speakerAudio.maxDistance = 1000f;
					speaker.speakerAudio.loop = true;
					speaker.speakerAudio.playOnAwake = true;
					speaker.speakerAudio.volume = 1;
					speaker.speakerAudio.mute = false;
					speaker.speakerAudio.bypassEffects = false;
					speaker.speakerAudio.bypassListenerEffects = false;
					speaker.speakerAudio.bypassReverbZones = false;
					speaker.speakerAudio.priority = 128;
					speaker.speakerAudio.outputAudioMixerGroup = null;
					speaker.speakerAudio.clip = null;
					speaker.speakerAudio.name = xmlSpeaker.Attributes["Name"].Value;
					speaker.relativePosition = new Vector3(
						float.Parse(xmlSpeaker.Attributes["X"].Value),
						float.Parse(xmlSpeaker.Attributes["Y"].Value),
						float.Parse(xmlSpeaker.Attributes["Z"].Value)
					);
					_virtualSpeakers.Add(speaker);
				}
			}
		}
		*/
	}

	void FillAudioBuffer(Interop.AudioFrame audio)
	{
		if (_recv == null)
		{
			return;
		}
		
		bool settingsChanged = false;
		if (audio.SampleRate != _receivedAudioSampleRate)
		{
			settingsChanged = true;
			_receivedAudioSampleRate = audio.SampleRate;
			if (_receivedAudioSampleRate != _expectedAudioSampleRate)
				Debug.LogWarning($"Audio sample rate does not match. Expected {_expectedAudioSampleRate} but received {_receivedAudioSampleRate}.", this);
		}

		if((_usingVirtualSpeakers && audio.NoChannels != _virtualSpeakersCount) || _receivedAudioChannels != audio.NoChannels)
		{
			settingsChanged = true;
			_receivedAudioChannels = audio.NoChannels;
			if(_receivedAudioChannels != _systemAvailableAudioChannels)
				Debug.LogWarning($"Audio channel count does not match. Expected {_systemAvailableAudioChannels} but received {_receivedAudioChannels}.", this);
		}

		if (audio.Metadata != null)
			Debug.Log(audio.Metadata);

		if (settingsChanged)
		{
			_settingsChanged = true; 
		}

		int totalSamples = 0;

		// If the received data's format is as expected we can convert from interleaved to planar and just memcpy
		if (_receivedAudioSampleRate == _expectedAudioSampleRate) 
		{
			// Converted from NDI C# Managed sample code
			// we're working in bytes, so take the size of a 32 bit sample (float) into account
			int sizeInBytes = audio.NoSamples * audio.NoChannels * sizeof(float);

			// Unity is expecting interleaved audio and NDI uses planar.
			// create an interleaved frame and convert from the one we received
			interleavedAudio.SampleRate = audio.SampleRate;
			interleavedAudio.NoChannels = audio.NoChannels;
			interleavedAudio.NoSamples = audio.NoSamples;
			interleavedAudio.Timecode = audio.Timecode;

			// allocate native array to copy interleaved data into
			unsafe
			{
				if (!m_aTempAudioPullBuffer.IsCreated || m_aTempAudioPullBuffer.Length < sizeInBytes)
				{
					if (m_aTempAudioPullBuffer.IsCreated)
						m_aTempAudioPullBuffer.Dispose();

					m_aTempAudioPullBuffer = new NativeArray<byte>(sizeInBytes, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
				}

				interleavedAudio.Data = (IntPtr)m_aTempAudioPullBuffer.GetUnsafePtr();
				if (interleavedAudio.Data != null)
				{
					// Convert from float planar to float interleaved audio
					_recv.AudioFrameToInterleaved(ref audio, ref interleavedAudio);
					int channels = _usingVirtualSpeakers ? _virtualSpeakersCount : _systemAvailableAudioChannels;

					totalSamples = interleavedAudio.NoSamples * channels;
					void* audioDataPtr = interleavedAudio.Data.ToPointer();

					if (audioDataPtr != null)
					{
						if (m_aTempSamplesArray.Length < totalSamples)
						{
							m_aTempSamplesArray = new float[totalSamples];
						}

						// Grab data from native array
						var tempSamplesPtr = UnsafeUtility.PinGCArrayAndGetDataAddress(m_aTempSamplesArray, out ulong tempSamplesHandle);
						UnsafeUtility.MemCpy(tempSamplesPtr, audioDataPtr, totalSamples * sizeof(float));
						UnsafeUtility.ReleaseGCObject(tempSamplesHandle);
					}
				}
			}
		}
		// If we need to resample or remap channels we can just work with the interleaved data as is
		else
		{
			unsafe
			{
				void* audioDataPtr = audio.Data.ToPointer();

				var resamplingRate = (float)_receivedAudioSampleRate / _expectedAudioSampleRate;
				var needsToResample = resamplingRate != 1;
				var neededSamples = needsToResample ? (int)(audio.NoSamples / resamplingRate) : audio.NoSamples;

				int channels = _usingVirtualSpeakers ? _virtualSpeakersCount : _systemAvailableAudioChannels;
				totalSamples = neededSamples * channels;

				for (int i = 0; i < neededSamples; i++)
				{
					for (int j = 0; j < channels; j++)
						m_aTempSamplesArray[i * channels + j] = ReadAudioDataSampleInterleaved(audio, audioDataPtr, i, j, resamplingRate);
				}
				
			}
		}
		//Debug.Log("RECV AUDIO FRAME: "+totalSamples+ " "+AudioSettings.dspTime);

		// Copy new sample data into the circular array
		lock (audioBufferLock)
		{
			if (audioBuffer.Capacity < totalSamples * m_iMinBufferAheadFrames)
			{
				audioBuffer = new CircularBuffer<float>(totalSamples * m_iMinBufferAheadFrames);
			}

			audioBuffer.PushBack(m_aTempSamplesArray, totalSamples);
		}
	}

	private unsafe float ReadAudioDataSampleInterleaved(Interop.AudioFrame audio, void* audioDataPtr, int sampleIndex, int channelIndex, float resamplingRate)
	{
		if (resamplingRate == 1)
			return UnsafeUtility.ReadArrayElement<float>(audioDataPtr, sampleIndex + channelIndex * audio.NoSamples);

		var resamplingIndex = (int)(sampleIndex * resamplingRate);
		var t = (sampleIndex * resamplingRate) - resamplingIndex;

		var lowerSample = UnsafeUtility.ReadArrayElement<float>(audioDataPtr, resamplingIndex + channelIndex * audio.NoSamples);

		if (Mathf.Approximately(t, 0))
			return lowerSample;

		var upperSample = UnsafeUtility.ReadArrayElement<float>(audioDataPtr, (resamplingIndex + 1) + channelIndex * audio.NoSamples);

		return Mathf.Lerp(lowerSample, upperSample, t);
	}

	//private unsafe float ReadAudioDataSamplePlanar(void* audioDataPtr, int sampleIndex, int channelIndex, float resamplingRate)
	//{
	//	if (resamplingRate == 1)
	//		return UnsafeUtility.ReadArrayElement<float>(audioDataPtr, sampleIndex * interleavedAudio.NoChannels + channelIndex);

	//	var resamplingIndex = (int)(sampleIndex * resamplingRate);
	//	var t = (sampleIndex * resamplingRate) - resamplingIndex;

	//	var lowerSample = UnsafeUtility.ReadArrayElement<float>(audioDataPtr, resamplingIndex * interleavedAudio.NoChannels + channelIndex);

	//	if (Mathf.Approximately(t, 0))
	//		return lowerSample;

	//	var upperSample = UnsafeUtility.ReadArrayElement<float>(audioDataPtr, (resamplingIndex + 1) * interleavedAudio.NoChannels + channelIndex);

	//	return Mathf.Lerp(lowerSample, upperSample, t);
	//}

	#endregion

}

} // namespace Klak.Ndi
