using Klak.Ndi;
using UnityEngine;

public class ReceivingAudioChannelVisualisation : AudioChannelsVisualisation
{
    [SerializeField] private NdiReceiver _ndiReceiver;

    protected override float[] GetChannelsData()
    {
        return _ndiReceiver.GetChannelVisualisations();
    }
}
