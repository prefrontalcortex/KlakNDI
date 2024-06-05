using Klak.Ndi;
using UnityEngine;

public class SendingAudioChannelVisualisation : AudioChannelsVisualisation
{
    [SerializeField] private NdiSender _ndiSender;

    protected override float[] GetChannelsData()
    {
        return _ndiSender.GetChannelVisualisations();
    }
}
