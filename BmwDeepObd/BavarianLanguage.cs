#if BAVARIAN_USB_BASELINE
using System;
using Android.Content;
using AndroidX.AppCompat.App;
using AndroidX.Core.OS;

namespace BmwDeepObd
{
    internal static class BavarianLanguage
    {
        private const string Prefs = "bavarian_ui";
        private const string Key = "language";
        public static bool IsRussian(Context context) =>
            context.GetSharedPreferences(Prefs, FileCreationMode.Private)?.GetString(Key, "en") == "ru";

        public static void SetRussian(Context context, bool russian) =>
            context.GetSharedPreferences(Prefs, FileCreationMode.Private)?.Edit()?.PutString(Key, russian ? "ru" : "en")?.Apply();

        public static void EnsureEngineEnglish()
        {
            if (AppCompatDelegate.ApplicationLocales.ToLanguageTags() != "en")
                AppCompatDelegate.ApplicationLocales = LocaleListCompat.ForLanguageTags("en");
        }

        public static string Error(string operation, Exception exception)
        {
            // Keep third-party exception messages out of UI: their language is not controlled.
            // Application-authored IO errors use explicit English strings at their throw sites.
            return operation + " (" + exception.GetType().Name + ").";
        }
    }
}
#endif
