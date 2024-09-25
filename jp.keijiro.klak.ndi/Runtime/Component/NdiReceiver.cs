using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Klak.Ndi.Audio;
using Klak.Ndi.Interop;
#if OSC_JACK
using OscJack;
#endif
using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;

namespace Klak.Ndi {

// FIXME: re-enable the execute in edit mode (with on/off/mute toggle?)
//[ExecuteInEditMode]
public sealed partial class NdiReceiver : MonoBehaviour
{
    #region Receiver objects

	Interop.Recv _recv;
	FormatConverter _converter;
	MaterialPropertyBlock _override;
	Interop.Bandwidth _lastBandwidth;
	private ReceiverPerformance _performance;
	private ReceiverPerformance _dropped;
	private ReceiverQueue _queue;
	
    void PrepareReceiverObjects()
    {
	    if (_lastBandwidth != _bandwidth)
	    {
		    _lastBandwidth = _bandwidth;
		    _recv?.Dispose();
		    _recv = null;
	    }
	    
        if (_recv == null) _recv = RecvHelper.TryCreateRecv(ndiName, _bandwidth);
        if (_converter == null) _converter = new FormatConverter(_resources);
        if (_override == null) _override = new MaterialPropertyBlock();
    }

    void ReleaseReceiverObjects()
    {
        _recv?.Dispose();
        _recv = null;

        _converter?.Dispose();
        _converter = null;
	}

	#endregion

	internal void Restart()
	{
		ResetAudioBuffer();
		ReleaseReceiverObjects();
		ResetBufferStatistics();
	}

	void Awake()
	{
		ndiName = _ndiName;

		mainThreadContext = SynchronizationContext.Current;

		if (_override == null) _override = new MaterialPropertyBlock();

		tokenSource = new CancellationTokenSource();
		cancellationToken = tokenSource.Token;
		
		Task.Run(ReceiveAudioFrameTask, cancellationToken);

		UpdateAudioExpectations();
		AudioSettings.OnAudioConfigurationChanged += AudioSettings_OnAudioConfigurationChanged;
		CheckPassthroughAudioSource();
	}

	private void Update()
	{
		if (_recv != null)
		{
			_recv.GetPerformance(ref _performance, ref _dropped);
			_recv.GetQueue(ref _queue);
		}
		
		if (_settingsChanged)
		{
			//ReadAudioMetaData(audio.Metadata);
			ResetAudioSpeakerSetup();
			_settingsChanged = false;
		}
		if (_updateAudioMetaSpeakerSetup || _receivingObjectBasedAudio)
			CreateOrUpdateSpeakerSetupByAudioMeta();
		
		ReceiveFrameTask();
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

    private object _bufferStatisticsLock = new object();

    public struct BufferStatistics
    {
	    public double lastReceivedVideoFrameTime;
	    public double lastReceivedAudioFrameTime;
	    public float audioBufferTimeLength;
	    public int audioBufferUnderrun;
	    public int discardedAudioFrames;
	    public int waitingForBufferFillUp;
	    
	    public void Reset()
	    {
		    discardedAudioFrames = 0;
		    lastReceivedVideoFrameTime = 0;
		    lastReceivedAudioFrameTime = 0;
		    audioBufferTimeLength = 0;
		    audioBufferUnderrun = 0;
		    waitingForBufferFillUp = 0;
	    }
    }

    private BufferStatistics _bufferStatistics;

    public BufferStatistics GetBufferStatistics()
    {
	    lock (_bufferStatisticsLock)
		    return _bufferStatistics;
    }
    
    void ReceiveFrameTask()
	{
		try
		{
			PrepareReceiverObjects();

			if (_recv == null)
				return;

			Interop.VideoFrame video;
			Interop.MetadataFrame metadata;
			
			var type = _recv.CaptureVideoAndMeta(out video, out metadata, 0);
			switch (type)
			{
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
					lock (_bufferStatisticsLock)
						_bufferStatistics.lastReceivedVideoFrameTime = AudioSettings.dspTime;
					//Debug.Log($"received {type}: {video}");
					ProcessVideoFrame(video);
					break;
			}
		}
		catch (System.Exception e)
		{
			Debug.LogException(e);
		}
	}

    void ReceiveAudioFrameTask()
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

			    Interop.AudioFrame audio;
				
			    var hasAudio = _recv.CaptureAudio(out audio, 10);
			    if (hasAudio)
			    {
				    lock (_bufferStatisticsLock)
					    _bufferStatistics.lastReceivedAudioFrameTime = AudioSettings.dspTime;

				    if (_receiveAudio)
				    {
					    FillAudioBuffer(audio);
				    }
				    
				    _recv.FreeAudioFrame(audio);
				    //mainThreadContext.Post(ProcessAudioFrame, audio);
			    }
			
		    }
	    }
	    catch (System.Exception e)
	    {
		    Debug.LogException(e);
	    }
    }
    
    private int _lastFrameUpdate = -1;
    
	void ProcessVideoFrame(System.Object data)
	{
		Interop.VideoFrame videoFrame = (Interop.VideoFrame)data;
		
		if (_recv == null) return;

		if (_lastFrameUpdate == Time.frameCount)
		{
			_recv.FreeVideoFrame(videoFrame);
			return;
		}

		_lastFrameUpdate = Time.frameCount;
		
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
	private readonly object					channelVisualisationLock = new object();
	//
	private const int						_MaxBufferSampleSize = 48000 / 5;
	private const int						_MinBufferSampleSize = 48000 / 10;
	//
	
	private int _expectedAudioSampleRate;
	private int _systemAvailableAudioChannels;

	private int _receivedAudioSampleRate;
	private int _receivedAudioChannels;

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
	private bool _receivingAudioMetaSpeakerSetup = false;
	private Vector3[] _receivedSpeakerPositions;
	private bool _receivingObjectBasedAudio = false;
	public Action<AudioFrame> OnAudioFrameReceived;

	
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

	private void ResetAudioBuffer()
	{
		_initWaitForAudioBufferFillUp = true;
		lock (audioBufferLock)
		{
			while (_audioFramesBuffer.Count > 0)
			{
				_audioFramesPool.Enqueue(_audioFramesBuffer[0]);
				_audioFramesBuffer.RemoveAt(0);
			}
		}
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
		
		if (!FillPassthroughData(ref data, channels))
			Array.Fill(data, 0f);
	}

	internal void HandleAudioSourceBridgeOnDestroy()
	{
		_audioSource = null;

		DestroyAudioSourceBridge();
	}
	
	public float[] GetChannelVisualisations()
	{
		lock (channelVisualisationLock)
		{
			return _channelVisualisations;
		}
	}
	
	ProfilerMarker PULL_NEXT_AUDIO_FRAME_MARKER = new ProfilerMarker("NdiReceiver.PullNextAudioFrame");
	ProfilerMarker FILL_AUDIO_CHANNEL_DATA_MARKER = new ProfilerMarker("NdiReceiver.FillAudioChannelData");
	ProfilerMarker ADD_AUDIO_FRAME_TO_QUEUE_MARKER = new ProfilerMarker("NdiReceiver.AddAudioFrameToQueue");

	private void UpdateAudioStatistics(int addRemoveFrames)
	{
		lock (_bufferStatisticsLock)
		{
			_bufferStatistics.discardedAudioFrames += addRemoveFrames;
			_bufferStatistics.audioBufferTimeLength = (float)(_newAudioFramesBuffer.Sum((b) => b.samplesPerChannel) +
			                                                  _audioFramesBuffer.Sum((b) => b.samplesPerChannel)) /
			                                          _expectedAudioSampleRate;
		}	
	}
	
	void ResetBufferStatistics()
	{
		lock (_bufferStatisticsLock)
			_bufferStatistics.Reset();
	}
	
	private AudioFrameData AddAudioFrameToQueue(AudioFrame audioFrame)
	{
		if (audioFrame.NoSamples == 0)
			return null;
		
		using (ADD_AUDIO_FRAME_TO_QUEUE_MARKER.Auto())
		{
			AudioFrameData frame;
			lock (audioBufferLock)
			{
				if (_audioFramesPool.Count == 0)
					frame = new AudioFrameData();
				else
					frame = _audioFramesPool.Dequeue();
			}
			
			frame.Set(audioFrame, _expectedAudioSampleRate);
			
			int removedFrames = 0;
			lock (audioBufferLock)
			{
				_newAudioFramesBuffer.Add(frame);
				while ((_newAudioFramesBuffer.Count*audioFrame.NoSamples) > _MaxBufferSampleSize)
				{
					var f = _newAudioFramesBuffer[0];
					_newAudioFramesBuffer.RemoveAt(0);
					_audioFramesPool.Enqueue(f);
					removedFrames++;
				}

				UpdateAudioStatistics(removedFrames);
			}
			return frame;
		}
	}

	internal bool FillPassthroughData(ref float[] data, int channelCountInData)
	{
		if (!PullNextAudioFrame(data.Length / channelCountInData, channelCountInData))
		{
			Array.Fill(data, 0f);
			return false;
		}

		using (FILL_AUDIO_CHANNEL_DATA_MARKER.Auto())
		{
			lock (audioBufferLock)
			{
				if (_audioFramesBuffer.Count == 0)
					return false;

				int frameSize = data.Length / channelCountInData;

				int frameIndex = 0;
				int samplesCopied = 0;
				
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

					void Release()
					{
#if ENABLE_UNITY_COLLECTIONS_CHECKS
						AtomicSafetyHandle.Release(safety);
#endif
						UnsafeUtility.ReleaseGCObject(handle);
					}

					var destPtr = (float*)dataPtr;

					do
					{
						AudioFrameData _currentAudioFrame;
						if (frameIndex >= _audioFramesBuffer.Count)
						{
							for (int i = 0; i < frameIndex; i++)
							{
								for (int c = 0; c < _audioFramesBuffer[i].channelSamplesReaded.Length; c++)
									_audioFramesBuffer[i].channelSamplesReaded[c] = 0;
							}
							Release();
							lock (_bufferStatisticsLock)
							{
								_bufferStatistics.audioBufferUnderrun++;
							}
							return false;
						}

						_currentAudioFrame = _audioFramesBuffer[frameIndex];
			
						var audioFrameData = _currentAudioFrame.GetAllChannelsArray();
				
						var audioFrameSamplesReaded = _audioFramesBuffer[frameIndex].channelSamplesReaded[0];
						int samplesToCopy = Mathf.Min(frameSize - samplesCopied, _currentAudioFrame.samplesPerChannel - audioFrameSamplesReaded);

						for (int i = 0; i < _currentAudioFrame.noChannels; i++)
							_currentAudioFrame.channelSamplesReaded[i] += samplesToCopy;
						
						var channelDataPtr = (float*)audioFrameData.GetUnsafePtr();
						BurstMethods.PlanarToInterleaved(channelDataPtr, audioFrameSamplesReaded, _currentAudioFrame.noChannels,  destPtr, samplesCopied * channelCountInData, channelCountInData, samplesToCopy );

						samplesCopied += samplesToCopy;
						frameIndex++;
					} while (samplesCopied < frameSize);

					Release();
				}

				lock (channelVisualisationLock)
				{
					unsafe
					{
						if (_channelVisualisations == null || _channelVisualisations.Length != channelCountInData)
							_channelVisualisations = new float[channelCountInData];
						
						var dataPtr = UnsafeUtility.PinGCArrayAndGetDataAddress(data, out var handleData);
						var visPtr = UnsafeUtility.PinGCArrayAndGetDataAddress(_channelVisualisations, out var handleVis);
						
						BurstMethods.GetVUs((float*)dataPtr, data.Length, channelCountInData, (float*)visPtr);
						
						UnsafeUtility.ReleaseGCObject(handleData);
						UnsafeUtility.ReleaseGCObject(handleVis);
					}
				}
			}

			return true;
		}
	}
	
	internal bool FillAudioChannelData(ref float[] data, int channelNo, int channelCountInData, bool dataContainsSpatialData = false)
	{
		if (_initWaitForAudioBufferFillUp)
		{
			return false;
		}
		
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

					void Release()
					{
#if ENABLE_UNITY_COLLECTIONS_CHECKS
						AtomicSafetyHandle.Release(safety);
#endif
						UnsafeUtility.ReleaseGCObject(handle);
					}
					
					var destPtr = (float*)dataPtr;

					do
					{
						AudioFrameData _currentAudioFrame;
						if (frameIndex >= _audioFramesBuffer.Count)
						{
                            // not enough audio data in the buffers!
                            
                            // Mark all frames as readed
							for (int i = 0; i < _audioFramesBuffer.Count; i++)
								Array.Fill(_audioFramesBuffer[i].channelSamplesReaded, _audioFramesBuffer[i].samplesPerChannel);
							
							// Wait the next time that the buffer is filled up
							_initWaitForAudioBufferFillUp = true;
							Array.Fill(data, 0f);

							lock (_bufferStatisticsLock)
							{
								_bufferStatistics.audioBufferUnderrun++;
							}
							break;
						}

						_currentAudioFrame = _audioFramesBuffer[frameIndex];

						if (channelNo >= _currentAudioFrame.noChannels)
						{
							Array.Fill(data, 0f, samplesCopied, data.Length - samplesCopied);
							break;
						}

						var channelData = _currentAudioFrame.GetChannelArray(channelNo);

						var audioFrameSamplesReaded = _currentAudioFrame.channelSamplesReaded[channelNo];
						if (frameIndex == 0 && audioFrameSamplesReaded >= _currentAudioFrame.samplesPerChannel)
						{
							// For some reason PullAudioFrame was not called...so we break here 
							Array.Fill(data, 0f);
 							Release();
							return false;
						}
						
						int samplesToCopy = Mathf.Min(frameSize - samplesCopied, _currentAudioFrame.samplesPerChannel-audioFrameSamplesReaded);
						
						var channelDataPtr = (float*)channelData.GetUnsafePtr();

						if (dataContainsSpatialData)
							BurstMethods.UpMixMonoWithDestination(channelDataPtr, audioFrameSamplesReaded,
								destPtr, samplesCopied, channelCountInData, samplesToCopy);
						else
							BurstMethods.UpMixMono(channelDataPtr, audioFrameSamplesReaded, destPtr,
								samplesCopied, channelCountInData, samplesToCopy);

						_currentAudioFrame.channelSamplesReaded[channelNo] += samplesToCopy;

						samplesCopied += samplesToCopy;
						frameIndex++;
					} while (samplesCopied < frameSize);

					Release();
				}

				lock (channelVisualisationLock)
				{
					unsafe
					{
						if (_channelVisualisations == null || _channelVisualisations.Length != maxChannels)
							_channelVisualisations = new float[maxChannels];
						
						var dataPtr = UnsafeUtility.PinGCArrayAndGetDataAddress(data, out var handleData);
						
						BurstMethods.GetVU((float*)dataPtr, data.Length, out float vu);
						
						if (channelNo >= 0 && channelNo < maxChannels)
							_channelVisualisations[channelNo] = vu;
						UnsafeUtility.ReleaseGCObject(handleData);
					}
				}
			}

			return true;
		}
	}

	private bool _initWaitForAudioBufferFillUp = false;

	internal bool PullNextAudioFrame(int frameSize, int channels)
	{
		int removeCounter = 0;
		using (PULL_NEXT_AUDIO_FRAME_MARKER.Auto())
		{
			lock (audioBufferLock)
			{
				for (int i = 0; i < _newAudioFramesBuffer.Count; i++)
					_audioFramesBuffer.Add(_newAudioFramesBuffer[i]);
				_newAudioFramesBuffer.Clear();

				if (_audioFramesBuffer.Count > 0 && (_audioFramesBuffer[0].samplesPerChannel*_audioFramesBuffer.Count) > _MaxBufferSampleSize)
				{
					// Reduce the buffer size if it is too big to the minimal size
					while (_audioFramesBuffer.Count > 0 && (_audioFramesBuffer[0].samplesPerChannel*_audioFramesBuffer.Count) > _MinBufferSampleSize)
					{
						removeCounter++;
						var f = _audioFramesBuffer[0];
						_audioFramesBuffer.RemoveAt(0);
						_audioFramesPool.Enqueue(f);
					}
				}

				if (_initWaitForAudioBufferFillUp)
				{
					if (_audioFramesBuffer.Count > 0)
					{
						lock (_bufferStatisticsLock)
							_bufferStatistics.waitingForBufferFillUp++;
					}

					// Wait for a minimal Buffer Size before start playing audio
					if (_audioFramesBuffer.Count > 0 && (_audioFramesBuffer[0].samplesPerChannel*_audioFramesBuffer.Count) < _MinBufferSampleSize)
					{
						return false;
					}
					else if (_audioFramesBuffer.Count <= 2)
					{
						return false;
					}
					
					_initWaitForAudioBufferFillUp = false;
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

						UpdateAudioStatistics(removeCounter);
						lock (_bufferStatisticsLock)
						{
							_bufferStatistics.audioBufferUnderrun++;
						}

						return false;
					}

					var nextFrame = _audioFramesBuffer[0];
					int sampledReadSum = nextFrame.channelSamplesReaded.Sum();
					if (sampledReadSum >= nextFrame.noChannels * nextFrame.samplesPerChannel)
					{
						_audioFramesPool.Enqueue(nextFrame);
						_audioFramesBuffer.RemoveAt(0);
					}
					else
					{
						break;
					}

				} while (true);
				UpdateAudioStatistics(removeCounter);

				int availableSamples = 0;
				for (int i = 0; i < _audioFramesBuffer.Count; i++)
				{
					availableSamples += _audioFramesBuffer[i].samplesPerChannel;
				}

				if (availableSamples < frameSize)
				{
					lock (_bufferStatisticsLock)
					{
						_bufferStatistics.audioBufferUnderrun++;
					}
					return false;
				}

				if (_audioFramesBuffer.Count > 0 && _audioFramesBuffer[0].speakerPositions != null)
				{
					lock (_audioMetaLock)
					{
						var admData = new AdmData();
						admData.positions = _audioFramesBuffer[0].speakerPositions.AsEnumerable();
						admData.gains = _audioFramesBuffer[0].gains.AsEnumerable();
						lock (_admEventLock)
							_onAdmDataChanged?.Invoke(admData);
						
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
	
	#region Virtual Speakers

	void ParkAllVirtualSpeakers()
	{
		for (int i = 0; i < _virtualSpeakers.Count; i++)
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
			var speaker = GetOrCreateVirtualSpeakerClass(out var isNew);
			
			float angle = i * Mathf.PI * 2 / channelCount;
			float x = Mathf.Cos(angle) * dist;
			float z = Mathf.Sin(angle) * dist;
			
			speaker.CreateGameObjectWithAudioSource(transform, new Vector3(x, 0, z));
			speaker.CreateAudioSourceBridge(this, i, channelCount, _systemAvailableAudioChannels);
			_virtualSpeakers.Add(speaker);		
		}
	}

	private VirtualSpeakers GetOrCreateVirtualSpeakerClass(out bool isNew)
	{
		if (_parkedVirtualSpeakers.Count > 0)
		{
			isNew = false;
			var vs = _parkedVirtualSpeakers[_parkedVirtualSpeakers.Count - 1];
			_parkedVirtualSpeakers.RemoveAt(_parkedVirtualSpeakers.Count - 1);
			return vs;
		}

		isNew = true;
		return new VirtualSpeakers();
	}
	
	void CreateVirtualSpeakersQuad()
	{
		float dist = virtualSpeakerDistances;
		for (int i = 0; i < 4; i++)
		{
			var speaker = GetOrCreateVirtualSpeakerClass(out var isNew);
			
			Vector3 position = Vector3.zero;
			switch (i)
			{
				case 0 : position = new Vector3(-dist, 0, dist); break;
				case 1 : position = new Vector3(dist, 0, dist); break;
				case 4 : position = new Vector3(-dist, 0, -dist); break;
				case 5 : position = new Vector3(dist, 0, -dist); break;
			}

			if (isNew)
			{
				speaker.CreateGameObjectWithAudioSource(transform, position);
				speaker.CreateAudioSourceBridge(this, i, 4, _systemAvailableAudioChannels);
			}
			else
				speaker.UpdateParameters(position, i, 4, _systemAvailableAudioChannels, false);
			_virtualSpeakers.Add(speaker);
		}		
	}	

	void CreateVirtualSpeakers5point1()
	{
		float dist = virtualSpeakerDistances;
		for (int i = 0; i < 6; i++)
		{
			var speaker = GetOrCreateVirtualSpeakerClass(out var isNew);
			
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
			
			if (isNew)
			{
				speaker.CreateGameObjectWithAudioSource(transform, position);
				speaker.CreateAudioSourceBridge(this, i, 6, _systemAvailableAudioChannels);
			}
			else 
				speaker.UpdateParameters(position, i, 6, _systemAvailableAudioChannels, false);

			_virtualSpeakers.Add(speaker);
		}		
	}

	void CreateVirtualSpeakers7point1()
	{
		float dist = virtualSpeakerDistances;
		for (int i = 0; i < 8; i++)
		{
			var speaker = GetOrCreateVirtualSpeakerClass(out var isNew);
			
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
			
			if (isNew)
			{
				speaker.CreateGameObjectWithAudioSource(transform, position);
				speaker.CreateAudioSourceBridge(this, i, 8, _systemAvailableAudioChannels);
			}
			else 
				speaker.UpdateParameters(position, i, 8, _systemAvailableAudioChannels, false);
			_virtualSpeakers.Add(speaker);
		}
	}

	void CreateOrUpdateSpeakerSetupByAudioMeta()
	{
		_updateAudioMetaSpeakerSetup = false;
		DestroyAudioSourceBridge();
		var speakerPositions = GetReceivedSpeakerPositions();
		_virtualSpeakersCount = _virtualSpeakers.Count;
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
				var speaker = GetOrCreateVirtualSpeakerClass(out var isNew);
				if (isNew)
				{
					speaker.CreateGameObjectWithAudioSource(transform, speakerPositions[i], _receivingObjectBasedAudio);
					speaker.CreateAudioSourceBridge(this, i, speakerPositions.Length, _systemAvailableAudioChannels);
				}
				else 
					speaker.UpdateParameters(speakerPositions[i],i, speakerPositions.Length, _systemAvailableAudioChannels, _receivingObjectBasedAudio);
				_virtualSpeakers.Add(speaker);
			}
		}
		_virtualSpeakersCount = _virtualSpeakers.Count;
	}

	void ResetAudioSpeakerSetup()
	{
		DestroyAudioSourceBridge();
		ParkAllVirtualSpeakers();

		if (!_receiveAudio)
		{
			return;
		}
		
		if (!_receivingObjectBasedAudio && !_receivingAudioMetaSpeakerSetup)
		{
			var audioConfiguration = AudioSettings.GetConfiguration();
			if (_systemAvailableAudioChannels == 2 && _receivedAudioChannels == 2)
			{
				_usingVirtualSpeakers = false;
				Debug.Log("Setting Speaker Mode to Stereo");
				audioConfiguration.speakerMode = AudioSpeakerMode.Stereo;
				AudioSettings.Reset(audioConfiguration);
				CheckPassthroughAudioSource();
				return;
			}

			if (_systemAvailableAudioChannels == 4 && _receivedAudioChannels == 4)
			{
				_usingVirtualSpeakers = false;
				Debug.Log("Setting Speaker Mode to Quad");
				audioConfiguration.speakerMode = AudioSpeakerMode.Quad;
				AudioSettings.Reset(audioConfiguration);
				CheckPassthroughAudioSource();
				return;
			}

			if (_systemAvailableAudioChannels == 6 && _receivedAudioChannels == 4)
			{
				_usingVirtualSpeakers = false;
				Debug.Log("Setting Speaker Mode to 5.1");
				audioConfiguration.speakerMode = AudioSpeakerMode.Mode5point1;
				AudioSettings.Reset(audioConfiguration);
				CheckPassthroughAudioSource();
				return;
			}

			if (_systemAvailableAudioChannels == 8 && _receivedAudioChannels == 4)
			{
				_usingVirtualSpeakers = false;
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

			ParkAllVirtualSpeakers();
			_virtualSpeakersCount = _virtualSpeakers.Count;

			return;
		}
		
		ParkAllVirtualSpeakers();

		_usingVirtualSpeakers = true;
		_virtualSpeakersCount = _virtualSpeakers.Count;
		if (!_receivingAudioMetaSpeakerSetup && channelNo == 4)
			CreateVirtualSpeakersQuad();
		else if (!_receivingAudioMetaSpeakerSetup && channelNo == 6)
			CreateVirtualSpeakers5point1();
		else if (!_receivingAudioMetaSpeakerSetup && channelNo == 8)
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
		var speakerSetup = AudioMeta.GetSpeakerConfigFromXml(metadata, out _receivingObjectBasedAudio, out _);
		if (speakerSetup != null && speakerSetup.Length >= 0)
		{
			_receivingAudioMetaSpeakerSetup = true;
			_updateAudioMetaSpeakerSetup = true;
			lock (_audioMetaLock)
			{
				if (_receivedSpeakerPositions == null)
					_receivedSpeakerPositions = speakerSetup;
			}
		}
		else
			_receivingAudioMetaSpeakerSetup = false;
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
		}
		
		if((_usingVirtualSpeakers && audio.NoChannels != _virtualSpeakersCount) || _receivedAudioChannels != audio.NoChannels)
		{
			settingsChanged = true;
			_receivedAudioChannels = audio.NoChannels;
		}

		if (audio.Metadata != null)
		{
			ReadAudioMetaData(audio.Metadata);
		}
		else
			_receivingAudioMetaSpeakerSetup = false;

		if (settingsChanged)
		{
			_settingsChanged = true; 
		}

		AddAudioFrameToQueue(audio);
		OnAudioFrameReceived?.Invoke(audio);
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

	#endregion

}

} // namespace Klak.Ndi
