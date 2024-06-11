using Klak.Ndi;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class OscSetup : MonoBehaviour
{
#if !OSC_JACK
    [Header("[Please add OSC JACK to your project > https://github.com/keijiro/OscJack]")]
#endif
    [SerializeField] private AdmOscSender _oscSender;
    [SerializeField] private TMP_InputField _ip;
    [SerializeField] private TMP_InputField _port;
    [SerializeField] private Slider _nearDistance;
    [SerializeField] private TextMeshProUGUI _nearDistanceValue;
    [SerializeField] private Slider _farDistance;
    [SerializeField] private TextMeshProUGUI _farDistanceValue;
    
    [SerializeField] private Toggle _enableToggle;
#if OSC_JACK
    private void Awake()
    {
        if (_oscSender == null)
        {
            Debug.LogError("OscSender is not set", this);
            return;
        }
        
        if (_enableToggle)
        {
            _enableToggle.onValueChanged.AddListener(
                (enabled => _oscSender.enabled = enabled));
        }
        
        
        if (_nearDistance)
        {
            _nearDistance.SetValueWithoutNotify(_oscSender.GetNearDistance());
            _nearDistanceValue.text = _oscSender.GetNearDistance().ToString("F2");
            _nearDistance.onValueChanged.AddListener(
                (value =>
                {
                    _oscSender.SetNearDistance(value);
                    _nearDistanceValue.text = value.ToString("F2");
                }));
        }
        
        if (_farDistance)
        {
            _farDistance.SetValueWithoutNotify(_oscSender.GetFarDistance());
            _farDistanceValue.text = _oscSender.GetFarDistance().ToString("F2");
            _farDistance.onValueChanged.AddListener(
                (value =>
                {
                    _farDistanceValue.text = value.ToString("F2");
                    _oscSender.SetFarDistance(value);
                }));
        }
    }

    private void OnEnable()
    {
        if (_enableToggle)
        {
            _enableToggle.SetIsOnWithoutNotify(_oscSender.enabled);
        }

        if (_oscSender)
        {
            _oscSender.GetHostIpAndPort(out string ip , out int port);
            _ip.text = ip;
            _port.text = port.ToString();
        }
    }
#endif

    public void Set()
    {
#if OSC_JACK
        _oscSender.ChangeHostIpAndPort(_ip.text, int.Parse(_port.text));
#endif
    }

}
