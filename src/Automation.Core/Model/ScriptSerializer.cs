using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Automation.Core.Model
{
    /// <summary>Reads and writes scripts as indented JSON, using only built-in framework serializers.</summary>
    public static class ScriptSerializer
    {
        public static void Save(AutomationScript script, string path)
        {
            script.Version = AutomationScript.CurrentVersion;
            var serializer = new DataContractJsonSerializer(typeof(AutomationScript));
            using (var ms = new MemoryStream())
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, false, true, "  "))
                {
                    serializer.WriteObject(writer, script);
                }
                File.WriteAllBytes(path, ms.ToArray());
            }
        }

        public static AutomationScript Load(string path)
        {
            var serializer = new DataContractJsonSerializer(typeof(AutomationScript));
            AutomationScript script;
            // Strip a UTF-8 byte order mark first: many editors add one and the JSON reader rejects it.
            byte[] bytes = File.ReadAllBytes(path);
            int offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            using (var stream = new MemoryStream(bytes, offset, bytes.Length - offset))
            {
                script = serializer.ReadObject(stream) as AutomationScript;
            }
            if (script == null)
                throw new InvalidDataException("The file does not contain a script.");
            if (script.Version > AutomationScript.CurrentVersion)
                throw new InvalidDataException("The script was saved by a newer version of the program (version " + script.Version + ").");
            script.Normalize();
            return script;
        }
    }
}
