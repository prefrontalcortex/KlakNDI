using System;
using System.Collections.Generic;
using Klak.Ndi.Audio;
using UnityEngine;
using UnityEditor;

namespace Klak.Ndi.Editor {

[CanEditMultipleObjects]
[CustomEditor(typeof(NdiSender))]
sealed class NdiSenderEditor : UnityEditor.Editor
{
    static class Labels
    {
        public static Label NdiName = "NDI Name";
    }

    #pragma warning disable CS0649

    private AutoProperty _ndiName;
    private AutoProperty _keepAlpha;
    private AutoProperty _captureMethod;
    private AutoProperty _sourceCamera;
    private AutoProperty _sourceTexture;
    private AutoProperty audioMode;
    private AutoProperty virtualListenerDistance;
    private AutoProperty maxObjectBasedChannels;
    private AutoProperty audioOrientation;
    private AutoProperty customAudioOrientationTransform;
    
    private AutoProperty setRenderTargetFrameRate;
    private AutoProperty frameRate;
    
    private AutoProperty customSpeakerConfig;
    private AutoProperty useCameraPositionForVirtualAttenuation;
    private AutoProperty playCenteredAudioSourcesOnAllSpeakers;

    #pragma warning restore

    private AudioSource[] _audioSourcesInScene;
    private Vector2 _audioSourcesScrollPos;

    void OnEnable()
    {
        AutoProperty.Scan(this);
        SearchForAudioSources();
    }

    private void SearchForAudioSources()
    {
        _audioSourcesInScene = Array.Empty<AudioSource>();
        if (((NdiSender.AudioMode)audioMode.Target.enumValueIndex) != NdiSender.AudioMode.AudioListener)
        {
            var audioSources = GameObject.FindObjectsByType<AudioSource>( FindObjectsInactive.Include, FindObjectsSortMode.None);
            var audioSourcesList = new List<AudioSource>();
            foreach (var a in audioSources)
            {
                if (a.GetComponent<AudioSourceListener>() == null)
                {
                    audioSourcesList.Add(a);
                }
            }
            _audioSourcesInScene = audioSourcesList.ToArray();
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // NDI Name
        if (_captureMethod.Target.hasMultipleDifferentValues ||
            _captureMethod.Target.enumValueIndex != (int)CaptureMethod.GameView)
            EditorGUILayout.DelayedTextField(_ndiName, Labels.NdiName);
        
        // Keep Alpha
        EditorGUILayout.PropertyField(_keepAlpha);

        // Capture Method
        EditorGUILayout.PropertyField(_captureMethod);

        EditorGUI.indentLevel++;

        // Source Camera
        if (_captureMethod.Target.hasMultipleDifferentValues ||
            _captureMethod.Target.enumValueIndex == (int)CaptureMethod.Camera)
            EditorGUILayout.PropertyField(_sourceCamera);

        // Source Texture
        if (_captureMethod.Target.hasMultipleDifferentValues ||
            _captureMethod.Target.enumValueIndex == (int)CaptureMethod.Texture)
            EditorGUILayout.PropertyField(_sourceTexture);

        EditorGUILayout.PropertyField(frameRate);
        EditorGUILayout.PropertyField(setRenderTargetFrameRate);
        
        EditorGUI.indentLevel--;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Audio Settings", EditorStyles.boldLabel);
        
        // Show system audio settings
        int availableAudioChannels = Util.AudioChannels(AudioSettings.driverCapabilities);
        var audioSettings = AudioSettings.GetConfiguration();
        int currentAudioChannels = Util.AudioChannels(audioSettings.speakerMode);
        
        EditorGUILayout.LabelField("System Audio Channels", availableAudioChannels.ToString());
        EditorGUI.BeginChangeCheck();
        var newSetting = EditorGUILayout.EnumPopup("Unity Speaker Mode", audioSettings.speakerMode);
        if (EditorGUI.EndChangeCheck())
        {
            audioSettings.speakerMode = (AudioSpeakerMode)newSetting;
            AudioSettings.Reset(audioSettings);
        }
        
        EditorGUI.BeginChangeCheck(); 
        EditorGUILayout.PropertyField(audioMode, new GUIContent("Audio Send Mode"));
        if (EditorGUI.EndChangeCheck())
        {
            if (audioMode.Target.enumValueIndex != (int)NdiSender.AudioMode.AudioListener && _audioSourcesInScene.Length == 0)
            {
                SearchForAudioSources();
            }
        }
        var audioModeEnum = (NdiSender.AudioMode)audioMode.Target.enumValueIndex;
        if (audioModeEnum != NdiSender.AudioMode.AudioListener)
        {
            EditorGUILayout.HelpBox("This AudioMode will create virtual AudioListeners and is not using the Unity builtin spatializer and AudioListener.\n" + 
                "Virtual Audio Listeners do not support Unity's Audio Mixer. All Audio Sources with the " +
                nameof(AudioSourceListener) + " component will be received by the Virtual Listeners.", MessageType.Info);
            
            var spatializer = AudioSettings.GetSpatializerPluginName();
            var spatializerExpectedName = "Dummy Spatializer (NDI)";
            if (spatializer != spatializerExpectedName)
            {
                EditorGUILayout.HelpBox("The Dummy Spatializer plugin is required in the Audio Settings to bypass any spatialized data modifications made by Unity.", MessageType.Error);
                if (GUILayout.Button("Fix"))
                {
                    AudioSettings.SetSpatializerPluginName(spatializerExpectedName);
                    if (AudioSettings.GetSpatializerPluginName() != spatializerExpectedName)
                        Debug.LogWarning("Spatializer plugin not found. If you just installed KlakNDI with Audio Support, please restart Unity. If this issue persists, please report a bug.");
                }   
            }

            if (audioModeEnum != NdiSender.AudioMode.ObjectBased)
            {
                EditorGUILayout.PropertyField(audioOrientation);
                if (audioOrientation.Target.enumValueIndex == (int)NdiSender.AudioOrientationOption.CustomTransform)
                {
                    EditorGUILayout.PropertyField(customAudioOrientationTransform);
                }
            }
            
            if (audioModeEnum == NdiSender.AudioMode.SpeakerConfigAsset)
            {
                if (customSpeakerConfig.Target.objectReferenceValue == null)
                    GUI.color = Color.red;
                EditorGUILayout.PropertyField(customSpeakerConfig);
                GUI.color = Color.white;
            }
            else if (audioModeEnum == NdiSender.AudioMode.ObjectBased)
            {
                EditorGUILayout.PropertyField(maxObjectBasedChannels);
            }
            else
                EditorGUILayout.PropertyField(virtualListenerDistance);
            EditorGUILayout.PropertyField(useCameraPositionForVirtualAttenuation);
            EditorGUILayout.PropertyField(playCenteredAudioSourcesOnAllSpeakers);
        }
        else
        {
            if (availableAudioChannels != currentAudioChannels)
            {
                EditorGUILayout.HelpBox($"You have selected {audioSettings.speakerMode} ({currentAudioChannels} channels), but the current audio device supports {availableAudioChannels} channels.\nOnly {availableAudioChannels} will be sent. Select a virtual send mode if you want to transmit more channels.", MessageType.Warning);
            }
            else {
                EditorGUILayout.HelpBox($"This AudioMode will capture all audio from the AudioListener.\nThe channel amount depends on the audio device capabilities and the Unity Audio Settings.\nWith your current settings, {currentAudioChannels} channels ({audioSettings.speakerMode}) will be sent.", MessageType.Info);
            }
        }
        
        serializedObject.ApplyModifiedProperties();

        if (!Application.isPlaying && _audioSourcesInScene.Length > 0 && audioModeEnum != NdiSender.AudioMode.AudioListener)
        {
            GUI.backgroundColor = Color.cyan;
            GUILayout.Space(30);
            EditorGUILayout.BeginVertical(GUI.skin.window);
            GUILayout.Space(-20);
            GUILayout.Label("Virtual Audio", EditorStyles.boldLabel);
            GUILayout.Label("Missing AudioSourceListener Components:", EditorStyles.boldLabel);

            _audioSourcesScrollPos = EditorGUILayout.BeginScrollView(_audioSourcesScrollPos, GUILayout.MaxHeight(200));
            for (int i = 0; i < _audioSourcesInScene.Length; i++)
            {
                EditorGUILayout.BeginHorizontal();
                if (EditorGUILayout.LinkButton(_audioSourcesInScene[i].name))
                {
                    Selection.activeGameObject = _audioSourcesInScene[i].gameObject;
                }
                GUILayout.FlexibleSpace();
                //EditorGUILayout.LabelField(_audioSourcesInScene[i].name);
                if (GUILayout.Button("Add AudioSourceListener"))
                {
                    _audioSourcesInScene[i].gameObject.AddComponent<AudioSourceListener>();
                    ArrayUtility.RemoveAt(ref _audioSourcesInScene, i);
                    return;
                }
                
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space();
            if (GUILayout.Button("Add to all"))
            {
                foreach (var audioSource in _audioSourcesInScene)
                {
                    audioSource.gameObject.AddComponent<AudioSourceListener>();
                }
                _audioSourcesInScene = Array.Empty<AudioSource>();
            }
            
            GUILayout.FlexibleSpace();
            
            EditorGUILayout.EndVertical();
            GUI.backgroundColor = Color.white;
        }
        
        var ndiSender = target as NdiSender;
        if (Application.isPlaying && VirtualAudio.UseVirtualAudio)
        {
            var channels = ndiSender.GetChannelVisualisations();

            var vol = VirtualAudio.GetListenersVolume();
            var channelPos = ndiSender.GetChannelObjectPositions();
            if (channels != null && vol != null)
            {
                ChannelMeter.Draw(channels, (int channelNo) =>
                {
                    if (ndiSender.audioMode == NdiSender.AudioMode.ObjectBased)
                    {
                     
                        GUILayout.Label("Pos: "+ channelPos[channelNo].ToString());
                        //var r = EditorGUILayout.GetControlRect(false, 10f, GUILayout.Width(80f));
                        //GUI.backgroundColor = Color.white;
                        //EditorGUI.ProgressBar(r, vol[channelNo], vol[channelNo].ToString("P0"));   
                    }
                    else
                    {
                        GUILayout.Label("List.Vol: ");
                        var r = EditorGUILayout.GetControlRect(false, 10f, GUILayout.Width(80f));
                        GUI.backgroundColor = Color.white;
                        
                        EditorGUI.ProgressBar(r, vol[channelNo], vol[channelNo].ToString("P0"));
                    }
                });
                Repaint();
            }
        }
        else
        {
            
#if OSC_JACK
            if (!ndiSender.GetComponent<AdmOscSender>())
            {
                GUILayout.Label("Object Based Audio:");
                if (GUILayout.Button("Add ADM OSC Sender Component", GUILayout.Height(30)))
                {
                    var oscSender = ndiSender.gameObject.AddComponent<AdmOscSender>();
                }
            }
#else
            GUILayout.Label("Add package OscJack to send object based audio position over OSC");
            GUI.enabled = false;
            GUILayout.TextField("https://github.com/keijiro/OscJack");
            GUI.enabled = true;
#endif  
        }
    }
}

} // namespace Klak.Ndi.Editor
