namespace QuickPrompt.Core
{
    using System;
    using System.IO;
    using System.IO.Compression;

    /// <summary>
    /// Files the popup page loads to read answers aloud: autolex.js, which respells English words
    /// in Turkish text for a Turkish voice, and its table (embedded gzipped, served unpacked).
    /// </summary>
    public static class VoiceAssets
    {
        private static readonly Lazy<Byte[]> ScriptBytes = new Lazy<Byte[]>(() => Read("QuickPrompt.Voice.autolex.js", false));
        private static readonly Lazy<Byte[]> TableBytes = new Lazy<Byte[]>(() => Read("QuickPrompt.Voice.autolex-tr.json.gz", true));

        public static Byte[] Script => ScriptBytes.Value;

        public static Byte[] TurkishTable => TableBytes.Value;

        private static Byte[] Read(String name, Boolean gzip)
        {
            using var resource = typeof(VoiceAssets).Assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException("Missing resource " + name);
            using var source = gzip ? (Stream)new GZipStream(resource, CompressionMode.Decompress) : resource;
            using var buffer = new MemoryStream();
            source.CopyTo(buffer);
            return buffer.ToArray();
        }
    }
}
