namespace OpenaiCompatibleAgent.Voice
{
    /// <summary>How the microphone is opened for voice input.</summary>
    public enum VoiceInputMode
    {
        /// <summary>Mic stays open; VAD auto-detects utterance start/end. Gated while TTS plays.</summary>
        AlwaysOn,
        /// <summary>Mic opens while the push-to-talk key is held; VAD trims silence.</summary>
        PushToTalk
    }
}
