using System.Runtime.CompilerServices;
using System.Globalization;
using Autofire.Infrastructure.Runtime;
using Autofire.Infrastructure.Runtime.Templates;

namespace Autofire.App.ViewModels;

/// <summary>Output-kind option for the template editor's combo box.</summary>
public sealed record OutputKindOption(VirtualControllerKind Kind, string Label);

/// <summary>
/// Edits the <see cref="DeviceOutputTemplate"/> for the device currently
/// selected in the Devices view. Loads a detached copy from the
/// <see cref="DeviceTemplateStore"/>, binds the UI to it, and saves on
/// every change. Output kinds gate which sections apply: lighting for
/// the DualShock family, adaptive triggers for DualSense, axis/button
/// counts for the generic DirectInput device.
/// </summary>
public sealed class DeviceTemplateEditorViewModel : ViewModelBase
{
    private readonly DeviceTemplateStore store;

    private DeviceOutputTemplate? template;
    private bool loading;
    private Action<DeviceOutputTemplate>? externalSaver;

    public DeviceTemplateEditorViewModel(
        DeviceTemplateStore store,
        Autofire.Infrastructure.Localization.ILocalizationService localization)
    {
        this.localization = localization ?? throw new ArgumentNullException(nameof(localization));
        localization.CultureChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(EmitLabel));
            OnPropertyChanged(nameof(EmitTooltip));
            OnPropertyChanged(nameof(OutputDeviceLabel));
        };
        this.store = store ?? throw new ArgumentNullException(nameof(store));

        OutputKindOptions =
        [
            new OutputKindOption(VirtualControllerKind.Xbox360, "Xbox 360"),
            new OutputKindOption(VirtualControllerKind.DualShock4, "DualShock 4"),
            new OutputKindOption(VirtualControllerKind.DualSense, "DualSense"),
            new OutputKindOption(VirtualControllerKind.GenericDirectInput, "Generic (DirectInput)"),
        ];
    }

    public IReadOnlyList<OutputKindOption> OutputKindOptions { get; }

    /// <summary>True when a device that supports an output template is loaded.</summary>
    private readonly Autofire.Infrastructure.Localization.ILocalizationService localization;

    public bool HasTemplate => template is not null;

    // ─── Localized labels ───
    public string EmitLabel        => localization["TemplateEmitLabel"];
    public string EmitTooltip      => localization["TemplateEmitTooltip"];
    public string OutputDeviceLabel => localization["TemplateOutputDeviceLabel"];

    /// <summary>
    /// Loads the template for the given device. Templates apply to
    /// gamepads and joysticks (devices that can drive a virtual
    /// controller); keyboards/mice clear the editor.
    /// </summary>
    public void LoadFor(string? deviceId, DeviceCategory category)
    {
        var supported = !string.IsNullOrWhiteSpace(deviceId)
            && (category == DeviceCategory.Gamepad || category == DeviceCategory.Joystick);

        loading = true;
        try
        {
            externalSaver = null;
            template = supported ? store.GetOrCreate(deviceId!) : null;
            RaiseAll();
        }
        finally
        {
            loading = false;
        }
    }

    /// <summary>
    /// Loads an explicit template (slot mode). Edits are persisted through
    /// <paramref name="saver"/> instead of the per-device store — used by
    /// the slot editor, which saves into the slot registry.
    /// </summary>
    public void LoadTemplate(DeviceOutputTemplate template, Action<DeviceOutputTemplate> saver)
    {
        loading = true;
        try
        {
            externalSaver = saver;
            this.template = template?.Clone();
            RaiseAll();
        }
        finally
        {
            loading = false;
        }
    }

    /// <summary>Clears the editor (no slot/device selected).</summary>
    public void Clear()
    {
        loading = true;
        try
        {
            externalSaver = null;
            template = null;
            RaiseAll();
        }
        finally
        {
            loading = false;
        }
    }

    private void Commit()
    {
        if (loading || template is null)
        {
            return;
        }
        if (externalSaver is not null)
        {
            externalSaver(template);
        }
        else
        {
            store.Save(template);
        }
    }

    // ── Enable ──
    public bool Enabled
    {
        get => template?.Enabled ?? false;
        set => SetField(t => t.Enabled = value, template?.Enabled, value);
    }

    // ── Output kind ──
    public OutputKindOption? SelectedOutputKind
    {
        get => OutputKindOptions.FirstOrDefault(o => o.Kind == (template?.OutputKind ?? VirtualControllerKind.Xbox360));
        set
        {
            if (template is null || value is null || template.OutputKind == value.Kind)
            {
                return;
            }
            template.OutputKind = value.Kind;
            Commit();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowGenericShape));
        }
    }

    public bool ShowGenericShape => template?.OutputKind == VirtualControllerKind.GenericDirectInput;

    // ── Generic device shape ──
    public int ThumbstickCount
    {
        get => template?.ThumbstickCount ?? 0;
        set => SetField(t => t.ThumbstickCount = Math.Clamp(value, 0, 4), template?.ThumbstickCount, value);
    }

    public int TriggerCount
    {
        get => template?.TriggerCount ?? 0;
        set => SetField(t => t.TriggerCount = Math.Clamp(value, 0, 8), template?.TriggerCount, value);
    }

    public int ButtonCount
    {
        get => template?.ButtonCount ?? 0;
        set => SetField(t => t.ButtonCount = Math.Clamp(value, 0, 128), template?.ButtonCount, value);
    }

    public int PovCount
    {
        get => template?.PovCount ?? 0;
        set => SetField(t => t.PovCount = Math.Clamp(value, 0, 4), template?.PovCount, value);
    }

    public string ProductString
    {
        get => template?.ProductString ?? string.Empty;
        set => SetField(t => t.ProductString = value ?? string.Empty, template?.ProductString, value ?? string.Empty);
    }

    // ── helpers ──

    /// <summary>
    /// Applies a change to the active template when the value actually
    /// differs, commits it through the active saver, and raises
    /// property-changed for the calling property. No-ops while no
    /// template is loaded.
    /// </summary>
    private void SetField<T>(
        Action<DeviceOutputTemplate> apply,
        T? oldValue,
        T newValue,
        [CallerMemberName] string? propertyName = null)
    {
        if (template is null)
        {
            return;
        }

        if (EqualityComparer<T?>.Default.Equals(oldValue, newValue))
        {
            return;
        }

        apply(template);
        Commit();
        OnPropertyChanged(propertyName);
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(HasTemplate));
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(SelectedOutputKind));
        OnPropertyChanged(nameof(ShowGenericShape));
        OnPropertyChanged(nameof(ThumbstickCount));
        OnPropertyChanged(nameof(TriggerCount));
        OnPropertyChanged(nameof(ButtonCount));
        OnPropertyChanged(nameof(PovCount));
        OnPropertyChanged(nameof(ProductString));
    }
}
