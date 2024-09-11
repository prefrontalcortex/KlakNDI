using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.Wave;
using TMPro;
using UnityEngine;

namespace Klak.Ndi.Audio.NAudio
{
    public class AsioDeviceSelection : MonoBehaviour
    {
        [SerializeField] private VirtualAudioAsioOut virtualAudioAsio;
        
        [SerializeField] private TMP_Dropdown _dropdown;
        [SerializeField] private TextMeshProUGUI _maxChannelsText;
        
        private string[] _driverNames;
        private string _selectedDriverName;

        private void Awake()
        {
            _dropdown.onValueChanged.AddListener(OnDropdownValueChanged);
        }

        private void OnEnable()
        {
            _driverNames = AsioOut.GetDriverNames();

            _dropdown.ClearOptions();
            ;
            var options = new List<string>();
            options.Add("");
            options.AddRange(_driverNames);

            _dropdown.AddOptions(options);
            _dropdown.SetValueWithoutNotify(0);

            if (_driverNames.Length == 0)
            {
                Debug.Log("No ASIO drivers found!");
                if (_maxChannelsText)
                    _maxChannelsText.text = "No ASIO drivers found!";
            }
            else
            {
                if (_driverNames.Contains(_selectedDriverName))
                {
                    _dropdown.value = Array.IndexOf(_driverNames, _selectedDriverName) + 1;
                }
            }
        }
        
        private void OnDropdownValueChanged(int selectedIndex)
        {
            if (selectedIndex == 0)
            {
                virtualAudioAsio.SetAsioDevice(null);
                return;
            }

            _selectedDriverName = _driverNames[selectedIndex - 1];
            virtualAudioAsio.SetAsioDevice(_selectedDriverName);
        }
    }
}