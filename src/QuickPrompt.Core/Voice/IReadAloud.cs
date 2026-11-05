namespace QuickPrompt.Core
{
    using System;

    /// <summary>
    /// Reading answers aloud with local voices (QuickPrompt.Voice: EMA Lightning for Turkish, Kokoro for English).
    /// The popup page asks for one sentence at a time and plays the WAV it gets back. Null on ChatManager when
    /// the host has no voices (the Logi plugin).
    /// </summary>
    public interface IReadAloud
    {
        /// <summary>{available, state: "ready"|"missing"|"downloading", received, size, error, languages:[...],
        /// voices:{en:[{id,name}]}} for the page.</summary>
        String StatusJson();

        /// <summary>Downloads the voice models once (checked by size and SHA-256); progress shows in StatusJson.</summary>
        void Download();

        void CancelDownload();

        /// <summary>16-bit mono WAV of one piece of text, or null when the language has no voice or the models are
        /// not there. lang: "tr" or "en"; voice: an English voice id ("" = the default); speed 0.5–2.</summary>
        Byte[] Speak(String lang, String text, String voice, Double speed);
    }
}
