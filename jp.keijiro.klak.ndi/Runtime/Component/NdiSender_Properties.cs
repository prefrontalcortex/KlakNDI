using System;
using Klak.Ndi.Audio;
using UnityEngine;
using UnityEngine.Serialization;

namespace Klak.Ndi {

public enum CaptureMethod { GameView, Camera, Texture }
[AddComponentMenu("NDI/Sender")]
public sealed partial class NdiSender : MonoBehaviour, IAdmDataProvider
{
    #region NDI source settings

    public enum AudioMode {
        None,
        AudioListener, 
        VirtualQuad,
        Virtual5Point1, 
        Virtual7Point1,
        Virtual32Array,
        SpeakerConfigAsset,
        ObjectBased,
        CustomVirtualAudioSetup
    }
    
    public Transform audioOrigin;
    public bool addMissingAudioSourceListenersAtRuntime = false;
    
    [SerializeField] string _ndiName = "NDI Sender";
    string _ndiNameRuntime;
    [Tooltip("If enabled, an AudioSource will be distributed to all Virtual listeners when it gets to the center of all of them.")]
    public bool playCenteredAudioSourcesOnAllSpeakers = true;
    public AudioMode audioMode = AudioMode.AudioListener;
    [Tooltip("Default distance of the Virtual Listeners from the center")]
    public float virtualListenerDistance = 4;
    [FormerlySerializedAs("useCameraPositionForVirtualAttenuation")] [Tooltip("If enabled, the Distance Attenuation will be calculated based on AudioOrigin Position instead of the Virtual Listener Position.")]
    public bool useAudioOriginPositionForVirtualAttenuation = false;
    public int maxObjectBasedChannels = 32;
    public SpeakerConfig customSpeakerConfig;
    [Tooltip("Sending Frame Rate")]
    public FrameRateOptions frameRate = FrameRateOptions.Common_60;
    [Tooltip("Limit render frame rate of this application")]
    public bool setRenderTargetFrameRate = true;
    
    public string ndiName
      { get => _ndiNameRuntime;
        set => SetNdiName(value); }

    void SetNdiName(string name)
    {
        if (_ndiNameRuntime == name) return;
        _ndiName = _ndiNameRuntime = name;
        Restart();
    }

    [SerializeField] bool _keepAlpha = false;

    public bool keepAlpha
      { get => _keepAlpha;
        set => _keepAlpha = value; }

    #endregion

    #region Capture target settings

    [SerializeField] CaptureMethod _captureMethod = CaptureMethod.GameView;
    CaptureMethod _captureMethodRuntime;

    public CaptureMethod captureMethod
      { get => _captureMethodRuntime;
        set => SetCaptureMethod(value); }

    void SetCaptureMethod(CaptureMethod method)
    {
        if (_captureMethodRuntime == method) return;
        _captureMethod = _captureMethodRuntime = method;
        Restart();
    }

    [SerializeField] Camera _sourceCamera = null;
    Camera _sourceCameraRuntime;

    public Camera sourceCamera
      { get => _sourceCamera;
        set => SetSourceCamera(value); }

    void SetSourceCamera(Camera camera)
    {
        if (_sourceCameraRuntime == camera) return;
        _sourceCamera = _sourceCameraRuntime = camera;
        ResetState();
    }

    [SerializeField] Texture _sourceTexture = null;

    public Texture sourceTexture
      { get => _sourceTexture;
        set => _sourceTexture = value; }

    #endregion

    #region Runtime property

    public string metadata { get; set; }

    public Interop.Send internalSendObject => _send;

    #endregion

    #region Resources asset reference

    [SerializeField, HideInInspector] NdiResources _resources = null;

    public void SetResources(NdiResources resources)
      => _resources = resources;

    #endregion

    #region Editor change validation

    // Applies changes on the serialized fields to the runtime properties.
    // We use OnValidate on Editor, which also works as an initializer.
    // Player never call it, so we use Awake instead of it.

    #if UNITY_EDITOR
    internal static string AudioSpatializerExpectedName = "Passthrough Spatializer (NDI)";

    void OnValidate()
    {
    #else
    void Awake()
    {
    #endif
        ndiName = _ndiName;
        captureMethod = _captureMethod;
        sourceCamera = _sourceCamera;
    }

    #endregion
    
    private event AdmDataChangedDelegate _onAdmDataChanged;

    private object _admEventLock = new object();
    
    public void RegisterAdmDataChangedEvent(AdmDataChangedDelegate callback)
    {
        lock (_admEventLock)
            _onAdmDataChanged += callback;
    }

    public void UnregisterAdmDataChangedEvent(AdmDataChangedDelegate callback)
    {
        lock (_admEventLock)
            _onAdmDataChanged -= callback;
    }
}

} // namespace Klak.Ndi
