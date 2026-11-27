using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace Tablero
{
    public enum Level { Seguro, Avanzado, Riesgo }
    public enum State { Off, On, Partial }

    public class Tweak
    {
        public string Id, Cat, Name, Desc, Effect, Restart;
        public Level Level;
        public bool Hidden; // retired for having no effect: listed only while applied, so it can still be undone
        public string Game { get { return Tablero.Game.OfTweak(Id); } }
        public List<Op> Ops;
        List<Op> usable;
        // Availability is computed once per session: querying tasks and the driver is slow.
        public List<Op> Usable { get { if (usable == null) usable = Ops.Where(Readable).ToList(); return usable; } }
        // What cannot be read could not be restored, so it is not offered.
        static bool Readable(Op o)
        {
            try { if (!o.Available()) return false; foreach (string k in o.AllKeys()) o.Read(k); return true; }
            catch (System.Security.SecurityException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (InvalidDataException) { return false; }
        }
        public bool Available { get { return Usable.Count > 0; } }
        public State Read() { return Read(new Dictionary<string, string>()); }
        // cache avoids rereading what was already read in the same pass.
        public State Read(Dictionary<string, string> cache)
        {
            int on = 0, total = 0;
            foreach (Op o in Usable)
                foreach (string k in o.CurrentKeys()) { string v; if (!cache.TryGetValue(k, out v)) v = o.Read(k); total++; if (v == o.Target) on++; }
            return on == 0 ? State.Off : on == total ? State.On : State.Partial;
        }
    }

    public class Snapshot
    {
        public string Name { get; set; }
        public string Date { get; set; }
        public string Kind { get; set; } // original | auto | manual
        public Dictionary<string, string> Values { get; set; }
        [ScriptIgnore] public string File;
    }

    public class Engine
    {
        const int KeepAuto = 10;
        readonly string dir, originalsFile;
        readonly Dictionary<string, string> originals;
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        public readonly List<Tweak> Tweaks;

        public Engine(string dir, IEnumerable<Tweak> tweaks)
        {
            this.dir = dir;
            Directory.CreateDirectory(Path.Combine(dir, "guardados"));
            originalsFile = Path.Combine(dir, "originales.json");
            originals = File.Exists(originalsFile)
                ? Json.Deserialize<Dictionary<string, string>>(File.ReadAllText(originalsFile))
                : new Dictionary<string, string>();
            Tweaks = tweaks.Where(t => t.Available).ToList();
        }

        internal static void WriteFile(string file, string text)
        {
            string tmp = file + ".tmp";
            using (FileStream f = new FileStream(tmp, FileMode.Create))
            {
                byte[] b = new UTF8Encoding(false).GetBytes(text);
                f.Write(b, 0, b.Length);
                f.Flush(true);
            }
            if (File.Exists(file)) File.Replace(tmp, file, null); else File.Move(tmp, file);
        }
        void SaveOriginals() { WriteFile(originalsFile, Json.Serialize(originals)); }

        class Change { public Op Op; public string Key, Old, New; }

        // Writes and checks each change; if one fails, undoes the previous ones.
        static void Commit(List<Change> changes)
        {
            int i = 0;
            try
            {
                for (; i < changes.Count; i++)
                {
                    Change c = changes[i];
                    c.Op.Write(c.Key, c.New);
                    if (!c.Op.Took(c.Key, c.New)) throw new InvalidOperationException("Windows no aceptó el cambio.");
                }
            }
            catch (Exception ex)
            {
                List<string> stuck = new List<string>();
                for (int j = Math.Min(i, changes.Count - 1); j >= 0; j--)
                {
                    try { if (changes[j].Op.Read(changes[j].Key) != changes[j].Old) changes[j].Op.Write(changes[j].Key, changes[j].Old); }
                    catch (Exception) { stuck.Add(changes[j].Key); }
                }
                if (stuck.Count > 0) throw new InvalidOperationException(ex.Message + " Además no se pudo deshacer: " + string.Join(", ", stuck) + ".", ex);
                throw;
            }
        }

        public void Enable(Tweak t)
        {
            List<Change> ch = new List<Change>();
            List<string> added = new List<string>();
            foreach (Op o in t.Usable)
                foreach (string k in o.CurrentKeys())
                {
                    string cur = o.Read(k);
                    if (cur == o.Target) continue;
                    if (!originals.ContainsKey(k)) { originals[k] = cur; added.Add(k); }
                    ch.Add(new Change { Op = o, Key = k, Old = cur, New = o.Target });
                }
            SaveOriginals();
            try { Commit(ch); }
            catch { foreach (string k in added) originals.Remove(k); SaveOriginals(); throw; }
            Live.After(t);
        }

        // onlyMine: only undoes what the app turned on; what was already like that is left alone.
        public void Disable(Tweak t, bool onlyMine)
        {
            List<Change> ch = new List<Change>();
            foreach (Op o in t.Usable)
            {
                foreach (string k in o.AllKeys().Where(x => originals.ContainsKey(x)))
                    ch.Add(new Change { Op = o, Key = k, Old = o.Read(k), New = originals[k] });
                if (!onlyMine)
                    foreach (string k in o.CurrentKeys().Where(x => !originals.ContainsKey(x) && o.Read(x) == o.Target))
                        ch.Add(new Change { Op = o, Key = k, Old = o.Target, New = o.DefaultFor(k) });
            }
            Commit(ch);
            foreach (Change c in ch) originals.Remove(c.Key);
            SaveOriginals();
            Live.After(t);
        }

        public bool Mine(Tweak t) { return t.Usable.Any(o => o.AllKeys().Any(k => originals.ContainsKey(k))); }

        public Snapshot Save(string name, string kind)
        {
            Snapshot s = new Snapshot { Name = name, Kind = kind, Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Values = ReadAll() };
            s.File = Path.Combine(dir, "guardados", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".json");
            WriteFile(s.File, Json.Serialize(s));
            if (kind == "auto")
                foreach (Snapshot old in List().Where(x => x.Kind == "auto").Skip(KeepAuto)) File.Delete(old.File);
            return s;
        }

        public List<Snapshot> List()
        {
            List<Snapshot> r = new List<Snapshot>();
            foreach (string f in Directory.GetFiles(Path.Combine(dir, "guardados"), "*.json"))
            {
                // An unreadable save is left out of the list, but the file is kept.
                try
                {
                    Snapshot s = Json.Deserialize<Snapshot>(File.ReadAllText(f));
                    if (s == null || s.Values == null) continue;
                    s.File = f;
                    r.Add(s);
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
            }
            return r.OrderByDescending(s => s.Kind == "original").ThenByDescending(s => s.File).ToList();
        }

        public void Delete(Snapshot s)
        {
            if (s.Kind == "original") throw new InvalidOperationException("El guardado original no se borra.");
            File.Delete(s.File);
        }

        List<Change> Diff(Snapshot s)
        {
            List<Change> ch = new List<Change>();
            foreach (Tweak t in Tweaks)
                foreach (Op o in t.Usable)
                    foreach (string k in o.AllKeys())
                    {
                        string v;
                        // Tweaks from newer versions are not in an old Original: they go back to what was there before enabling them.
                        if (!s.Values.TryGetValue(k, out v) && (s.Kind != "original" || !originals.TryGetValue(k, out v))) continue;
                        string cur = o.Read(k);
                        if (cur != v) ch.Add(new Change { Op = o, Key = k, Old = cur, New = v });
                    }
            return ch;
        }
        public Dictionary<string, string> ReadAll()
        {
            Dictionary<string, string> r = new Dictionary<string, string>();
            foreach (Tweak t in Tweaks) foreach (Op o in t.Usable) foreach (string k in o.AllKeys()) r[k] = o.Read(k);
            return r;
        }
        public int DiffCount(Snapshot s, Dictionary<string, string> now)
        {
            Func<KeyValuePair<string, string>, bool> differs = p => { string v; return now.TryGetValue(p.Key, out v) && v != p.Value; };
            return s.Values.Count(differs) + (s.Kind == "original" ? originals.Where(p => !s.Values.ContainsKey(p.Key)).Count(differs) : 0);
        }

        // Restores each value separately: one failure does not prevent recovering the rest.
        public List<string> Restore(Snapshot s, out int restored)
        {
            List<Change> ch = Diff(s);
            if (ch.Any(c => c.Op is IniOp) && IniOp.GameRunning()) throw new InvalidOperationException("Cierra Fortnite antes de restaurar.");
            List<string> errors = new List<string>();
            restored = 0;
            foreach (Change c in ch)
            {
                try { Commit(new List<Change> { c }); restored++; }
                catch (Exception ex) { errors.Add(c.Key + ": " + ex.Message); }
            }
            foreach (Tweak t in Tweaks.Where(x => x.Usable.Any(o => ch.Any(c => c.Op == o)))) Live.After(t);
            foreach (Tweak t in Tweaks)
                foreach (Op o in t.Usable)
                    foreach (string k in o.AllKeys().Where(x => originals.ContainsKey(x)))
                        if (o.Read(k) == originals[k]) originals.Remove(k);
            SaveOriginals();
            return errors;
        }
    }
}
