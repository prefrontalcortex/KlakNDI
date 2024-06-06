using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CircularBuffer;
using Klak.Ndi.Audio;
using Klak.Ndi.Interop;
#if OSC_JACK
using OscJack;
#endif
using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;
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
#if OSC_JACK
		if (_sendAdmOsc)
		{
			_admOscSender = new AdmOscSender(_oscConnection);
			_admOscSender.SetSettings(_admSettings);
		}
#endif

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
		if (_updateAudioMetaSpeakerSetup || _receivingObjectBasedAudio)
			CreateOrUpdateSpeakerSetupByAudioMeta();
	}

	void OnDestroy()
	{
		while (_audioFramesPool.Count > 0)
			_audioFramesPool.Dequeue().Dispose();
		while (_audioFramesBuffer.Count > 0)
		{
			_audioFramesBuffer[0].Dispose();
			_audioFramesBuffer.RemoveAt(0);
		}
		
#if OSC_JACK
		if (_admOscSender != null)
			_admOscSender.Dispose();
#endif		
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
	
	private readonly List<VirtualSpeakers> _virtualSpeakers = new List<VirtualSpeakers>();
	private readonly List<VirtualSpeakers> _parkedVirtualSpeakers = new List<VirtualSpeakers>();
	private float[] _channelVisualisations;
	private readonly List<AudioFrameData> _audioFramesBuffer = new List<AudioFrameData>();
	private readonly List<AudioFrameData> _newAudioFramesBuffer = new List<AudioFrameData>();
	private readonly Queue<AudioFrameData> _audioFramesPool = new Queue<AudioFrameData>();
	
	private int _virtualSpeakersCount = 0;
	private bool _settingsChanged = false;
	private object _audioMetaLock = new object();
	private bool _updateAudioMetaSpeakerSetup = false;
	private Vector3[] _receivedSpeakerPositions;
	private bool _receivingObjectBasedAudio = false;
	
#if OSC_JACK
	private AdmOscSender _admOscSender;
	
	public void SetupAndStartOsc(string host, int port = 9000)
	{
		OscConnection connection = new OscConnection();
		connection.host = host;
		connection.port = 9000;
		_oscConnection = connection;
		if (_admOscSender != null)
			_admOscSender.Dispose();
		_admOscSender = new AdmOscSender(_oscConnection);
	}
#endif

	
	public Vector3[] GetReceivedSpeakerPositions()
	{
		lock (_audioMetaLock)
			return _receivedSpeakerPositions;
	}
	
	public Vector3[] GetCurrentSpeakerPositions()
	{
		if (_usingVirtualSpeakers)
		{
			var positions = new Vector3[_virtualSpeakers.Count];
			for (int i = 0; i < _virtualSpeakers.Count; i++)
				positions[i] = _virtualSpeakers[i].speakerAudio.transform.position;
			return positions;
		}
		return null;
	}
	
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
	
	public float[] GetChannelVisualisations()
	{
		lock (audioBufferLock)
		{
			return _channelVisualisations;
		}
	}
	
	ProfilerMarker PULL_NEXT_AUDIO_FRAME_MARKER = new ProfilerMarker("NdiReceiver.PullNextAudioFrame");
	ProfilerMarker FILL_AUDIO_CHANNEL_DATA_MARKER = new ProfilerMarker("NdiReceiver.FillAudioChannelData");
	ProfilerMarker ADD_AUDIO_FRAME_TO_QUEUE_MARKER = new ProfilerMarker("NdiReceiver.AddAudioFrameToQueue");
	
	private AudioFrameData AddAudioFrameToQueue(AudioFrame audioFrame)
	{
		using (ADD_AUDIO_FRAME_TO_QUEUE_MARKER.Auto())
		{
			lock (audioBufferLock)
			{
				AudioFrameData frame;
				if (_audioFramesPool.Count == 0)
					frame = new AudioFrameData();
				else
					frame = _audioFramesPool.Dequeue();
				frame.Set(audioFrame);

				_newAudioFramesBuffer.Add(frame);

				while (_newAudioFramesBuffer.Count > m_iMinBufferAheadFrames)
				{
					var f = _newAudioFramesBuffer[0];
					_newAudioFramesBuffer.RemoveAt(0);
					_audioFramesPool.Enqueue(f);
				}

				return frame;
			}
		}
	}
	
	internal bool FillAudioChannelData(ref float[] data, int channelNo, int channelCountInData, bool dataContainsSpatialData = false)
	{
		using (FILL_AUDIO_CHANNEL_DATA_MARKER.Auto())
		{
			lock (audioBufferLock)
			{
				if (_audioFramesBuffer.Count == 0)
					return false;

				int frameSize = data.Length / channelCountInData;

				int frameIndex = 0;
				int samplesCopied = 0;
				int maxChannels = _audioFramesBuffer.Max(f => f.noChannels);
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

					var destPtr = (float*)dataPtr;

					do
					{
						AudioFrameData _currentAudioFrame;
						if (frameIndex >= _audioFramesBuffer.Count)
						{
							for (int i = 0; i < frameIndex; i++)
							{
								_audioFramesBuffer[i].channelSamplesReaded[channelNo] = 0;
							}

							return false;
						}

						_currentAudioFrame = _audioFramesBuffer[frameIndex];

						if (channelNo >= _currentAudioFrame.noChannels)
						{
							for (int i = samplesCopied; i < data.Length; i++)
							{
								data[i] = 0f;
							}

							break;
						}

						var channelData = _currentAudioFrame.GetChannelArray(channelNo, frameSize);

						int samplesToCopy = Mathf.Min(frameSize, channelData.Length);
						_currentAudioFrame.channelSamplesReaded[channelNo] += samplesToCopy;


						var channelDataPtr = (float*)channelData.GetUnsafePtr();
						var audioFrameSamplesReaded = _audioFramesBuffer[frameIndex].channelSamplesReaded[channelNo];

						if (dataContainsSpatialData)
							VirtualAudio.BurstMethods.UpMixMonoWithDestination(channelDataPtr, audioFrameSamplesReaded,
								destPtr, samplesCopied, channelCountInData, samplesToCopy);
						else
							VirtualAudio.BurstMethods.UpMixMono(channelDataPtr, audioFrameSamplesReaded, destPtr,
								samplesCopied, channelCountInData, samplesToCopy);


						samplesCopied += samplesToCopy;
						frameIndex++;
					} while (samplesCopied < frameSize);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
					AtomicSafetyHandle.Release(safety);
#endif
					UnsafeUtility.ReleaseGCObject(handle);
				}

				Util.UpdateVUMeterSingleChannel(ref _channelVisualisations, data, maxChannels, channelNo);
			}

			return true;
		}
	}

	internal bool PullNextAudioFrame(int frameSize, int channels)
	{
		using (PULL_NEXT_AUDIO_FRAME_MARKER.Auto())
		{

			lock (audioBufferLock)
			{
				for (int i = 0; i < _newAudioFramesBuffer.Count; i++)
					_audioFramesBuffer.Add(_newAudioFramesBuffer[i]);
				_newAudioFramesBuffer.Clear();

				if (_audioFramesBuffer.Count > m_iMinBufferAheadFrames)
				{
					while (_audioFramesBuffer.Count > m_iMinBufferAheadFrames)
					{
						var f = _audioFramesBuffer[0];
						_audioFramesBuffer.RemoveAt(0);
						_audioFramesPool.Enqueue(f);
					}
				}

				do
				{
					if (_audioFramesBuffer.Count == 0)
					{
						if (_channelVisualisations == null || _channelVisualisations.Length != channels)
                            _channelVisualisations = new float[channels];
						Array.Fill(_channelVisualisations, 0f);
						lock (_audioMetaLock)
						{
							if (_receivedSpeakerPositions == null || _receivedSpeakerPositions.Length != 0)
                                _receivedSpeakerPositions = new Vector3[channels];
							Array.Fill(_receivedSpeakerPositions, Vector3.zero);
						}

						return false;
					}

					var nextFrame = _audioFramesBuffer[0];
					if (nextFrame.channelSamplesReaded.Sum() >= nextFrame.noChannels * nextFrame.samplesPerChannel)
					{
						_audioFramesPool.Enqueue(nextFrame);
						_audioFramesBuffer.RemoveAt(0);
					}
					else
					{
						break;
					}

				} while (true);

				int availableSamples = 0;
				for (int i = 0; i < _audioFramesBuffer.Count; i++)
				{
					availableSamples += _audioFramesBuffer[i].samplesPerChannel;
				}

				if (availableSamples < frameSize)
					return false;

				if (_audioFramesBuffer.Count > 0)
				{
					lock (_audioMetaLock)
					{
						
#if OSC_JACK
						if (_admOscSender != null)
							_admOscSender.SendMeta(_audioFramesBuffer[0].speakerPositions);
#endif
	
						_receivingObjectBasedAudio = _audioFramesBuffer[0].isObjectBased;
						_updateAudioMetaSpeakerSetup = true;
						if (_receivedSpeakerPositions == null || _receivedSpeakerPositions.Length !=
						    _audioFramesBuffer[0].speakerPositions.Length)
							_receivedSpeakerPositions = _audioFramesBuffer[0].speakerPositions;
						else
							Array.Copy(_audioFramesBuffer[0].speakerPositions, _receivedSpeakerPositions,
								_receivedSpeakerPositions.Length);
					}
				}

				return true;
			}
		}
	}
	
	internal bool HandleAudioFilterRead(float[] data, int channels)
	{
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
	
	#region Virtual Speakers

	void ParkAllVirtualSpeakers()
	{
		for (int i = 0; i < _virtualSpeakers.Count(); i++)
		{
			_virtualSpeakers[i].speakerAudio.gameObject.SetActive(false);
		}
		_parkedVirtualSpeakers.AddRange(_virtualSpeakers);
		_virtualSpeakers.Clear();
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

	void CreateOrUpdateSpeakerSetupByAudioMeta()
	{
		DestroyAudioSourceBridge();
		var speakerPositions = GetReceivedSpeakerPositions();
		
		if (speakerPositions == null || speakerPositions.Length == 0)
		{
			Debug.LogWarning("No speaker positions found in audio metadata.", this);
			return;
		}

		_usingVirtualSpeakers = true;

		if (speakerPositions.Length == _virtualSpeakers.Count)
		{
			// Just Update Positions
			for (int i = 0; i < speakerPositions.Length; i++)
			{
				if (_receivingObjectBasedAudio)
				{
					var tr = _virtualSpeakers[i].speakerAudio.transform;
					// TODO: figure out how to best lerp the position 
					tr.position = speakerPositions[i];// Vector3.Lerp(tr.position, speakerPositions[i], Time.deltaTime * 5f);
				}
				else
					_virtualSpeakers[i].speakerAudio.transform.position = speakerPositions[i];
			}
		}
		else
		{
			ParkAllVirtualSpeakers();
			
			for (int i = 0; i < speakerPositions.Length; i++)
			{
				if (_parkedVirtualSpeakers.Count > 0)
				{
					var vs = _parkedVirtualSpeakers[0];
					_parkedVirtualSpeakers.RemoveAt(0);
					vs.speakerAudio.transform.position = speakerPositions[i];
					_virtualSpeakers.Add(vs);
					vs.UpdateParameters(i, speakerPositions.Length, _systemAvailableAudioChannels, _receivingObjectBasedAudio);

				}
				else
				{
					var speaker = new VirtualSpeakers();
					speaker.CreateGameObjectWithAudioSource(transform, speakerPositions[i], _receivingObjectBasedAudio);
					speaker.CreateAudioSourceBridge(this, i, speakerPositions.Length, _systemAvailableAudioChannels);
					_virtualSpeakers.Add(speaker);
				}
			}
		}
	}

	void ResetAudioSpeakerSetup()
	{
		DestroyAudioSourceBridge();
		ParkAllVirtualSpeakers();
		var audioConfiguration = AudioSettings.GetConfiguration();
		if (!_receivingObjectBasedAudio)
		{
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
		}

		if (!_receivingObjectBasedAudio && !_createVirtualSpeakers && _systemAvailableAudioChannels < _receivedAudioChannels)
			Debug.Log("Received more audio channels than supported with the current audio device. Virtual Speakers will be created.");
		
		Debug.Log("Try setting Speaker Mode to Virtual Speakers. Received channel count: " + _receivedAudioChannels + ". System available channel count: " + _systemAvailableAudioChannels);

		CreateVirtualSpeakers(_receivedAudioChannels);
	}
	
	void CreateVirtualSpeakers(int channelNo)
	{
		if (_receivingObjectBasedAudio)
		{
			_usingVirtualSpeakers = true;
			var metaSpeakerSetup = GetReceivedSpeakerPositions();
			if (metaSpeakerSetup != null && metaSpeakerSetup.Length > 0)
			{
				Debug.Log("Received speaker positions from audio metadata. Creating speaker setup.");
				CreateOrUpdateSpeakerSetupByAudioMeta();
				_virtualSpeakersCount = _virtualSpeakers.Count;
				return;
			}

			DestroyAllVirtualSpeakers();
			_virtualSpeakersCount = _virtualSpeakers.Count;

			return;
		}
		
		DestroyAllVirtualSpeakers();

		_usingVirtualSpeakers = true;
		_virtualSpeakersCount = _virtualSpeakers.Count;
		if (channelNo == 4)
			CreateVirtualSpeakersQuad();
		else if (channelNo == 6)
			CreateVirtualSpeakers5point1();
		else if (channelNo == 8)
			CreateVirtualSpeakers7point1();
		else
		{
			var metaSpeakerSetup = GetReceivedSpeakerPositions();
			if (metaSpeakerSetup != null && metaSpeakerSetup.Length > 0)
			{
				Debug.Log("Received speaker positions from audio metadata. Creating speaker setup.");
				CreateOrUpdateSpeakerSetupByAudioMeta();
			}
			else
			{
				Debug.LogWarning($"No configuration found for {channelNo} channels. Creating virtual speaker circle arrangement.", this);
				CreateVirtualSpeakerCircle(channelNo);
			}
		}

		_virtualSpeakersCount = _virtualSpeakers.Count;
	}
	#endregion
	
	void ReadAudioMetaData(string metadata)
	{
		var speakerSetup = AudioMeta.GetSpeakerConfigFromXml(metadata, out _receivingObjectBasedAudio);
		if (speakerSetup != null && speakerSetup.Length >= 0)
		{
			_updateAudioMetaSpeakerSetup = true;
			lock (_audioMetaLock)
			{
				if (_receivedSpeakerPositions == null)
					_receivedSpeakerPositions = speakerSetup;
			}
		}
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
		{
			ReadAudioMetaData(audio.Metadata);
		}

		if (settingsChanged)
		{
			_settingsChanged = true; 
		}

		AddAudioFrameToQueue(audio);
		return;

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
