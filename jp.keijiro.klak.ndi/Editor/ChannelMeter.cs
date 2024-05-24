using UnityEditor;
using UnityEngine;

namespace Klak.Ndi.Editor
{
    public static class ChannelMeter
    {
        public static void Draw(float[] channels)
        {
            GUI.backgroundColor = Color.cyan;
            EditorGUILayout.BeginVertical(GUI.skin.window);
            GUILayout.Space(-20);
            EditorGUILayout.LabelField("Audio Channels");
            int channelNo = 0;
            foreach (var channel in channels)
            {
                EditorGUILayout.BeginHorizontal();
                   
                GUILayout.Label(channelNo.ToString(), EditorStyles.miniLabel, GUILayout.Width(15f));
                    
                var r = EditorGUILayout.GetControlRect(false, 10f);
                GUI.backgroundColor = Color.white;
                EditorGUI.ProgressBar(r, channel, "");
                GUI.backgroundColor = Color.cyan;
                    
                GUILayout.Label(channel.ToString("P1"), EditorStyles.miniLabel, GUILayout.Width(40f));
                EditorGUILayout.EndHorizontal();
                channelNo++;
            }
            EditorGUILayout.EndVertical();
            GUI.backgroundColor = Color.white;
        }
    }
}