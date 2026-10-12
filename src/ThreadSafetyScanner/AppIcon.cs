using System.Drawing;

namespace ThreadSafetyScanner
{
    internal static class AppIcon
    {
        internal static readonly Icon Value = Load();
        private static Icon Load()
        {
            using (var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("ThreadSafetyScanner.AppIcon.ico"))
            using (var icon = new Icon(stream)) return (Icon)icon.Clone();
        }
    }
}
