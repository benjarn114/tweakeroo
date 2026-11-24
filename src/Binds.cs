using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Tablero
{
    // Keys the player has set in Fortnite, read from their local settings copy. Nothing is written.
    public class Binds
    {
        public int Edit, Select, Reset;          // 0 = not found
        public bool Confirm, HasConfirm;
        public Dictionary<int, List<string>> Uses = new Dictionary<int, List<string>>(); // key -> game actions

        public static string Folder = Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\FortniteGame\Saved\Cloud");
        public static Binds Load()
        {
            if (!Directory.Exists(Folder)) return null;
            FileInfo f = new DirectoryInfo(Folder).GetFiles("ClientSettings.Sav", SearchOption.AllDirectories).OrderByDescending(x => x.LastWriteTimeUtc).FirstOrDefault();
            if (f == null || f.Length > 16 << 20) return null;
            byte[] b;
            using (FileStream s = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (MemoryStream m = new MemoryStream()) { s.CopyTo(m); b = m.ToArray(); }
            byte[] raw = Inflate(b);
            return raw == null ? null : Parse(raw);
        }

        // The content is compressed (zlib) after a short header.
        public static byte[] Inflate(byte[] b)
        {
            for (int i = 0; i < Math.Min(64, b.Length - 2); i++)
            {
                if (b[i] != 0x78) continue;
                try
                {
                    using (DeflateStream d = new DeflateStream(new MemoryStream(b, i + 2, b.Length - i - 2), CompressionMode.Decompress))
                    using (MemoryStream m = new MemoryStream())
                    {
                        byte[] t = new byte[81920]; int n;
                        while ((n = d.Read(t, 0, t.Length)) > 0) { m.Write(t, 0, n); if (m.Length > 32 << 20) return null; }
                        if (m.Length > 1000) return m.ToArray();
                    }
                }
                catch (InvalidDataException) { }
            }
            return null;
        }

        public static Binds Parse(byte[] raw)
        {
            // Strings prefixed by their length: they are property names and values.
            List<KeyValuePair<int, string>> tok = new List<KeyValuePair<int, string>>();
            for (int i = 0; i < raw.Length - 5; i++)
            {
                int n = BitConverter.ToInt32(raw, i);
                if (n < 2 || n > 90 || i + 4 + n > raw.Length || raw[i + 3 + n] != 0) continue;
                bool ok = true;
                for (int j = i + 4; ok && j < i + 3 + n; j++) ok = raw[j] >= 0x20 && raw[j] <= 0x7e;
                if (!ok) continue;
                tok.Add(new KeyValuePair<int, string>(i, Encoding.ASCII.GetString(raw, i + 4, n - 1)));
                i += 3 + n;
            }
            // There is a default key set and the player's own: theirs is the last complete one.
            List<Dictionary<string, List<string>>> sets = new List<Dictionary<string, List<string>>>();
            Binds r = new Binds();
            string action = null;
            bool keyboard = false; // what follows the controller buttons does not count
            for (int k = 0; k < tok.Count; k++)
            {
                string t = tok[k].Value, next = After(tok, k);
                if (t == "UserActionBindings") { sets.Add(new Dictionary<string, List<string>>()); keyboard = true; }
                else if (t == "CustomGamepadActionBindings") keyboard = false;
                else if (t == "ActionName") action = next;
                else if (t == "KeyName" && keyboard && action != null && next != null)
                {
                    List<string> keys;
                    if (!sets[sets.Count - 1].TryGetValue(action, out keys)) sets[sets.Count - 1][action] = keys = new List<string>();
                    if (!keys.Contains(next)) keys.Add(next);
                }
                else if (t == "bEditConfirmOnReleaseEnabled" && k + 1 < tok.Count && tok[k + 1].Value == "BoolProperty")
                {
                    int at = tok[k + 1].Key + 4 + 13 + 8; // length, "BoolProperty\0", index and size
                    if (at < raw.Length) { r.HasConfirm = true; r.Confirm = (raw[at] & 0x10) != 0; }
                }
            }
            Dictionary<string, List<string>> mine = sets.LastOrDefault(x => x.Count >= 100);
            if (mine == null) return null;
            r.Edit = First(mine, "PerformBuildingEditInteraction");
            r.Select = First(mine, "EditSelect");
            r.Reset = First(mine, "BuildingEditReset");
            foreach (KeyValuePair<string, List<string>> a in mine)
                foreach (string n in a.Value)
                {
                    int vk = Vk(n);
                    if (vk == 0) continue;
                    List<string> l;
                    if (!r.Uses.TryGetValue(vk, out l)) r.Uses[vk] = l = new List<string>();
                    l.Add(a.Key);
                }
            return r;
        }
        static string After(List<KeyValuePair<int, string>> tok, int k)
        {
            for (int j = k + 1; j < Math.Min(tok.Count, k + 4); j++) if (!tok[j].Value.EndsWith("Property")) return tok[j].Value;
            return null;
        }
        static int First(Dictionary<string, List<string>> set, string action)
        {
            List<string> l;
            return set.TryGetValue(action, out l) ? l.Select(Vk).FirstOrDefault(x => x != 0) : 0;
        }

        // What the game often does: a macro on that key would do both things.
        static readonly string[] Common = { "Reload", "TogglePickAxe", "HotbarKey", "Jump", "Crouch", "Sprint", "Use", "Interact", "ToggleInventory", "ToggleFullScreenMap", "Emote", "PushToTalk", "TrapPicker", "CreativeToggle", "Move", "Fire", "Target", "PerformBuildingEdit", "SwitchQuickbar" };
        public string Conflict(int vk)
        {
            List<string> l;
            if (!Uses.TryGetValue(vk, out l)) return null;
            string[] hit = l.Where(a => Common.Any(c => a.StartsWith(c, StringComparison.Ordinal))).Take(2).ToArray();
            return hit.Length == 0 ? null : string.Join(", ", hit);
        }

        static readonly Dictionary<string, int> Names = new Dictionary<string, int> {
            { "LeftMouseButton", 1 }, { "RightMouseButton", 2 }, { "MiddleMouseButton", 4 }, { "ThumbMouseButton", 5 }, { "ThumbMouseButton2", 6 },
            { "MouseScrollUp", Macros.WheelUp }, { "MouseScrollDown", Macros.WheelDown },
            { "BackSpace", 0x08 }, { "Tab", 0x09 }, { "Enter", 0x0D }, { "CapsLock", 0x14 }, { "SpaceBar", 0x20 }, { "PageUp", 0x21 }, { "PageDown", 0x22 }, { "End", 0x23 }, { "Home", 0x24 },
            { "Left", 0x25 }, { "Up", 0x26 }, { "Right", 0x27 }, { "Down", 0x28 }, { "Insert", 0x2D }, { "Delete", 0x2E },
            { "Zero", 0x30 }, { "One", 0x31 }, { "Two", 0x32 }, { "Three", 0x33 }, { "Four", 0x34 }, { "Five", 0x35 }, { "Six", 0x36 }, { "Seven", 0x37 }, { "Eight", 0x38 }, { "Nine", 0x39 },
            { "LeftShift", 0xA0 }, { "RightShift", 0xA1 }, { "LeftControl", 0xA2 }, { "RightControl", 0xA3 }, { "LeftAlt", 0xA4 }, { "RightAlt", 0xA5 },
            { "Semicolon", 0xBA }, { "Equals", 0xBB }, { "Comma", 0xBC }, { "Hyphen", 0xBD }, { "Period", 0xBE }, { "Slash", 0xBF }, { "Tilde", 0xC0 }, { "LeftBracket", 0xDB }, { "Backslash", 0xDC }, { "RightBracket", 0xDD }, { "Apostrophe", 0xDE } };
        public static int Vk(string name)
        {
            int v;
            if (name.Length == 1 && name[0] >= 'A' && name[0] <= 'Z') return name[0];
            if (Names.TryGetValue(name, out v)) return v;
            if (name[0] == 'F' && int.TryParse(name.Substring(1), out v) && v >= 1 && v <= 24) return 0x6F + v;
            return 0;
        }
    }
}
