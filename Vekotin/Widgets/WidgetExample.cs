using System.Text.Json;

namespace Vekotin.Widgets
{
    abstract class WidgetExample
    {
        public abstract string FolderName { get; }
        public abstract Dictionary<string, string> Files { get; }

        protected static string ToJson(object obj) =>
            JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
    }
}
