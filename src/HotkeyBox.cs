// Field to choose a global shortcut: shows it ("Ctrl+Shift+O"); pressed, it waits for the next combination with at least one
// modifier. Esc cancels, Backspace leaves it without a shortcut. Pick decides whether it can be used (Hotkeys tells if it is taken);
// a taken one is said in Note and the previous one stays. Styled as Btn in the XAML.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Tablero
{
    public class HotkeyBox : Btn
    {
        public const string Waiting = "Pulsa la combinación…", None = "Sin atajo", Taken = "Combinación ocupada por otro programa.";
        string value = "";
        bool waiting;
        public Func<string, bool> Pick; // true = taken and saved
        public TextBlock Note;          // where "taken" is said; optional

        public string Value { get { return value; } set { this.value = value ?? ""; waiting = false; Show(); } }
        void Show() { Content = waiting ? Waiting : value == "" ? None : value; }
        void Say(string s) { if (Note != null) { Note.Text = s ?? ""; Note.Visibility = s == null ? Visibility.Collapsed : Visibility.Visible; } }

        protected override void OnClick()
        {
            base.OnClick();
            waiting = !waiting;
            Say(null);
            Show();
            if (waiting) Focus();
        }
        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); if (waiting) { waiting = false; Show(); } }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (!waiting) { base.OnPreviewKeyDown(e); return; }
            e.Handled = true; // while waiting no key presses or moves anything
            Key k = e.Key == Key.System ? e.SystemKey : e.Key;
            ModifierKeys m = Keyboard.Modifiers;
            if (k == Key.Escape && m == ModifierKeys.None) { waiting = false; Show(); return; }
            string t;
            if (k == Key.Back && m == ModifierKeys.None) t = "";
            else
            {
                ModifierKeys mm;
                Key kk;
                // Only a modifier so far, or a key without one: keep waiting.
                if (m == ModifierKeys.None || !Hotkeys.Parse(Hotkeys.Text(m, k), out mm, out kk)) return;
                t = Hotkeys.Text(m, k);
            }
            waiting = false;
            if (t == value || Pick == null || Pick(t)) value = t;
            else Say(Taken);
            Show();
        }
    }
}
