using System;
using Klak.Ndi;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class FulldomeMode : MonoBehaviour
{
    public enum Mode
    {
        Fisheye,
        Cubemap
    }
    
    public Mode mode;
    [SerializeField] private Camera _ndiCamera;
    [SerializeField] private NdiSender _ndiSender;
    [SerializeField] private Toggle _toggle;
    [SerializeField] private Transform _fullDomeAudioOrigin;
    [SerializeField] private Transform _defaultAudioOrigin;
    [SerializeField] private GameObject _domeGrid;
    [SerializeField] private Transform domeVisual;
    [SerializeField] private Slider domeRadiusSlider;
    
    [Header("Cubemap Settings")]
    [SerializeField] private RealtimeCubemap _realtimeCubemap;
    [Header("FishEye Settings")]
    [SerializeField] private Material _fullDomeMaterial;
    
    [Range(1f, 2f)]
    public float allowedUndersampling = 2f;
    [Range(0.0001f, 0.1f)]
    public float allowedPerfectRange = 0.01f;
    
    private RenderTexture tempRT;
    
    private static readonly int AllowedUndersampling = Shader.PropertyToID("_AllowedUndersampling");
    private static readonly int AllowedPerfectRange = Shader.PropertyToID("_AllowedPerfectRange");
    private static readonly int Offset = Shader.PropertyToID("_CameraOffset");
    private static readonly int WorldToDomeCam = Shader.PropertyToID("_WorldToDomeCam");
    private static readonly int DomeCamToWorld = Shader.PropertyToID("_DomeCamToWorld");

    public float domeRadius = 20f;
    public Vector3 CameraOffset => _ndiCamera.transform.localPosition;
    
    private RenderTexture _cameraTarget;
    
    private void Awake()
    {
        _toggle.onValueChanged.AddListener(OnToggleValueChanged);
        _toggle.isOn = false;
        _cameraTarget = _ndiCamera.targetTexture;
        _realtimeCubemap.gameObject.SetActive(false);
        _domeGrid.SetActive(false);
        domeRadiusSlider.onValueChanged.AddListener(OnDomeRadiusChanged);
        OnDomeRadiusChanged(domeRadiusSlider.value);
    }

    private void OnDomeRadiusChanged(float domeRadius)
    {
        domeVisual.localScale = Vector3.one * domeRadius;
    }

    public void UpdateTextures()
    {
        _cameraTarget = _ndiCamera.targetTexture;
        if (_toggle.isOn)
        {
            switch (mode)
            {
                case Mode.Fisheye:
                    AssignTempTarget();
                    break;
                case Mode.Cubemap:
                    _realtimeCubemap.targetTexture = _cameraTarget;
                    break;
            }
        }
    }

    private void ActivateFisheye()
    {
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        AssignTempTarget();
    }

    private void ActivateCubemap()
    {
        _realtimeCubemap.targetTexture = _cameraTarget;
        _ndiCamera.gameObject.SetActive(false);
        _realtimeCubemap.gameObject.SetActive(true);
    }

    private void Deactivate()
    {
        _domeGrid.SetActive(false);

        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        _ndiCamera.targetTexture = _cameraTarget;
        _ndiCamera.gameObject.SetActive(true);
    }
    
    private void OnToggleValueChanged(bool isOn)
    {
        _ndiSender.audioOrigin = isOn ? _fullDomeAudioOrigin : _defaultAudioOrigin;
        
        if (isOn)
        {
            _domeGrid.SetActive(true);

            switch (mode)
            {
                case Mode.Fisheye:
                    ActivateFisheye();
                    break;
                case Mode.Cubemap:
                    ActivateCubemap();
                    break;
            }
            ActivateFisheye();
        }
        else
        {
            Deactivate();
        }
    }

    private void OnDestroy()
    {
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        if (tempRT) RenderTexture.ReleaseTemporary(tempRT);
    }

    private void AssignTempTarget()
    {
        if (tempRT) RenderTexture.ReleaseTemporary(tempRT);
        tempRT = RenderTexture.GetTemporary(_cameraTarget.descriptor);
        tempRT.hideFlags = HideFlags.DontSave;
        _ndiCamera.targetTexture = tempRT;
    }
    
    private void OnEndCameraRendering(ScriptableRenderContext arg1, Camera arg2)
    {
        if (arg2 != _ndiCamera) return;
        SetFisheyeMaterialParams(_fullDomeMaterial);
        _fullDomeMaterial.mainTexture = arg2.targetTexture;
        Graphics.Blit(arg2.targetTexture, _cameraTarget, _fullDomeMaterial, 0);
    }
    
    void SetFisheyeMaterialParams(Material material)
    {
        if(!material) return;
       
        if (!material.IsKeywordEnabled("MODE_DOME")) material.EnableKeyword("MODE_DOME");
        if (material.IsKeywordEnabled("MODE_DEBUG_STRETCH")) material.DisableKeyword("MODE_DEBUG_STRETCH");
        

        material.SetFloat(AllowedUndersampling, allowedUndersampling);
        material.SetFloat(AllowedPerfectRange, allowedPerfectRange);
        material.SetVector(Offset, CameraOffset / domeRadius);
            
        Shader.SetGlobalMatrix(WorldToDomeCam, transform.worldToLocalMatrix);
        Shader.SetGlobalMatrix(DomeCamToWorld, transform.localToWorldMatrix);
    }
    
}
