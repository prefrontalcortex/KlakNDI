using System.Collections.Generic;
using System.Linq;
using Klak.Ndi.Audio;
using SFB;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class ConfigEditor : MonoBehaviour
{
    [SerializeField] private VirtualAudioSetup audioSetup;
    [SerializeField] private ListenerMover _listenerMoverTemplate;
    [SerializeField] private ListenerUI _listenerUITemplate;
    [SerializeField] private TextMeshProUGUI _listenerCountText;
    [SerializeField] private Toggle _saveOscToggle;
    [SerializeField] private Transform _selectedHandle; 
    
    [SerializeField] private Slider _circleRadiusSlider;
    [SerializeField] private Slider _circleStartSlider;
    [SerializeField] private Slider _circleEndSlider;
    
    private List<ListenerMover> _listenerMovers = new List<ListenerMover>();
    private List<ListenerUI> _listenerUIs = new List<ListenerUI>();

    public UnityEvent onConfigChanged = new UnityEvent();
    public UnityEvent<bool> onAnySelected = new UnityEvent<bool>();
    
    private bool _snapPosition = false;

    private Vector3[] _tempPosition;
    private Vector3[] _orgPositions;

    private Vector3[] _groupedOffsets;
    private string _lastSavedFile = "audioConfig.asc";

    public void UpdateAnySelected()
    {
        bool anySelected = _listenerUIs.Any(l => l.selected.isOn);
        onAnySelected.Invoke(anySelected);

        foreach (var l in _listenerMovers)
            l.EnableHandle(!anySelected);

        for (int i = 0; i < _listenerMovers.Count(); i++)
        {
            _listenerMovers[i].IsSelected(_listenerUIs[i].selected.isOn);
        }
     
        _selectedHandle.gameObject.SetActive(anySelected);
        if (anySelected)
        {
            var pos = VirtualAudio.GetListenersPositions();
            _groupedOffsets = new Vector3[pos.Length];
            int selCount = 0;
            for (int i = 0; i < pos.Length; i++)
            {
                if (_listenerUIs[i].selected.isOn)
                {
                    selCount++;
                    _groupedOffsets[i] = pos[i];
                }
                else
                    _groupedOffsets[i] = Vector3.zero;
            }
            Vector3 center = Vector3.zero;
            for (int i = 0; i < pos.Length; i++)
                center += _groupedOffsets[i];
            center /= selCount;
            for (int i = 0; i < pos.Length; i++)
                _groupedOffsets[i] -= center;
            _selectedHandle.position = center;
        }
    }
    
    public void SetTempCircle()
    {
        if (_tempPosition == null)
            _tempPosition = VirtualAudio.GetListenersPositions();
        
        if (_tempPosition == null)
            return;
        
        if (_orgPositions == null)
            _orgPositions = VirtualAudio.GetListenersPositions();
        
        var radius = _circleRadiusSlider.value;
        var start = _circleStartSlider.value;
        var end = _circleEndSlider.value;

        int selectedCount = 0;
        for (int i = 0; i < _tempPosition.Length; i++)
        {
            if (_listenerUIs[i].selected.isOn)
                selectedCount++;
        }

        int selectedi = -1;
        for (int i = 0; i < _tempPosition.Length; i++)
        {
            if (!_listenerUIs[i].selected.isOn)
                continue;

            selectedi++;
            var angle = start + (end - start) * selectedi / selectedCount;
            var pos = new Vector3(Mathf.Sin(angle * Mathf.Deg2Rad) * radius, 0, Mathf.Cos(angle * Mathf.Deg2Rad) * radius);
            _tempPosition[i] = pos;
            VirtualAudio.UpdateListenerPosition(i, pos);
            _listenerUIs[i].UpdatePosition(pos);
            _listenerMovers[i].UpdatePosition(pos);
        }
    }
    
    public void SetTempScale(float scale)
    {
        if (_tempPosition == null)
            _tempPosition = VirtualAudio.GetListenersPositions();
        
        if (_tempPosition == null)
            return;
        
        if (_orgPositions == null)
            _orgPositions = VirtualAudio.GetListenersPositions();
        
        for (int i = 0; i < _tempPosition.Length; i++)
        {
            if (!_listenerUIs[i].selected.isOn)
                continue;

            _tempPosition[i] = _orgPositions[i] * scale;
            VirtualAudio.UpdateListenerPosition(i, _tempPosition[i]);
            _listenerUIs[i].UpdatePosition(_tempPosition[i]);
            _listenerMovers[i].UpdatePosition(_tempPosition[i]);
        }
    }
    
    public void AbortTemp()
    {
        if (_tempPosition == null)
            return;
        
        for (int i = 0; i < _tempPosition.Length; i++)
        {
            if (!_listenerUIs[i].selected.isOn)
                continue;

            VirtualAudio.UpdateListenerPosition(i, _orgPositions[i]);
            _listenerUIs[i].UpdatePosition(_orgPositions[i]);
            _listenerMovers[i].UpdatePosition(_orgPositions[i]);
        }

        _orgPositions = null;
        _tempPosition = null;
    }

    public void ApplyTemp()
    {
        if (_tempPosition == null)
            return;
        
        for (int i = 0; i < _tempPosition.Length; i++)
        {
            if (!_listenerUIs[i].selected.isOn)
                continue;

            VirtualAudio.UpdateListenerPosition(i, _tempPosition[i]);
            _listenerUIs[i].UpdatePosition(_tempPosition[i]);
            _listenerMovers[i].UpdatePosition(_tempPosition[i]);
        }
        _tempPosition = null;
        ConfigChanged();
    }
    
    public void ActivateSnap(bool active)
    {
        _snapPosition = active;
        foreach (var listenerMover in _listenerMovers)
            listenerMover.snapPosition = active;
    }
    
    public void MirrorXAxis()
    {
        var positions = VirtualAudio.GetListenersPositions();
        if (positions == null)
            return;

        for (int i = 0; i < positions.Length; i++)
        {
            if (!_listenerUIs[i].selected.isOn)
                continue;
            var pos = positions[i];
            pos.x = -pos.x;
            VirtualAudio.UpdateListenerPosition(i, pos);
            _listenerMovers[i].UpdatePosition(pos);
            _listenerUIs[i].UpdatePosition(pos);
        }
        UpdateAnySelected();
        ConfigChanged();
    }

    public void MirrorZAxis()
    {
        var positions = VirtualAudio.GetListenersPositions();
        if (positions == null)
            return;

        for (int i = 0; i < positions.Length; i++)
        {
            if (!_listenerUIs[i].selected.isOn)
                continue;
            var pos = positions[i];
            pos.z = -pos.z;
            VirtualAudio.UpdateListenerPosition(i, pos);
            _listenerMovers[i].UpdatePosition(pos);
            _listenerUIs[i].UpdatePosition(pos);
        }
        UpdateAnySelected();
        ConfigChanged();        
    }

    public void RotateAll(float degree)
    {
        var positions = VirtualAudio.GetListenersPositions();
        if (positions == null)
            return;

        for (int i = 0; i < positions.Length; i++)
        {
            if (!_listenerUIs[i].selected.isOn)
                continue;
            var pos = positions[i];
            pos = Quaternion.Euler(0, degree, 0) * pos;
            VirtualAudio.UpdateListenerPosition(i, pos);
            _listenerMovers[i].UpdatePosition(pos);
            _listenerUIs[i].UpdatePosition(pos);
        }
        UpdateAnySelected();
        ConfigChanged();
    }

    public void ReverseChannels()
    {
        var positions = VirtualAudio.GetListenersPositions();
        var volumes = VirtualAudio.GetListenersVolume();
        
        int reversei = positions.Length;
        // Reverse only selected listeners
        for (int i = 0; i < _listenerUIs.Count; i++)
        {
            if (_listenerUIs[i].selected.isOn)
            {

                reversei--;
                while (!_listenerUIs[reversei].selected.isOn)
                {
                    reversei--;
                    if (reversei <= i)
                        break;
                }
                if (reversei <= i)
                    break;
                
                var tempPos = positions[i];
                var tempVol = volumes[i];
                
                positions[i] = positions[reversei];
                volumes[i] = volumes[reversei];
                
                positions[reversei] = tempPos;
                volumes[reversei] = tempVol;
                
                VirtualAudio.SetListenerVolume(i, volumes[i]);
                VirtualAudio.SetListenerVolume(reversei, volumes[reversei]);
                
                VirtualAudio.UpdateListenerPosition(i, positions[i]);
                VirtualAudio.UpdateListenerPosition(reversei, positions[reversei]);
                
                _listenerMovers[i].UpdatePosition(positions[i]);
                _listenerMovers[reversei].UpdatePosition(positions[reversei]);
                
                _listenerUIs[i].UpdatePosition(positions[i]);
                _listenerUIs[reversei].UpdatePosition(positions[reversei]);
                
                _listenerUIs[i].UpdateVolume(volumes[i]);
                _listenerUIs[reversei].UpdateVolume(volumes[reversei]);
            }
            
        }
        
        UpdateAnySelected();
        ConfigChanged();
    }
    
    public void LoadConfigFromFile()
    {
        var dir = System.IO.Path.GetDirectoryName(_lastSavedFile);
        var selection = StandaloneFileBrowser.OpenFilePanel("Load Audio Setup Config", dir, "asc", false);
        if (selection.Length == 0)
            return;
            
        _lastSavedFile = selection[0];
        
        audioSetup.LoadConfigFromFile(_lastSavedFile);
        Debug.Log("Loaded config from file: " + _lastSavedFile);
        UpdateList();
        ConfigChanged();
    }

    public void SelectAllToggle()
    {
        bool anySelected = _listenerUIs.Any(l => l.selected.isOn);

        if (anySelected)
        {
            foreach (var l in _listenerUIs)
                l.Unselect();
        }
        else
        {
            foreach (var l in _listenerUIs)
                l.Select();
        }
        UpdateAnySelected();
    }
    
    public void SaveCurrentConfigToFile()
    {
        var dir = System.IO.Path.GetDirectoryName(_lastSavedFile);
        var filename = System.IO.Path.GetFileName(_lastSavedFile);
        var newPath = StandaloneFileBrowser.SaveFilePanel("Save current Audio Setup Config", dir, filename, "asc");
        if (string.IsNullOrEmpty(newPath))
            return;
        _lastSavedFile = newPath;
        
        audioSetup.SaveCurrentConfigToFile(_lastSavedFile, _saveOscToggle.isOn);
        Debug.Log("Saved config to file: " + _lastSavedFile);
    }
    
    public void AddNewListener()
    {
        VirtualAudio.AddListener(new Vector3(0,0, 0.2f), 1f);
        UpdateList();
        ConfigChanged();
    }

    public void RemoveSelected()
    {
        for (int i = _listenerUIs.Count -1 ; i >= 0; i--)
        {
            if (_listenerUIs[i].selected.isOn)
            {
                VirtualAudio.RemoveListener(i);
            }
        }
        UpdateList();
        ConfigChanged();
    }
    
    public void RemoveLastListener()
    {
        VirtualAudio.RemoveListener(VirtualAudio.GetListenersPositions().Length-1);
        UpdateList();
        ConfigChanged();
    }
    
    private void UpdateList()
    {
        var positions = VirtualAudio.GetListenersPositions();
        if (positions == null)
        {
            for (int i = 0; i < _listenerMovers.Count; i++)
            {
                _listenerMovers[i].gameObject.SetActive(false);
                _listenerUIs[i].gameObject.SetActive(false);
            }
            return;
        }
        
        // Remember the last selected listeners
        List<int> selectedListeners = new List<int>();
        for (int i = 0; i < _listenerUIs.Count; i++)
        {
            if (_listenerUIs[i].selected.isOn)
                selectedListeners.Add(i);
        }

        _listenerCountText.text = positions.Length.ToString();
        
        for (int i = 0; i < positions.Length; i++)
        {
            if (i >= _listenerMovers.Count)
            {
                var newListenerMover = Instantiate(_listenerMoverTemplate, _listenerMoverTemplate.transform.parent);
                _listenerMovers.Add(newListenerMover);
                newListenerMover.snapPosition = _snapPosition;
                
                var newListenerUI = Instantiate(_listenerUITemplate, _listenerUITemplate.transform.parent);
                _listenerUIs.Add(newListenerUI);
                
                newListenerMover.onPointerIsOver.AddListener((bool isOver) =>
                {
                    newListenerUI.Highlight(isOver);
                });
                
                newListenerUI.onPositionChanged.AddListener((Vector3 pos) =>
                {
                    UpdateAnySelected();
                    ConfigChanged();
                    newListenerMover.UpdatePosition(pos);
                });
                
                newListenerMover.onPositionChanged.AddListener((Vector3 pos) =>
                {
                    ConfigChanged();
                    newListenerUI.UpdatePosition(pos);
                });
                
                
            }
            
            _listenerUIs[i].Init(i, positions[i]);
            _listenerUIs[i].gameObject.SetActive(true);
            _listenerUIs[i].transform.SetAsLastSibling();
            
            _listenerMovers[i].channelIndex = i;
            _listenerMovers[i].transform.position = positions[i];
            _listenerMovers[i].gameObject.SetActive(true);
            
        }

        for (int i = positions.Length; i < _listenerMovers.Count; i++)
        {
            _listenerMovers[i].gameObject.SetActive(false);
            _listenerUIs[i].gameObject.SetActive(false);
        }
        UpdateAnySelected();
    }

    private void ConfigChanged()
    {
        onConfigChanged.Invoke();
    }
    
    public void ReorderListener(ListenerUI listener, int toIndex)
    {
        Debug.Log("Set to pos index: "+toIndex);
        var index = _listenerUIs.IndexOf(listener);
        
        var positions = VirtualAudio.GetListenersPositions();
        var volumes = VirtualAudio.GetListenersVolume();
        
        if (positions == null)
            return;
        
        List<Vector3> positionsList = positions.ToList();
        List<float> volumeList = volumes.ToList();
        
        var posToInsert = positions[index];
        var volToInsert = volumes[index];
        
        positionsList.RemoveAt(index);
        volumeList.RemoveAt(index);
        
        if (toIndex <= 0)
        {
            positionsList.Insert(0, posToInsert);
            volumeList.Insert(0, volToInsert);
        }
        else if (toIndex >= positionsList.Count)
        {
            positionsList.Add(posToInsert);
            volumeList.Add(volToInsert);
        }
        else
        {
            positionsList.Insert(toIndex, posToInsert);
            volumeList.Insert(toIndex, volToInsert);
        }

        for (int i = 0; i < positionsList.Count; i++)
        {
            VirtualAudio.UpdateListenerPosition(i, positionsList[i]);
            VirtualAudio.UpdateListenerVolume(i, volumeList[i]);
        }
        UpdateList();
        ConfigChanged();
    }
     
    private void Awake()
    {
        _listenerMoverTemplate.gameObject.SetActive(false);
        _listenerUITemplate.gameObject.SetActive(false);
        VirtualAudio.OnVirtualAudioStateChanged.AddListener(StateChanged);
    }

    private void OnEnable()
    {
        UpdateList();
    }

    private void Update()
    {
        if (_selectedHandle.gameObject.activeSelf && _groupedOffsets != null)
        {
            var handlePos = _selectedHandle.position;
            
            for (int i = 0; i < _groupedOffsets.Length; i++)
            {
                if (_listenerUIs[i].selected.isOn)
                {
                    var newPos = _groupedOffsets[i] + handlePos;
                    if (newPos == _listenerUIs[i].currentPos)
                        continue;
                    VirtualAudio.UpdateListenerPosition(i, newPos);
                    _listenerMovers[i].UpdatePosition(newPos);
                    _listenerUIs[i].UpdatePosition(newPos);
                }
            }
        }
  
    }

    private void StateChanged(bool active)
    {
        UpdateList();
    }
}
