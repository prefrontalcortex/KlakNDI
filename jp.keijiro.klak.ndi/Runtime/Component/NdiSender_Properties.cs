using Klak.Ndi.Audio;
#if OSC_JACK
using OscJack;
#endif
using UnityEngine;
using UnityEngine.Serialization;

namespace Klak.Ndi {

public enum CaptureMethod { GameView, Camera, Texture }

public sealed partial class NdiSender : MonoBehaviour
{
    #region NDI source settings

    public enum AudioMode { 
        AudioListener, 
        VirtualQuad,
        Virtual5Point1, 
        Virtual7Point1,
        Virtual32Array,
        CustomSpeakerConfig,
        ObjectBased
    }
    
    [SerializeField] string _ndiName = "NDI Sender";
    string _ndiNameRuntime;
    public AudioMode audioMode = AudioMode.AudioListener;
    public float virtualListenerDistance = 10;
    public bool useCameraPositionForVirtualAttenuation = false;
    public int maxObjectBasedChannels = 32;
    public SpeakerConfig customSpeakerConfig;
   
#if OSC_JACK
    [SerializeField] private bool _sendAdmOsc = false;
    [SerializeField] OscConnection _oscConnection;
    [SerializeField] private AdmOscSender.AdmSettings _admSettings = new AdmOscSender.AdmSettings(0.1f, 10f);

#endif    
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
    void OnValidate()
    {
        if (audioMode != AudioMode.AudioListener)
        {
            var spatializerExpectedName = "Dummy Spatializer (NDI)";
            AudioSettings.SetSpatializerPluginName(spatializerExpectedName);
            if (AudioSettings.GetSpatializerPluginName() != spatializerExpectedName)
                Debug.LogWarning("Spatializer plugin not found. If you just installed KlakNDI with Audio Support, please restart Unity. If this issue persists, please report a bug.");
        }

    #else
    void Awake()
    {
    #endif
        
#if OSC_JACK
        if (_sendAdmOsc)
        {
            _admOscSender = new AdmOscSender(_oscConnection);
            _admOscSender.SetSettings(_admSettings);
        }
#endif
        ndiName = _ndiName;
        captureMethod = _captureMethod;
        sourceCamera = _sourceCamera;
    }

    #endregion
}

} // namespace Klak.Ndi
