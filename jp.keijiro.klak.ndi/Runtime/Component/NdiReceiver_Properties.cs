#if OSC_JACK
using OscJack;
#endif

using UnityEngine;
using UnityEngine.Serialization;

namespace Klak.Ndi {

public sealed partial class NdiReceiver : MonoBehaviour
{
    #region NDI source settings

    [SerializeField] string _ndiName = null;
    string _ndiNameRuntime;

    public string ndiName
      { get => _ndiNameRuntime;
        set => SetNdiName(value); }

    void SetNdiName(string name)
    {
        if (_ndiNameRuntime == name) return;
        _ndiName = _ndiNameRuntime = name;
        Restart();
    }

    #endregion

    #region Target settings

    [SerializeField] RenderTexture _targetTexture = null;

    public RenderTexture targetTexture
      { get => _targetTexture;
        set => _targetTexture = value; }

    [SerializeField] Renderer _targetRenderer = null;

    public Renderer targetRenderer
      { get => _targetRenderer;
        set => _targetRenderer = value; }

    [SerializeField] string _targetMaterialProperty = null;

    public string targetMaterialProperty
      { get => _targetMaterialProperty;
        set => _targetMaterialProperty = value; }

    [SerializeField] bool _receiveAudio = true;
    [SerializeField] AudioSource _audioSource = null;
    [Tooltip("When receiving more audio channels than the current audio device is capable of , create virtual speakers for each channel.")]
    [SerializeField] bool _createVirtualSpeakers = true;
    
    public AudioSource audioSource
      { get => _audioSource;
        set { _audioSource = value; CheckPassthroughAudioSource(); } }

    #endregion

    #region Runtime property

    public RenderTexture texture => _converter?.LastDecoderOutput;

    public string metadata { get; set; }

    public Interop.Recv internalRecvObject => _recv;

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

    void OnValidate()
    {
      ndiName = _ndiName;
    }

    #endregion

    #region Audio Settings

    public float virtualSpeakerDistances = 10f;
#if OSC_JACK
    [SerializeField] private bool _sendAdmOsc = false;
    [SerializeField] OscConnection _oscConnection;
    [SerializeField] private AdmOscSender.AdmSettings _admSettings = new AdmOscSender.AdmSettings(0.1f, 10f);
#endif
    
    #endregion
}

} // namespace Klak.Ndi
