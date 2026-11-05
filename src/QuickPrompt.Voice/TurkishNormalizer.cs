namespace QuickPrompt.Voice
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text;

    /// <summary>
    /// normalizer-tr 0.8 (Apache-2.0) through native/normalizer's C ABI: numbers, dates, times, money, units and
    /// symbols as a Turkish voice should say them ("12 Kasım 14.30'a" -> "on iki Kasım on dört otuza"), as EMA
    /// Lightning's own frontend does. Without the library the text goes on as written.
    /// </summary>
    public static class TurkishNormalizer
    {
        private delegate IntPtr NormalizeFn(Byte[] input);
        private delegate void FreeFn(IntPtr text);

        private static readonly Lazy<(NormalizeFn Normalize, FreeFn Free)> Native = new Lazy<(NormalizeFn, FreeFn)>(Load);

        public static Boolean Available => Native.Value.Normalize != null;

        public static String Normalize(String text)
        {
            var (normalize, free) = Native.Value;
            if (normalize == null || String.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            var bytes = Encoding.UTF8.GetBytes(text.Replace('\0', ' ') + "\0");
            var result = normalize(bytes);
            if (result == IntPtr.Zero)
            {
                return text;
            }

            try
            {
                return Marshal.PtrToStringUTF8(result) ?? text;
            }
            finally
            {
                free(result);
            }
        }

        private static (NormalizeFn, FreeFn) Load()
        {
            var name = OperatingSystem.IsWindows() ? "qp_normalizer.dll" : OperatingSystem.IsMacOS() ? "libqp_normalizer.dylib" : "libqp_normalizer.so";
            var path = Path.Combine(AppContext.BaseDirectory, name);
            if (!NativeLibrary.TryLoad(path, out var lib)
                || !NativeLibrary.TryGetExport(lib, "qp_normalize_tr", out var normalize)
                || !NativeLibrary.TryGetExport(lib, "qp_free", out var free))
            {
                return (null, null);
            }

            return (Marshal.GetDelegateForFunctionPointer<NormalizeFn>(normalize), Marshal.GetDelegateForFunctionPointer<FreeFn>(free));
        }
    }
}
