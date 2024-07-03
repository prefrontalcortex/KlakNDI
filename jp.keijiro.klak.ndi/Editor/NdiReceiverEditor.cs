using UnityEngine;
using UnityEditor;
using System.Linq;

namespace Klak.Ndi.Editor {

[CanEditMultipleObjects]
[CustomEditor(typeof(NdiReceiver))]
sealed class NdiReceiverEditor : UnityEditor.Editor
{
    static class Labels
    {
        public static Label NdiName = "NDI Name";
        public static Label Property = "Property";
        public static Label Select = "Select";
    }

    #pragma warning disable CS0649

    private AutoProperty _ndiName;
    private AutoProperty _targetTexture;
    private AutoProperty _targetRenderer;
    private AutoProperty _targetMaterialProperty;
    private AutoProperty _audioSource;
    private AutoProperty _createVirtualSpeakers;
    private AutoProperty _receiveAudio;
    private AutoProperty _bandwidth;
    
    #pragma warning restore
    private bool _foldOutChannelIncome = true;
    private bool _foldOutReceivedSpeakerSetup = true;
    private bool _foldOutBufferStats = false;

    // NDI name dropdown
    void ShowNdiNameDropdown(Rect rect)
    {
        var menu = new GenericMenu();
        var sources = NdiFinder.sourceNames;

        if (sources.Any())
        {
            foreach (var name in sources)
                menu.AddItem(new GUIContent(name), false, OnSelectName, name);
        }
        else
        {
            menu.AddItem(new GUIContent("No source available"), false, null);
        }

        menu.DropDown(rect);
    }

    // NDI source name selection callback
    void OnSelectName(object name)
    {
        serializedObject.Update();
        _ndiName.Target.stringValue = (string)name;
        serializedObject.ApplyModifiedProperties();
    }

    // Request receiver restart.
    //void RequestRestart()
    //{
    //    foreach (NdiReceiver receiver in targets) receiver.Restart();
    //}

    void OnEnable()
    {
            /*
        var finder = new PropertyFinder(serializedObject);
        _ndiName = finder["_ndiName"];
        _targetTexture = finder["_targetTexture"];
        _targetRenderer = finder["_targetRenderer"];
        _targetMaterialProperty = finder["_targetMaterialProperty"];
        _audioSource = finder["_audioSource"];
            */
        AutoProperty.Scan(this);
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.BeginHorizontal();

        // NDI Name
        EditorGUILayout.DelayedTextField(_ndiName, Labels.NdiName);

        // NDI name dropdown
        var rect = EditorGUILayout.GetControlRect(false, GUILayout.Width(60));
        if (EditorGUI.DropdownButton(rect, Labels.Select, FocusType.Keyboard))
            ShowNdiNameDropdown(rect);

        EditorGUILayout.EndHorizontal();

        // Bandwidth mode
        EditorGUILayout.PropertyField(_bandwidth);
        
        // Target Texture/Renderer
        EditorGUILayout.PropertyField(_targetTexture);
        EditorGUILayout.PropertyField(_targetRenderer);

        EditorGUI.indentLevel++;

        if (_targetRenderer.Target.hasMultipleDifferentValues)
        {
            // Multiple renderers selected: Show the simple text field.
            EditorGUILayout.
              PropertyField(_targetMaterialProperty, Labels.Property);
        }
        else if (_targetRenderer.Target.objectReferenceValue != null)
        {
            // Single renderer: Show the material property selection dropdown.
            MaterialPropertySelector.
              DropdownList(_targetRenderer, _targetMaterialProperty);
        }

        EditorGUI.indentLevel--;

        EditorGUI.BeginChangeCheck();
        var currentIndex = _createVirtualSpeakers.Target.boolValue ? 1 : 0;
        if (!_receiveAudio.Target.boolValue) currentIndex = 2;
        var newIndex = EditorGUILayout.Popup("Audio Receiver Mode", currentIndex, new[] { "Automatic by channel count", "Always create Virtual Speakers", "None" });
        if (currentIndex == 0)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox("If the NDI audio stream has at most the supported channel count of this device, a regular AudioSource will be created to pass through the received audio data.\n" +
                                    "Otherwise, Virtual Speakers will be created, and Unity will spatialize them.\n" +
                                    "You can assign a custom AudioSource to receive the data here, otherwise one will be created when needed."
                , MessageType.Info);
            EditorGUILayout.PropertyField(_audioSource);
            EditorGUI.indentLevel--;
        }
        var audioSourceChanged = EditorGUI.EndChangeCheck();
        if (currentIndex != newIndex)
        {
            _createVirtualSpeakers.Target.boolValue = newIndex == 1;
            _receiveAudio.Target.boolValue = newIndex != 2;
        }
        
        serializedObject.ApplyModifiedProperties();

        // if (restart) RequestRestart();

        if (audioSourceChanged)
            foreach (NdiReceiver receiver in targets) receiver.CheckPassthroughAudioSource();

        var ndiReceiver = target as NdiReceiver;
        if (Application.isPlaying)
        {
            var channels = ndiReceiver.GetChannelVisualisations();
            
            if (channels != null)
            {
                _foldOutChannelIncome = EditorGUILayout.BeginFoldoutHeaderGroup(_foldOutChannelIncome, "Received Channels");
                if (_foldOutChannelIncome)
                {
                    ChannelMeter.Draw(channels);
                    Repaint();
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
            }

            var bufferStat = ndiReceiver.GetBufferStatistics();
            _foldOutBufferStats = EditorGUILayout.BeginFoldoutHeaderGroup(_foldOutBufferStats, "Buffer Statistics");
            if (_foldOutBufferStats)
            {
                EditorGUILayout.BeginVertical(GUI.skin.window);
                GUILayout.Space(-20);
                GUILayout.Label("Audio Buffer length: " + bufferStat.audioBufferTimeLength);
                GUILayout.Label(bufferStat.discardedAudioFrames + " audio frames discarded (Overrun)");
                GUILayout.Label(bufferStat.audioBufferUnderrun + " audio frames missing (Underrun)");
                GUILayout.Label("Last Received Frame");
                GUILayout.Label("Audio: "+bufferStat.lastReceivedAudioFrameTime.ToString("F"));
                GUILayout.Label("Video: "+bufferStat.lastReceivedVideoFrameTime.ToString("F"));
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            
            var recvSpeakerSetup = ndiReceiver.GetReceivedSpeakerPositions();
            if (recvSpeakerSetup != null)
            {
                _foldOutReceivedSpeakerSetup = EditorGUILayout.BeginFoldoutHeaderGroup(_foldOutReceivedSpeakerSetup, "Received Speaker Positions");
                if (_foldOutReceivedSpeakerSetup)
                {
                    EditorGUILayout.BeginVertical(GUI.skin.window);
                    GUILayout.Space(-20);
                    // GUILayout.Label("Received Speaker Positions from AudioMeta", EditorStyles.boldLabel);
                    for (int i = 0; i < recvSpeakerSetup.Length; i++)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(i.ToString(), GUILayout.Width(25f));
                        GUI.enabled = false;
                        EditorGUILayout.Vector3Field("", recvSpeakerSetup[i]);
                        GUI.enabled = true;
                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
            }
        }
        else
        {
#if OSC_JACK
            if (!ndiReceiver.GetComponent<AdmOscSender>())
            {
                GUILayout.Label("Object Based Audio:");
                if (GUILayout.Button("Add ADM OSC Sender Component", GUILayout.Height(30)))
                {
                    var oscSender = ndiReceiver.gameObject.AddComponent<AdmOscSender>();
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
