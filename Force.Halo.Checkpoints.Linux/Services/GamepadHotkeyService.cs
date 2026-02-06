using System;
using System.Collections.Generic;
using System.Threading;
using SDL2;

namespace Force.Halo.Checkpoints.Linux.Services;

internal sealed class GamepadHotkeyService : IDisposable
{
    private readonly object _sync = new();
    private Thread? _thread;
    private bool _running;
    private bool _captureNextButton;
    private IntPtr _controller = IntPtr.Zero;
    private int _controllerInstanceId = -1;
    private SDL.SDL_GameControllerButton _boundButton = SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_INVALID;
    private string? _controllerName;
    private bool _sdlInitialized;

    public event Action? Activated;
    public event Action<string, int, SDL.SDL_GameControllerButton>? Captured;
    public event Action<string>? Status;

    public string? LastErrorMessage { get; private set; }

    public bool Start()
    {
        lock (_sync)
        {
            if (_running)
            {
                return true;
            }

            try
            {
                if (SDL.SDL_Init(SDL.SDL_INIT_GAMECONTROLLER) < 0)
                {
                    LastErrorMessage = "SDL2 failed to initialize.";
                    Status?.Invoke(LastErrorMessage);
                    return false;
                }
            }
            catch (DllNotFoundException)
            {
                LastErrorMessage = "SDL2 not available (libSDL2 missing).";
                Status?.Invoke(LastErrorMessage);
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                LastErrorMessage = "SDL2 not available (libSDL2 symbols missing).";
                Status?.Invoke(LastErrorMessage);
                return false;
            }

            LastErrorMessage = null;
            _sdlInitialized = true;
            _running = true;
            _thread = new Thread(EventLoop)
            {
                IsBackground = true,
                Name = "GamepadHotkeyService"
            };
            _thread.Start();
            return true;
        }
    }

    public void BeginCapture()
    {
        _captureNextButton = true;
        Status?.Invoke("Press a controller button...");
    }

    public void BindFromSettings(int instanceId, SDL.SDL_GameControllerButton button, string? controllerName)
    {
        _controllerInstanceId = instanceId;
        _boundButton = button;
        _controllerName = controllerName;
    }

    public void ClearBinding()
    {
        _boundButton = SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_INVALID;
        _controllerInstanceId = -1;
        _controllerName = null;
    }

    public bool HasBinding => _boundButton != SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_INVALID;

    public void Dispose()
    {
        lock (_sync)
        {
            _running = false;
        }

        _thread?.Join(200);
        _thread = null;

        if (_controller != IntPtr.Zero)
        {
            SDL.SDL_GameControllerClose(_controller);
            _controller = IntPtr.Zero;
        }

        if (_sdlInitialized)
        {
            try
            {
                SDL.SDL_QuitSubSystem(SDL.SDL_INIT_GAMECONTROLLER);
            }
            catch (DllNotFoundException)
            {
                // Ignore missing SDL2 at shutdown.
            }
            catch (EntryPointNotFoundException)
            {
                // Ignore missing SDL2 symbols at shutdown.
            }
        }
    }

    private void EventLoop()
    {
        while (true)
        {
            lock (_sync)
            {
                if (!_running)
                {
                    return;
                }
            }

            while (SDL.SDL_PollEvent(out SDL.SDL_Event e) == 1)
            {
                switch (e.type)
                {
                    case SDL.SDL_EventType.SDL_CONTROLLERDEVICEADDED:
                        TryOpenPreferredController();
                        break;
                    case SDL.SDL_EventType.SDL_CONTROLLERDEVICEREMOVED:
                        if (_controllerInstanceId == e.cdevice.which)
                        {
                            CloseController();
                            TryOpenPreferredController();
                        }
                        break;
                    case SDL.SDL_EventType.SDL_CONTROLLERBUTTONDOWN:
                        HandleButtonDown(e.cbutton);
                        break;
                }
            }

            Thread.Sleep(8);
        }
    }

    private void HandleButtonDown(SDL.SDL_ControllerButtonEvent buttonEvent)
    {
        if (_captureNextButton)
        {
            _captureNextButton = false;
            _boundButton = (SDL.SDL_GameControllerButton)buttonEvent.button;
            _controllerInstanceId = buttonEvent.which;
            _controllerName = _controller != IntPtr.Zero ? SDL.SDL_GameControllerName(_controller) : "Controller";
            Captured?.Invoke(_controllerName ?? "Controller", _controllerInstanceId, _boundButton);
            return;
        }

        if (_boundButton == SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_INVALID)
        {
            return;
        }

        if (_controllerInstanceId != -1 && buttonEvent.which != _controllerInstanceId)
        {
            return;
        }

        if ((SDL.SDL_GameControllerButton)buttonEvent.button == _boundButton)
        {
            Activated?.Invoke();
        }
    }

    private void TryOpenPreferredController()
    {
        if (_controller != IntPtr.Zero)
        {
            return;
        }

        int numJoysticks = SDL.SDL_NumJoysticks();
        if (numJoysticks <= 0)
        {
            return;
        }

        int? index = FindBestControllerIndex(numJoysticks);
        if (index == null)
        {
            return;
        }

        _controller = SDL.SDL_GameControllerOpen(index.Value);
        if (_controller == IntPtr.Zero)
        {
            return;
        }

        _controllerName = SDL.SDL_GameControllerName(_controller);
        var joystick = SDL.SDL_GameControllerGetJoystick(_controller);
        _controllerInstanceId = SDL.SDL_JoystickInstanceID(joystick);
        Status?.Invoke($"Controller connected: {_controllerName ?? "Controller"}");
    }

    private void CloseController()
    {
        if (_controller != IntPtr.Zero)
        {
            SDL.SDL_GameControllerClose(_controller);
            _controller = IntPtr.Zero;
        }
    }

    private static int? FindBestControllerIndex(int numJoysticks)
    {
        var candidates = new List<(int index, int score)>();
        for (int i = 0; i < numJoysticks; i++)
        {
            if (SDL.SDL_IsGameController(i) == SDL.SDL_bool.SDL_FALSE)
            {
                continue;
            }

            string name = SDL.SDL_GameControllerNameForIndex(i) ?? string.Empty;
            int score = ScoreControllerName(name);

            candidates.Add((i, score));
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        candidates.Sort((a, b) => b.score.CompareTo(a.score));
        return candidates[0].index;
    }

    private static int ScoreControllerName(string name)
    {
        string lower = name.ToLowerInvariant();
        if (lower.Contains("xbox") || lower.Contains("x-box") || lower.Contains("xinput") ||
            lower.Contains("microsoft"))
        {
            return 100;
        }

        if (lower.Contains("generic"))
        {
            return 90;
        }

        if (lower.Contains("ps5") || lower.Contains("dualsense") || lower.Contains("ps4") ||
            lower.Contains("dualshock") || lower.Contains("playstation"))
        {
            return 80;
        }

        return 50;
    }
}
