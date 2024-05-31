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
      
      public void AddSpeakerGroup(SpeakerGroup group)
      {
         Array.Resize(ref speakerGroups, speakerGroups.Length + 1);
         speakerGroups[speakerGroups.Length - 1] = group;
      }

      public void CreateDomeSpeakers(float radius, float maxHeight, int[] countPerStage)
      {
         for (int i = 0; i < countPerStage.Length; i++)
         {
          //  float stageRadius = radius - i * Mathf.Sin()
           // AddCircleSpeakerGroup(radius, 0,);
            
           
           // Add CircleSpeakerGroup which going to round to maxHeight
           
         }
      }
      
      public void AddCircleSpeakerGroup(float radius, float startAngleDeg, float height, int count, float volume = 1f)
      {
         var newGroup = new SpeakerGroup();
         AddSpeakerGroup(newGroup);
         
         List<Speaker> speakers = new List<Speaker>();
         float startAngleRad = startAngleDeg * Mathf.Deg2Rad;
         for (int i = 0; i < count; i++)
         {
            float angle = i * Mathf.PI * 2 / count;
            float x = Mathf.Cos(angle + startAngleRad) * radius;
            float z = Mathf.Sin(angle + startAngleRad) * radius;

            speakers.Add(new Speaker { position = new Vector3(x, height, z), volume = volume });
         }

         newGroup.name = $"Circle {radius}m {count} speakers";
         newGroup.speakers = speakers.ToArray();
      }
   }
}