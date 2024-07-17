using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using NAudio.Wave;
using TMPro;

namespace Klak.Ndi.Audio.NAudio
{
    public class VirtualAudioAsioOut : MonoBehaviour
    {
        public enum SenderReceiverMode
        {
            NdiSender,
            NdiReceiver
        }

        // TODO:
        public bool blockNdiAudioWhenUsingAsio = false;

        public SenderReceiverMode senderReceiverMode = SenderReceiverMode.NdiSender;
        [SerializeField] private NdiReceiver _receiver;
        [SerializeField] private TMP_Dropdown dropdown;
        [SerializeField] private TextMeshProUGUI _maxChannelsText;

        private string[] _driverNames;
        private IDisposable _sampleProvider;
        private AsioOut _asioOut;

        public string[] DriverNames => _driverNames;
        
        private string _selectedDriverName;
        
        private void Awake()
        {
            dropdown.onValueChanged.AddListener(OnDropdownValueChanged);
        }

        private void OnDropdownValueChanged(int selectedIndex)
        {
            if (_asioOut != null)
            {
                _asioOut.Stop();
                _asioOut.Dispose();
                _sampleProvider.Dispose();
            }

            if (selectedIndex == 0)
                return;

            _selectedDriverName = _driverNames[selectedIndex - 1];
            _asioOut = new AsioOut(_selectedDriverName);

            var maxChannels = _asioOut.DriverOutputChannelCount;
            
            if (_maxChannelsText)
                _maxChannelsText.text = "Max channels: " + maxChannels;
            
            Debug.Log("Max channels: " + maxChannels);

            ISampleProvider virtualAudioSampleProvider;

            switch (senderReceiverMode)
            {
                case SenderReceiverMode.NdiSender:
                    virtualAudioSampleProvider = new VirtualAudioSampleProvider(_asioOut);
                    break;
                case SenderReceiverMode.NdiReceiver:
                    virtualAudioSampleProvider = new ReceiverSampleProvider(_asioOut, _receiver);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            _sampleProvider = virtualAudioSampleProvider as IDisposable;
            _asioOut.Init(virtualAudioSampleProvider);
            _asioOut.Play();
        }

        void OnEnable()
        {
            _driverNames = AsioOut.GetDriverNames();

            dropdown.ClearOptions();
            ;
            var options = new List<string>();
            options.Add("");
            options.AddRange(_driverNames);

            dropdown.AddOptions(options);
            dropdown.SetValueWithoutNotify(0);

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
                    dropdown.value = Array.IndexOf(_driverNames, _selectedDriverName) + 1;
                }
            }
        }

        private void OnDisable()
        {
            if (_asioOut != null)
            {
                _asioOut.Stop();
                _asioOut.Dispose();
                _sampleProvider.Dispose();
            }
        }
    }
}