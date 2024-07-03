using UnityEngine;

[RequireComponent(typeof(AudioBehaviour))]
public class AudioLatencyTest : MonoBehaviour
{
    [SerializeField] private Material _visMaterial;
    private AudioSource _audioSource;
    private float _clipLength = 0;
    
    private Texture2D _texture;
    private static readonly int CurrentPosition = Shader.PropertyToID("_currentPosition");

    private void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        _audioSource.loop = true;
        _clipLength = _audioSource.clip.length;
        
        _audioSource.clip.LoadAudioData();
        var channels = _audioSource.clip.channels;
        var samplesCnt = _audioSource.clip.samples;
        
        var samples = new float[_audioSource.clip.samples * channels];
        _audioSource.clip.GetData(samples, 0);
        
        _texture = new Texture2D(2048, 1, TextureFormat.R8, false);
        var colors = new Color[2048];
        
        float sampleStep = (float) samplesCnt / 2048;
        float sample = 0;
       
        int pxIndex = 0;
        int sampleCounter = 0;
        for (int i = 0; i < samplesCnt; i++)
        {
            for (int c = 0; c < channels; c++)
                sample = Mathf.Max(sample, Mathf.Abs(samples[i * channels + c]));

            sampleCounter++;
            if (sampleCounter >= sampleStep)
            {
                sampleCounter = 0;
                pxIndex++;
                sample = 0;
            }
      
            if (pxIndex < 2048)
                colors[pxIndex] = new Color(sample, sample, sample);
        }

        _texture.SetPixels(colors);
        _texture.Apply();
        _visMaterial.SetTexture("_audioSamples", _texture);
    }

    private void OnDestroy()
    {
        Destroy(_texture);
    }

    private void OnEnable()
    {
        _audioSource.Play();
    }

    private void Update()
    {
        var currentPosition = _audioSource.time;
        _visMaterial.SetFloat(CurrentPosition, Mathf.InverseLerp(0, _clipLength, currentPosition));
    }
}
