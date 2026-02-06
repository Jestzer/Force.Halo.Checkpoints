using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Force.Halo.Checkpoints.Linux.Services;

internal sealed class X11HotkeyListener : IDisposable
{
    private const int KeyPress = 2;
    private const int KeyPressMask = 1 << 0;
    private const uint ShiftMask = 1 << 0;
    private const uint LockMask = 1 << 1;
    private const uint ControlMask = 1 << 2;
    private const uint Mod1Mask = 1 << 3;
    private const uint Mod2Mask = 1 << 4;
    private const uint Mod4Mask = 1 << 6;

    private readonly object _sync = new();
    private IntPtr _display = IntPtr.Zero;
    private IntPtr _root = IntPtr.Zero;
    private uint _keycode;
    private uint _modifierMask;
    private bool _running;
    private Thread? _thread;
    private List<uint> _grabbedMasks = new();

    public event Action? Activated;

    public bool Bind(string keysymName, bool ctrl, bool shift, bool alt, bool meta)
    {
        lock (_sync)
        {
            if (!EnsureDisplay())
            {
                return false;
            }

            Unbind();

            ulong keysym = XStringToKeysym(keysymName);
            if (keysym == 0)
            {
                return false;
            }

            _keycode = XKeysymToKeycode(_display, keysym);
            if (_keycode == 0)
            {
                return false;
            }

            _modifierMask = 0;
            if (ctrl) _modifierMask |= ControlMask;
            if (shift) _modifierMask |= ShiftMask;
            if (alt) _modifierMask |= Mod1Mask;
            if (meta) _modifierMask |= Mod4Mask;

            XSelectInput(_display, _root, KeyPressMask);

            _grabbedMasks = BuildModifierMasks(_modifierMask);
            foreach (uint mask in _grabbedMasks)
            {
                XGrabKey(_display, (int)_keycode, mask, _root, true, 1, 1);
            }

            XFlush(_display);
            StartLoop();
            return true;
        }
    }

    public void Unbind()
    {
        if (_display == IntPtr.Zero || _keycode == 0)
        {
            return;
        }

        foreach (uint mask in _grabbedMasks)
        {
            XUngrabKey(_display, (int)_keycode, mask, _root);
        }

        _grabbedMasks.Clear();
        XFlush(_display);
        _keycode = 0;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _running = false;
        }

        _thread?.Join(200);
        _thread = null;

        if (_display != IntPtr.Zero)
        {
            Unbind();
            XCloseDisplay(_display);
            _display = IntPtr.Zero;
        }
    }

    private void StartLoop()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        _thread = new Thread(EventLoop)
        {
            IsBackground = true,
            Name = "X11HotkeyListener"
        };
        _thread.Start();
    }

    private void EventLoop()
    {
        while (true)
        {
            lock (_sync)
            {
                if (!_running || _display == IntPtr.Zero)
                {
                    return;
                }
            }

            if (XPending(_display) > 0)
            {
                XNextEvent(_display, out XEvent xevent);
                if (xevent.type == KeyPress)
                {
                    uint state = xevent.xkey.state & ~(LockMask | Mod2Mask);
                    if (xevent.xkey.keycode == _keycode && state == _modifierMask)
                    {
                        Activated?.Invoke();
                    }
                }
            }
            else
            {
                Thread.Sleep(10);
            }
        }
    }

    private bool EnsureDisplay()
    {
        if (_display != IntPtr.Zero)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            return false;
        }

        _display = XOpenDisplay(IntPtr.Zero);
        if (_display == IntPtr.Zero)
        {
            return false;
        }

        _root = XDefaultRootWindow(_display);
        return _root != IntPtr.Zero;
    }

    private static List<uint> BuildModifierMasks(uint baseMask)
    {
        var masks = new List<uint>(4);
        masks.Add(baseMask);
        masks.Add(baseMask | LockMask);
        masks.Add(baseMask | Mod2Mask);
        masks.Add(baseMask | LockMask | Mod2Mask);
        return masks;
    }

    [DllImport("libX11")]
    private static extern IntPtr XOpenDisplay(IntPtr display);

    [DllImport("libX11")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11")]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport("libX11")]
    private static extern int XSelectInput(IntPtr display, IntPtr window, int eventMask);

    [DllImport("libX11")]
    private static extern int XGrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow, bool ownerEvents,
        int pointerMode, int keyboardMode);

    [DllImport("libX11")]
    private static extern int XUngrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow);

    [DllImport("libX11")]
    private static extern int XPending(IntPtr display);

    [DllImport("libX11")]
    private static extern int XNextEvent(IntPtr display, out XEvent xevent);

    [DllImport("libX11")]
    private static extern int XFlush(IntPtr display);

    [DllImport("libX11")]
    private static extern uint XKeysymToKeycode(IntPtr display, ulong keysym);

    [DllImport("libX11")]
    private static extern ulong XStringToKeysym(string name);

    [StructLayout(LayoutKind.Explicit)]
    private struct XEvent
    {
        [FieldOffset(0)]
        public int type;

        [FieldOffset(0)]
        public XKeyEvent xkey;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XKeyEvent
    {
        public int type;
        public IntPtr serial;
        public int send_event;
        public IntPtr display;
        public IntPtr window;
        public IntPtr root;
        public IntPtr subwindow;
        public IntPtr time;
        public int x;
        public int y;
        public int x_root;
        public int y_root;
        public uint state;
        public uint keycode;
        public int same_screen;
    }
}
