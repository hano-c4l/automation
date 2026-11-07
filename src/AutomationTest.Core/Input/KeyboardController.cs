using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using AutomationTest.Core.Native;

namespace AutomationTest.Core.Input
{
    /// <summary>Sends keyboard input through SendInput.</summary>
    public sealed class KeyboardController
    {
        private static readonly HashSet<int> ExtendedKeys = new HashSet<int>
        {
            0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, // PageUp .. Down
            0x2C, 0x2D, 0x2E,                               // PrintScreen, Insert, Delete
            0x5B, 0x5C, 0x5D,                               // Win keys, Apps
            0x6F, 0x90,                                     // NumDivide, NumLock
            0xA3, 0xA5                                      // RightCtrl, RightAlt
        };

        static KeyboardController()
        {
            if (Marshal.SizeOf(typeof(NativeMethods.KEYINPUT)) != Marshal.SizeOf(typeof(NativeMethods.INPUT)))
                throw new InvalidOperationException("Keyboard input structure does not match the native INPUT size.");
        }

        public void Down(int virtualKey) => SendKey(virtualKey, false);

        public void Up(int virtualKey) => SendKey(virtualKey, true);

        /// <summary>Presses the key, keeps it down for holdMs, then releases it (also when cancelled).</summary>
        public void Press(int virtualKey, int holdMs, CancellationToken token)
        {
            Down(virtualKey);
            try { Hold(holdMs, token); }
            finally { Up(virtualKey); }
        }

        /// <summary>Presses the keys in order, holds them, then releases them in reverse order (also when cancelled).</summary>
        public void Combo(IList<int> virtualKeys, int holdMs, CancellationToken token)
        {
            int pressed = 0;
            try
            {
                foreach (int key in virtualKeys)
                {
                    Down(key);
                    pressed++;
                }
                Hold(holdMs, token);
            }
            finally
            {
                for (int i = pressed - 1; i >= 0; i--) Up(virtualKeys[i]);
            }
        }

        /// <summary>
        /// Types text as Unicode characters, so any language works regardless of keyboard layout. New lines and tabs
        /// are sent as Enter and Tab. totalMs is spread evenly over the characters (0 types as fast as possible).
        /// </summary>
        public void TypeText(string text, int totalMs, CancellationToken token)
        {
            if (string.IsNullOrEmpty(text)) return;
            double perChar = totalMs > 0 ? totalMs / (double)text.Length : 0;
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < text.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                char c = text[i];
                if (c == '\r') { }
                else if (c == '\n') { Down(0x0D); Up(0x0D); }
                else if (c == '\t') { Down(0x09); Up(0x09); }
                else SendUnicode(c);

                if (perChar > 0)
                {
                    long due = (long)(perChar * (i + 1));
                    long remaining = due - watch.ElapsedMilliseconds;
                    if (remaining > 0) token.WaitHandle.WaitOne((int)remaining);
                }
            }
            token.ThrowIfCancellationRequested();
        }

        private static void Hold(int ms, CancellationToken token)
        {
            if (ms > 0) token.WaitHandle.WaitOne(ms);
            token.ThrowIfCancellationRequested();
        }

        private static void SendKey(int virtualKey, bool up)
        {
            uint flags = (up ? NativeMethods.KEYEVENTF_KEYUP : 0) | (ExtendedKeys.Contains(virtualKey) ? NativeMethods.KEYEVENTF_EXTENDEDKEY : 0);
            Send(new NativeMethods.KEYBDINPUT
            {
                wVk = (ushort)virtualKey,
                wScan = (ushort)NativeMethods.MapVirtualKey((uint)virtualKey, 0),
                dwFlags = flags
            });
        }

        private static void SendUnicode(char c)
        {
            Send(new NativeMethods.KEYBDINPUT { wScan = c, dwFlags = NativeMethods.KEYEVENTF_UNICODE });
            Send(new NativeMethods.KEYBDINPUT { wScan = c, dwFlags = NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP });
        }

        private static void Send(NativeMethods.KEYBDINPUT key)
        {
            var input = new NativeMethods.KEYINPUT { type = NativeMethods.INPUT_KEYBOARD, ki = key };
            uint sent = NativeMethods.SendKeyInput(1, new[] { input }, Marshal.SizeOf(typeof(NativeMethods.INPUT)));
            if (sent != 1)
                throw new InvalidOperationException("SendInput failed, Win32 error " + Marshal.GetLastWin32Error());
        }
    }
}
