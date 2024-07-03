using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(AudioSource))]
public class AudioTest : MonoBehaviour
{
    [SerializeField] private Slider _distanceSlider;
    [SerializeField] private Slider _spatiallider;

    private AudioSource _audioSource;
    private void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        if (_distanceSlider)
        {
            _distanceSlider.onValueChanged.AddListener(OnDistanceSlider);
            _distanceSlider.SetValueWithoutNotify(transform.localPosition.z);
        }

        if (_spatiallider)
        {
            _spatiallider.onValueChanged.AddListener(OnSpatialSlider);
            _spatiallider.SetValueWithoutNotify(_audioSource.spatialBlend);
        }
    }

    private void OnSpatialSlider(float arg0)
    {
        _audioSource.spatialBlend = arg0;
    }

    private void OnDistanceSlider(float arg0)
    {
        var pos = transform.localPosition;
        pos.z = arg0;
        transform.localPosition = pos;
    }
}
