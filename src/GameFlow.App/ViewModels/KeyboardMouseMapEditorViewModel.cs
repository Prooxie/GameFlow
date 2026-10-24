using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using GameFlow.Infrastructure.Runtime.Input;

namespace GameFlow.App.ViewModels;

/// <summary>
/// The keyboard &amp; mouse controller map editor: every controller control,
/// what keys and mouse inputs produce it, and what mouse movement drives.
/// </summary>
/// <remarks>
/// <para>
/// Binding is done by pressing the key, not by picking it from a list:
/// the editor watches the same Raw Input key state the runtime reads, so
/// whatever key arrives is exactly the code the runtime will look up —
/// including keys a list would get wrong, such as keypad Enter. Mouse
/// buttons and the wheel are captured by clicking or scrolling in the
/// capture box, because a mouse click is also how the user operates the
/// editor itself.
/// </para>
/// <para>
/// Every change saves at once; the map applies to every keyboard and mouse
/// assigned to any virtual controller.
/// </para>
/// </remarks>
public sealed class KeyboardMouseMapEditorViewModel : ViewModelBase, IDisposable
{
    /// <summary>Escape cancels a capture rather than being bound.</summary>
    private const int EscapeKey = 0x1B;

    private readonly KeyboardMouseMapStore store;
    private readonly IKeyboardStateSource keyboard;
    private readonly DispatcherTimer captureTimer;
    private HashSet<int> keysHeldAtCaptureStart = [];
    private KeyboardMouseActionRow? capturing;
    private KeyboardMouseBinding? pendingInput;
    private bool loading;
    private bool updatingDevices;
    private bool disposed;
    private readonly GameFlow.Infrastructure.Runtime.InputDeviceCatalog? catalog;
    private string? deviceId;
    private MapDeviceOption? selectedDevice;
    private KeyboardMouseMap Current => store.CurrentFor(deviceId);
    private void Save(KeyboardMouseMap map) => store.SaveFor(map, deviceId);
    public ObservableCollection<MapDeviceOption> Devices { get; } = [];
    public MapDeviceOption? SelectedDevice
    {
        get => selectedDevice;
        set
        {
            if (updatingDevices) { return; }
            if (!SetProperty(ref selectedDevice, value)) { return; }
            CancelCapture();
            deviceId = value?.Id;
            Load();
            OnPropertyChanged(nameof(ShowMouseOptions));
        }
    }
    public bool ShowMouseOptions => selectedDevice?.Category != GameFlow.Infrastructure.Runtime.DeviceCategory.Keyboard;

    public void SelectDevice(string? id)
    {
        RefreshDevices();
        SelectedDevice = Devices.FirstOrDefault(option => option.Id == id) ?? Devices.FirstOrDefault();
    }

    private void OnDevicesChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(RefreshDevices);
    private void RefreshDevices()
    {
        if (disposed) { return; }
        var old = selectedDevice;
        var choices = new List<MapDeviceOption> { new(null, string.Empty, null) };
        if (catalog is not null)
        {
            choices.AddRange(catalog.Devices.Where(device => device.Category is
                GameFlow.Infrastructure.Runtime.DeviceCategory.Keyboard or GameFlow.Infrastructure.Runtime.DeviceCategory.Mouse)
                .Select(device => new MapDeviceOption(device.Id, device.DisplayName, device.Category)));
        }
        if (old?.Id is not null && choices.All(option => option.Id != old.Id)) { choices.Add(old); }
        if (!Devices.Select(option => (option.Id, option.Name, option.Category))
            .SequenceEqual(choices.Select(option => (option.Id, option.Name, option.Category))))
        {
            // Keep selection writeback out of an ItemsSource refresh.
            var id = deviceId;
            updatingDevices = true;
            Devices.Clear();
            foreach (var choice in choices) { Devices.Add(choice); }
            selectedDevice = Devices.FirstOrDefault(option => option.Id == id) ?? Devices[0];
            deviceId = selectedDevice.Id;
            updatingDevices = false;
            OnPropertyChanged(nameof(SelectedDevice));
        }
    }

    public KeyboardMouseMapEditorViewModel(KeyboardMouseMapStore store, IKeyboardStateSource keyboard, GameFlow.Infrastructure.Runtime.InputDeviceCatalog? catalog = null)
    {
        this.catalog = catalog;
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));

        foreach (var action in KeyboardMouseMap.EditableActions)
        {
            Rows.Add(new KeyboardMouseActionRow(this, action));
        }

        MotionTargets =
        [
            new(MouseMotionTarget.RightStick),
            new(MouseMotionTarget.LeftStick),
            new(MouseMotionTarget.None),
        ];

        captureTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        captureTimer.Tick += OnCaptureTick;

        CancelCaptureCommand = new RelayCommand(CancelCapture);
        ResetToDefaultCommand = new RelayCommand(() => store.ResetFor(deviceId));

        if (catalog is not null) { catalog.Updated += OnDevicesChanged; }
        RefreshDevices();
        store.Changed += OnStoreChanged;
        Load();
    }

    public ObservableCollection<KeyboardMouseActionRow> Rows { get; } = [];

    public IReadOnlyList<MotionTargetOption> MotionTargets { get; }

    public ICommand CancelCaptureCommand { get; }

    public ICommand ResetToDefaultCommand { get; }

    public bool IsCapturing => capturing is not null || pendingInput is not null;

    public void PrepareInput(int key, MouseInput input)
    {
        CancelCapture();
        if (KeyboardVirtualKeys.IsSupported(key) || input != MouseInput.None)
        {
            pendingInput = new KeyboardMouseBinding(GamepadAction.None, key, input);
            RaiseCaptureChanged();
        }
    }

    public string CapturePrompt => pendingInput is { } pending
        ? UiTextResources.Format("InputMapChooseControl", pending.Key > 0 ? VirtualKeyNames.GetName(pending.Key) : UiTextResources.EnumValue(pending.Mouse))
        : capturing is null
        ? string.Empty
        : UiTextResources.Format("InputMapCapturePrompt", capturing.Label);

    public MotionTargetOption? SelectedMotionTarget
    {
        get => MotionTargets.FirstOrDefault(option => option.Target == Current.MouseMotion);
        set
        {
            if (value is not null && !loading)
            {
                Save(Current with { MouseMotion = value.Target });
            }
        }
    }

    public double MouseSensitivity
    {
        get => Current.MouseSensitivity;
        set
        {
            if (!loading)
            {
                Save(Current with { MouseSensitivity = Math.Round(value, 2) });
            }
        }
    }

    public double MouseAntiDeadzone
    {
        get => Current.MouseAntiDeadzone;
        set
        {
            if (!loading)
            {
                Save(Current with { MouseAntiDeadzone = Math.Round(value, 2) });
            }
        }
    }

    public bool InvertMouseY
    {
        get => Current.InvertMouseY;
        set
        {
            if (!loading)
            {
                Save(Current with { InvertMouseY = value });
            }
        }
    }

    /// <summary>Starts listening for the next key or mouse input to bind to <paramref name="row"/>.</summary>
    internal void BeginCapture(KeyboardMouseActionRow row)
    {
        if (pendingInput is { } pending) { Bind(pending with { Action = row.Action }); return; }
        CancelCapture();
        capturing = row;
        // Keys already held (the one that was used to activate this, a
        // modifier) must be released and pressed again to count.
        keysHeldAtCaptureStart = [.. (deviceId is null ? keyboard.GetPressedKeysAggregate() : keyboard.GetPressedKeys(deviceId))];
        captureTimer.Start();
        RaiseCaptureChanged();
    }

    /// <summary>Called by the view for a click or wheel notch inside the capture box.</summary>
    public void CaptureMouse(MouseInput input)
    {
        if (capturing is null || input == MouseInput.None || selectedDevice?.Category == GameFlow.Infrastructure.Runtime.DeviceCategory.Keyboard)
        {
            return;
        }

        Bind(new KeyboardMouseBinding(capturing.Action, Mouse: input));
    }

    internal void RemoveBinding(KeyboardMouseBinding binding) =>
        Save(Current with { Bindings = [.. Current.Bindings.Where(b => b != binding)] });

    internal void ClearAction(GamepadAction action) =>
        Save(Current with { Bindings = [.. Current.Bindings.Where(b => b.Action != action)] });

    private void OnCaptureTick(object? sender, EventArgs e)
    {
        if (capturing is null)
        {
            captureTimer.Stop();
            return;
        }

        var pressed = (deviceId is null ? keyboard.GetPressedKeysAggregate() : keyboard.GetPressedKeys(deviceId));
        if (keyboard.GetPressedKeysAggregate().Contains(EscapeKey)) { CancelCapture(); return; }
        if (selectedDevice?.Category == GameFlow.Infrastructure.Runtime.DeviceCategory.Mouse) { return; }
        keysHeldAtCaptureStart.IntersectWith(pressed); // a released key may be pressed again
        foreach (var key in pressed)
        {
            if (keysHeldAtCaptureStart.Contains(key))
            {
                continue;
            }

            if (key == EscapeKey)
            {
                CancelCapture();
                return;
            }

            if (KeyboardVirtualKeys.IsSupported(key))
            {
                Bind(new KeyboardMouseBinding(capturing.Action, Key: key));
                return;
            }
        }
    }

    private void Bind(KeyboardMouseBinding binding)
    {
        CancelCapture();
        var current = Current.Bindings;
        if (!current.Contains(binding))
        {
            Save(Current with { Bindings = [.. current, binding] });
        }
    }

    private void CancelCapture()
    {
        if (capturing is null && pendingInput is null)
        {
            return;
        }

        capturing = null;
        pendingInput = null;
        captureTimer.Stop();
        RaiseCaptureChanged();
    }

    private void RaiseCaptureChanged()
    {
        OnPropertyChanged(nameof(IsCapturing));
        OnPropertyChanged(nameof(CapturePrompt));
        foreach (var row in Rows)
        {
            row.RaiseCaptureChanged(ReferenceEquals(row, capturing));
        }
    }

    private void OnStoreChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(Load);

    private void Load()
    {
        if (disposed) { return; }
        loading = true;
        try
        {
            var bindings = Current.Bindings;
            foreach (var row in Rows)
            {
                row.SetBindings(bindings.Where(binding => binding.Action == row.Action));
            }

            OnPropertyChanged(nameof(SelectedMotionTarget));
            OnPropertyChanged(nameof(MouseSensitivity));
            OnPropertyChanged(nameof(MouseAntiDeadzone));
            OnPropertyChanged(nameof(InvertMouseY));
        }
        finally
        {
            loading = false;
        }
    }

    public void Dispose()
    {
        disposed = true;
        captureTimer.Stop();
        captureTimer.Tick -= OnCaptureTick;
        if (catalog is not null) { catalog.Updated -= OnDevicesChanged; }
        store.Changed -= OnStoreChanged;
    }
}

/// <summary>One controller control in the keyboard &amp; mouse editor, with its bindings.</summary>
public sealed class KeyboardMouseActionRow : ViewModelBase
{
    private readonly KeyboardMouseMapEditorViewModel owner;
    private bool isCapturing;

    internal KeyboardMouseActionRow(KeyboardMouseMapEditorViewModel owner, GamepadAction action)
    {
        this.owner = owner;
        Action = action;
        AddCommand = new RelayCommand(() => owner.BeginCapture(this));
        ClearCommand = new RelayCommand(() => owner.ClearAction(Action), () => Bindings.Count > 0);
    }

    public GamepadAction Action { get; }

    public string Label => UiTextResources.EnumValue(Action);

    public ObservableCollection<KeyboardMouseBindingChip> Bindings { get; } = [];

    public bool HasBindings => Bindings.Count > 0;

    public bool IsCapturing => isCapturing;

    public string AddLabel => UiTextResources.Get(isCapturing ? "BindingListening" : "BindingAdd");

    public ICommand AddCommand { get; }

    public RelayCommand ClearCommand { get; }

    internal void SetBindings(IEnumerable<KeyboardMouseBinding> bindings)
    {
        var next = bindings.ToList();
        if (next.SequenceEqual(Bindings.Select(chip => chip.Binding)))
        {
            return;
        }

        Bindings.Clear();
        foreach (var binding in next)
        {
            Bindings.Add(new KeyboardMouseBindingChip(binding, () => owner.RemoveBinding(binding)));
        }

        OnPropertyChanged(nameof(HasBindings));
        ClearCommand.NotifyCanExecuteChanged();
    }

    internal void RaiseCaptureChanged(bool capturing)
    {
        if (isCapturing == capturing)
        {
            return;
        }

        isCapturing = capturing;
        OnPropertyChanged(nameof(IsCapturing));
        OnPropertyChanged(nameof(AddLabel));
    }
}

/// <summary>One bound key or mouse input, shown as a removable chip.</summary>
public sealed class KeyboardMouseBindingChip(KeyboardMouseBinding binding, Action remove) : ViewModelBase
{
    public KeyboardMouseBinding Binding { get; } = binding;

    public string Label => Binding.IsKey ? VirtualKeyNames.GetName(Binding.Key) : UiTextResources.EnumValue(Binding.Mouse);

    public string Icon => Binding.IsMouse ? "🖱" : "⌨";

    public ICommand RemoveCommand { get; } = new RelayCommand(remove);
}

/// <summary>One choice of where mouse movement goes.</summary>
public sealed class MotionTargetOption(MouseMotionTarget target) : ViewModelBase
{
    public MouseMotionTarget Target { get; } = target;
    public string Label => UiTextResources.Get(Target switch
    {
        MouseMotionTarget.LeftStick => "InputMapLeftStick",
        MouseMotionTarget.RightStick => "InputMapRightStick", _ => "InputMapNothing"
    });
}

public sealed class MapDeviceOption(string? id, string name, GameFlow.Infrastructure.Runtime.DeviceCategory? category) : ViewModelBase
{
    public string? Id { get; } = id;
    public string Name { get; } = name;
    public GameFlow.Infrastructure.Runtime.DeviceCategory? Category { get; } = category;
    public string Label => Id is null ? UiTextResources.Get("InputMapDefaults") : Name;
}
