using System;
using System.Collections.Generic;
using System.Linq;
using Klak.Ndi.Audio;
using UnityEditor;
using UnityEngine;

namespace Klak.Ndi.Audio
{

   [CreateAssetMenu(fileName = "newSpeakerConfig", menuName = "SpeakerConfig")]
   public class SpeakerConfig : ScriptableObject
   {
      [Serializable]
      public class Speaker
      {
         public Vector3 position;
         public float volume;
      }

      [Serializable]
      public class SpeakerGroup
      {
         public string name;
         public Speaker[] speakers = Array.Empty<Speaker>();
      }

      public SpeakerGroup[] speakerGroups = Array.Empty<SpeakerGroup>();

      public Speaker[] GetAllSpeakers()
      {
         return speakerGroups.SelectMany(sg => sg.speakers).ToArray();
      }
      
      public void AddCircleSpeakerGroup(float radius, float startAngleDeg, float height, int count, float volume = 1f)
      {
         var newGroup = new SpeakerGroup();
         ArrayUtility.Add(ref speakerGroups, newGroup);

         List<Speaker> speakers = new List<Speaker>();
         float startAngleRad = startAngleDeg * Mathf.Deg2Rad;
         for (int i = 0; i < count; i++)
         {
            float angle = i * Mathf.PI * 2 / count;
            float x = Mathf.Cos(angle + startAngleDeg) * radius;
            float z = Mathf.Sin(angle + startAngleDeg) * radius;

            speakers.Add(new Speaker { position = new Vector3(x, height, z), volume = volume });
         }

         newGroup.name = $"Circle {radius}m {count} speakers";
         newGroup.speakers = speakers.ToArray();
      }
   }
}