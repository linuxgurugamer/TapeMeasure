using System;
using System.Collections.Generic;
using UnityEngine;

namespace TapeMeasure
{
    internal enum ShortcutAction
    {
        None = 0,
        ToggleMeasurement,
        CancelMode,
        NewDistance,
        NewAngle,
        EditEndpoints,
        ToggleLabels,
        DeleteSelected,
        CopySelected,
        Undo,
        Redo,
        RedoAlternate,
        SnapModifier,
        AxisX,
        AxisY,
        AxisZ
    }

    internal sealed class ShortcutBinding
    {
        public KeyCode Key;
        public bool Control;
        public bool Shift;
        public bool Alt;

        public ShortcutBinding(KeyCode key, bool control = false, bool shift = false, bool alt = false)
        {
            Key = key;
            Control = control;
            Shift = shift;
            Alt = alt;
        }

        public bool IsBound { get { return Key != KeyCode.None; } }

        public bool MatchesKeyDown()
        {
            if (!IsBound || !GetKeyDown(Key)) return false;
            return ModifiersMatch();
        }

        public bool IsHeld()
        {
            if (!IsBound || !GetKey(Key)) return false;
            return ModifiersMatch();
        }


        public bool IsHeldAllowExtraModifiers()
        {
            if (!IsBound || !GetKey(Key)) return false;
            bool controlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool altHeld = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (Control && !controlHeld) return false;
            if (Shift && !shiftHeld) return false;
            if (Alt && !altHeld) return false;
            return true;
        }

        private bool ModifiersMatch()
        {
            bool controlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool altHeld = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

            // When the bound key itself is a modifier, don't require it twice.
            bool keyIsControl = Key == KeyCode.LeftControl || Key == KeyCode.RightControl;
            bool keyIsShift = Key == KeyCode.LeftShift || Key == KeyCode.RightShift;
            bool keyIsAlt = Key == KeyCode.LeftAlt || Key == KeyCode.RightAlt;

            if (Control && !controlHeld) return false;
            if (Shift && !shiftHeld) return false;
            if (Alt && !altHeld) return false;

            // Extra modifiers are allowed only when they are the bound key itself.
            if (!Control && controlHeld && !keyIsControl) return false;
            if (!Shift && shiftHeld && !keyIsShift) return false;
            if (!Alt && altHeld && !keyIsAlt) return false;
            return true;
        }

        private static bool GetKey(KeyCode key)
        {
            if (key == KeyCode.LeftShift || key == KeyCode.RightShift)
                return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (key == KeyCode.LeftControl || key == KeyCode.RightControl)
                return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (key == KeyCode.LeftAlt || key == KeyCode.RightAlt)
                return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            return Input.GetKey(key);
        }

        private static bool GetKeyDown(KeyCode key)
        {
            if (key == KeyCode.LeftShift || key == KeyCode.RightShift)
                return Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift);
            if (key == KeyCode.LeftControl || key == KeyCode.RightControl)
                return Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.RightControl);
            if (key == KeyCode.LeftAlt || key == KeyCode.RightAlt)
                return Input.GetKeyDown(KeyCode.LeftAlt) || Input.GetKeyDown(KeyCode.RightAlt);
            return Input.GetKeyDown(key);
        }

        public override string ToString()
        {
            if (!IsBound) return "Unbound";
            List<string> parts = new List<string>();
            bool keyIsControl = Key == KeyCode.LeftControl || Key == KeyCode.RightControl;
            bool keyIsShift = Key == KeyCode.LeftShift || Key == KeyCode.RightShift;
            bool keyIsAlt = Key == KeyCode.LeftAlt || Key == KeyCode.RightAlt;
            if (Control && !keyIsControl) parts.Add("Ctrl");
            if (Shift && !keyIsShift) parts.Add("Shift");
            if (Alt && !keyIsAlt) parts.Add("Alt");
            parts.Add(PrettyKey(Key));
            return string.Join("+", parts.ToArray());
        }

        public string Serialize()
        {
            if (!IsBound) return "None";
            return (Control ? "C" : "-") +
                   (Shift ? "S" : "-") +
                   (Alt ? "A" : "-") + ":" + Key;
        }

        public static ShortcutBinding Parse(string text, ShortcutBinding fallback)
        {
            if (string.IsNullOrEmpty(text)) return Clone(fallback);
            if (string.Equals(text, "None", StringComparison.OrdinalIgnoreCase))
                return new ShortcutBinding(KeyCode.None);

            int colon = text.IndexOf(':');
            if (colon > 0)
            {
                string flags = text.Substring(0, colon);
                string keyText = text.Substring(colon + 1);
                KeyCode key;
                if (Enum.TryParse(keyText, true, out key))
                {
                    return new ShortcutBinding(
                        key,
                        flags.IndexOf('C') >= 0,
                        flags.IndexOf('S') >= 0,
                        flags.IndexOf('A') >= 0);
                }
            }

            return Clone(fallback);
        }

        public static ShortcutBinding FromEvent(Event e)
        {
            if (e == null || e.keyCode == KeyCode.None)
                return new ShortcutBinding(KeyCode.None);
            bool keyIsControl = e.keyCode == KeyCode.LeftControl || e.keyCode == KeyCode.RightControl;
            bool keyIsShift = e.keyCode == KeyCode.LeftShift || e.keyCode == KeyCode.RightShift;
            bool keyIsAlt = e.keyCode == KeyCode.LeftAlt || e.keyCode == KeyCode.RightAlt;
            return new ShortcutBinding(
                e.keyCode,
                e.control && !keyIsControl,
                e.shift && !keyIsShift,
                e.alt && !keyIsAlt);
        }

        public static ShortcutBinding Clone(ShortcutBinding source)
        {
            return source == null
                ? new ShortcutBinding(KeyCode.None)
                : new ShortcutBinding(source.Key, source.Control, source.Shift, source.Alt);
        }

        public static bool IsModifierKey(KeyCode key)
        {
            return key == KeyCode.LeftShift || key == KeyCode.RightShift ||
                   key == KeyCode.LeftControl || key == KeyCode.RightControl ||
                   key == KeyCode.LeftAlt || key == KeyCode.RightAlt;
        }

        private static string PrettyKey(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.LeftShift:
                case KeyCode.RightShift: return "Shift";
                case KeyCode.LeftControl:
                case KeyCode.RightControl: return "Ctrl";
                case KeyCode.LeftAlt:
                case KeyCode.RightAlt: return "Alt";
                case KeyCode.Delete: return "Delete";
                case KeyCode.Escape: return "Esc";
                default: return key.ToString();
            }
        }
    }
}
