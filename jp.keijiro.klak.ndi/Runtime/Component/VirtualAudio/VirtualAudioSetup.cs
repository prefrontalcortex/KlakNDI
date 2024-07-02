using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace Klak.Ndi.Audio
{
    public class VirtualAudioSetup : MonoBehaviour
    {
        [SerializeField] private NdiSender _ndiSender;
#if OSC_JACK
        [SerializeField] private AdmOscSender _admOsc;
#endif

        [Header("Auto Load from File")]
        [SerializeField] private bool _autoConfigLoad = false;
        public enum SourceType
        {
            StreamingAssets,
            Resources,
            FileSystem,
        }
        public SourceType sourceType = SourceType.Resources;
        public string autoConfigFilePath = "config.asc";
        private DateTime _lastAutoConfigFileModifyTime;
        
        public void ActivateAutoConfigLoad(bool active)
        {
            if (active == _autoConfigLoad)
                return;
            
            _autoConfigLoad = active;
            LoadAutoConfigFile();
        }

        private void OnEnable()
        {
            LoadAutoConfigFile();
        }

        IEnumerator CheckForConfigFileChanges()
        {
            do
            {
                yield return new WaitForSeconds(3f);
                var modTime = File.GetLastWriteTime(autoConfigFilePath);
                if (modTime != _lastAutoConfigFileModifyTime)
                {
                    _lastAutoConfigFileModifyTime = modTime;
                    LoadConfigFromFile(autoConfigFilePath);
                }

            } while (true);
        }
        
        private void LoadAutoConfigFile()
        {
            if (_autoConfigLoad)
            {
                if (string.IsNullOrEmpty(autoConfigFilePath))
                {
                    Debug.LogError("Please set the path for the config file!");
                    return;
                }

                switch (sourceType)
                {
                    case SourceType.StreamingAssets:
                        autoConfigFilePath = Path.Combine(Application.streamingAssetsPath, autoConfigFilePath);
                        break;
                    case SourceType.Resources:
                        var json =  Resources.Load<TextAsset>(autoConfigFilePath).text;
                        LoadConfigFromJson(json);
                        return; 
                }
                
                if (sourceType != SourceType.Resources && !File.Exists(autoConfigFilePath))
                {
                    Debug.LogError("Config file not existing. Path="+autoConfigFilePath);
                    return;
                }

                LoadConfigFromFile(autoConfigFilePath);
                if (sourceType != SourceType.Resources)
                {
                    _lastAutoConfigFileModifyTime = File.GetLastWriteTime(autoConfigFilePath);
                    StartCoroutine(CheckForConfigFileChanges());
                }
            }
        }
        
        public void SaveCurrentConfigToFile(string path, bool saveOscSettings = false)
        {
            var current = GetCurrentConfig();
            if (!saveOscSettings)
                current.oscSetting = null;
            
            var json = current.ToJson();
            System.IO.File.WriteAllText(path, json);
        }

        public void LoadConfigFromFile(string path)
        {
            var json = System.IO.File.ReadAllText(path);
            LoadConfigFromJson(json);
        }

        public void LoadConfigFromJson(string json)
        {
            var loadedConfig = VirtualAudioSetupConfig.FromJson(json);
            LoadConfig(loadedConfig);
        }

        public VirtualAudioSetupConfig GetCurrentConfig()
        {
            var setupConfig = new VirtualAudioSetupConfig();
            setupConfig.maxObjectBasedChannels = VirtualAudio.MaxObjectBasedChannels;
            setupConfig.objectBasedAudio = VirtualAudio.ObjectBasedAudio;
            setupConfig.centeredAudioOnAllSpeakers = VirtualAudio.PlayCenteredAudioSourceOnAllListeners;
            setupConfig.useCameraPositionForVirtualAttenuation = _ndiSender.useCameraPositionForVirtualAttenuation;
            
            var listenerPositions = VirtualAudio.GetListenersPositions();
            var listenerVolumes = VirtualAudio.GetListenersVolume();

            if (listenerPositions != null)
            {
                setupConfig.speakers = new VirtualAudioSetupConfig.Speaker[listenerPositions.Length];
                for (int i = 0; i < listenerPositions.Length; i++)
                {
                    var speaker = new VirtualAudioSetupConfig.Speaker();
                    speaker.position = listenerPositions[i];
                    speaker.volume = listenerVolumes[i];
                    setupConfig.speakers[i] = speaker;
                }
            }
#if OSC_JACK
            var oscSetting = new VirtualAudioSetupConfig.OscSetting();
            oscSetting.farDistance = _admOsc.GetFarDistance();
            oscSetting.nearDistance = _admOsc.GetNearDistance();
            oscSetting.enabled = _admOsc.enabled;
            _admOsc.GetHostIpAndPort(out oscSetting.host, out oscSetting.port);
            setupConfig.oscSetting = oscSetting;
#endif
            return setupConfig;
        }

        public void LoadConfig(VirtualAudioSetupConfig config)
        {
            _ndiSender.audioMode = config.objectBasedAudio
                ? NdiSender.AudioMode.ObjectBased
                : NdiSender.AudioMode.CustomVirtualAudioSetup;
            VirtualAudio.ActivateObjectBasedAudio(config.objectBasedAudio, config.maxObjectBasedChannels);
            VirtualAudio.UseVirtualAudio = true;
            VirtualAudio.PlayCenteredAudioSourceOnAllListeners = config.centeredAudioOnAllSpeakers;
            _ndiSender.useCameraPositionForVirtualAttenuation = config.useCameraPositionForVirtualAttenuation;
            
            if (config.speakers != null)
            {
                VirtualAudio.ClearAllVirtualSpeakerListeners();
                foreach (var speaker in config.speakers)
                {
                    VirtualAudio.AddListener(speaker.position, speaker.volume);
                }
            }
#if OSC_JACK
            if (config.oscSetting != null)
            {
                _admOsc.enabled = config.oscSetting.enabled;
                _admOsc.SetNearDistance(config.oscSetting.nearDistance);
                _admOsc.SetFarDistance(config.oscSetting.farDistance);
                _admOsc.ChangeHostIpAndPort(config.oscSetting.host, config.oscSetting.port);
            }

            if (config.oscInitCommandsFloats != null)
            {
                foreach (var cmd in config.oscInitCommandsFloats)
                    _admOsc.SendCmd(cmd.command, cmd.parameters);
            }
            if (config.oscInitCommandsInts != null)
            {
                foreach (var cmd in config.oscInitCommandsInts)
                    _admOsc.SendCmd(cmd.command, cmd.parameters);
            }
            if (config.oscInitCommands != null)
            {
                foreach (var cmd in config.oscInitCommands)
                    _admOsc.SendCmd(cmd.command);
            }
#endif

        }
    }
}