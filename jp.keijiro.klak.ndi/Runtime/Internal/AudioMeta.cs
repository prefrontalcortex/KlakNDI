using System.Collections.Generic;
using System.Xml;
using JetBrains.Annotations;
using Klak.Ndi.Audio;
using UnityEngine;

namespace Klak.Ndi
{
    internal static class AudioMeta
    {
        public static Vector3[] GetSpeakerConfigFromXml([CanBeNull] string xml, out bool isObjectBased)
        {
            if (xml == null)
            {
                isObjectBased = false;
                return null;
            }
                
            isObjectBased = false;
            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(xml);
            var speakerNodes = xmlDoc.GetElementsByTagName("Speaker");
            var speakers = new Vector3[speakerNodes.Count];
            for (int i = 0; i < speakerNodes.Count; i++)
            {
                var speakerNode = speakerNodes[i];
                if (speakerNode?.Attributes == null) continue;
                var x = float.Parse(speakerNode.Attributes["x"].Value);
                var y = float.Parse(speakerNode.Attributes["y"].Value);
                var z = float.Parse(speakerNode.Attributes["z"].Value);
                if (speakerNode.Attributes["objectbased"] != null)
                {
                    isObjectBased = true;
                }
                speakers[i] = new Vector3(x, y, z);
            }
            return speakers;
        }
        
        public static string GenerateObjectBasedConfigXmlMetaData(List<Vector3> positions)
        {
            var xmlMeta = new XmlDocument();
            // Write in xmlMeta all Speaker positions
            var root = xmlMeta.CreateElement("VirtualSpeakers");
     
            foreach (var pos in positions)
            {
                var speakerNode = xmlMeta.CreateElement("Speaker");
                //var relativePosition = speaker.transform.position - listenerPosition;
                //var relativePosition = speaker.position - listenerPosition;
                speakerNode.SetAttribute("objectbased", "true");
                speakerNode.SetAttribute("x", pos.x.ToString());
                speakerNode.SetAttribute("y", pos.y.ToString());
                speakerNode.SetAttribute("z", pos.z.ToString());
                root.AppendChild(speakerNode);
            }
            xmlMeta.AppendChild(root);
            
            string xml = null;
            // Save xmlDoc to string xml
            using (var stringWriter = new System.IO.StringWriter())
            {
                using (var xmlTextWriter = XmlWriter.Create(stringWriter))
                {
                    xmlMeta.WriteTo(xmlTextWriter);
                }
                xml = stringWriter.ToString();
            }

            return xml;
        }
        
        public static string GenerateSpeakerConfigXmlMetaData()
        {
            var xmlMeta = new XmlDocument();
            // Write in xmlMeta all Speaker positions
            var root = xmlMeta.CreateElement("VirtualSpeakers");
            var listenerPositions = VirtualAudio.GetListenersPositions();
            foreach (var pos in listenerPositions)
            {
                var speakerNode = xmlMeta.CreateElement("Speaker");
                //var relativePosition = speaker.transform.position - listenerPosition;
                //var relativePosition = speaker.position - listenerPosition;
                speakerNode.SetAttribute("x", pos.x.ToString());
                speakerNode.SetAttribute("y", pos.y.ToString());
                speakerNode.SetAttribute("z", pos.z.ToString());
                root.AppendChild(speakerNode);
            }
            xmlMeta.AppendChild(root);
            

            string xml = null;
            // Save xmlDoc to string xml
            using (var stringWriter = new System.IO.StringWriter())
            {
                using (var xmlTextWriter = XmlWriter.Create(stringWriter))
                {
                    xmlMeta.WriteTo(xmlTextWriter);
                }
                xml = stringWriter.ToString();
            }

            return xml;
        }
        
    }
}