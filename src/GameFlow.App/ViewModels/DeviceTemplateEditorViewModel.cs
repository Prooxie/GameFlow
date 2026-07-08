using System.Runtime.CompilerServices;
using System.Globalization;
using GameFlow.Infrastructure.Runtime;
using GameFlow.Infrastructure.Runtime.Templates;

namespace GameFlow.App.ViewModels;

/// <summary>Output-kind option for the template editor's combo box.</summary>
public sealed record OutputKindOption(VirtualControllerKind Kind, string Label);

/// <summary>
/// Output-provider (backend) option for the template editor's combo box.
/// <see cref="Key"/> is empty for the "inherit from profile" sentinel —
/// the historical behavior for slots saved before per-slot providers
/// existed — and a <see cref="ProviderCatalog"/> key otherwise.
/// </summary>
public sealed record SlotOutputProviderOption(string Key, string Label);

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
        GameFlow.Infrastructure.Localization.ILocalizationService localization)
    {
        this.localization = localization ?? throw new ArgumentNullException(nameof(localization));
        localization.CultureChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(EmitLabel));
            OnPropertyChanged(nameof(EmitTooltip));
            OnPropertyChanged(nameof(OutputDeviceLabel));
            OnPropertyChanged(nameof(OutputProviderLabel));
            OnPropertyChanged(nameof(OutputProviderTooltip));
        };
        this.store = store ?? throw new ArgumentNullException(nameof(store));

        OutputKindOptions =
        [
            new OutputKindOption(VirtualControllerKind.Xbox360, "Xbox 360"),
            new OutputKindOption(VirtualControllerKind.DualShock4, "DualShock 4"),
            new OutputKindOption(VirtualControllerKind.DualSense, "DualSense"),
            new OutputKindOption(VirtualControllerKind.GenericDirectInput, "Generic (DirectInput)"),
        ];

        // Beta scope: only the three ViGEm backends are selectable here.
        // HidMaestro and Preview are both still fully implemented at the
        // sink layer (nothing there changed) — they're just not offered
        // as a choice in this dropdown for now, since both have been
        // recurring sources of "why isn't my output working" confusion
        // (HidMaestro's reflection bridge is inherently fragile without
        // the real SDK to verify against; Preview creates no real device
        // at all, which reads as broken rather than intentional to most
        // users). Dropping the "(inherit from profile)" sentinel too —
        // it could silently resolve to Preview via the profile's own
        // default, which is exactly the confusing-default problem this
        // whole restriction exists to close. To bring either back
        // post-beta, just add its key back to this Where clause; no
        // other change is needed, since SlotRuntime, the factory, and
        // both sinks never stopped supporting them.
        OutputProviderOptions =
        [
            .. ProviderCatalog.KnownProviders
                .Where(p => p.Key is "vigem-xbox360" or "vigem-ds4" or "vigem-ds5")
                .Select(p => new SlotOutputProviderOption(p.Key, p.DisplayName)),
        ];
    }

    public IReadOnlyList<OutputKindOption> OutputKindOptions { get; }
    public IReadOnlyList<SlotOutputProviderOption> OutputProviderOptions { get; }

    /// <summary>True when a device that supports an output template is loaded.</summary>
    private readonly GameFlow.Infrastructure.Localization.ILocalizationService localization;

    public bool HasTemplate => template is not null;

    // ─── Localized labels ───
    public string EmitLabel        => localization["TemplateEmitLabel"];
    public string EmitTooltip      => localization["TemplateEmitTooltip"];
    public string OutputDeviceLabel => localization["TemplateOutputDeviceLabel"];
    public string OutputProviderLabel => localization["TemplateOutputProviderLabel"];
    public string OutputProviderTooltip => localization["TemplateOutputProviderTooltip"];

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

    /// <summary>
    /// The backend this slot actually uses. This is what fixes "I picked
    /// HIDMaestro and nothing happened" — that pick lived only on the
    /// Dashboard's global selector, gated behind its own Apply button,
    /// with no connection to this specific virtual controller. Changing
    /// it here writes straight to this slot's template and commits
    /// immediately, the same as every other field in this editor.
    /// </summary>
    public SlotOutputProviderOption? SelectedOutputProvider
    {
        get => OutputProviderOptions.FirstOrDefault(
            o => string.Equals(o.Key, template?.OutputProvider ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            ?? OutputProviderOptions.FirstOrDefault();
        set
        {
            if (template is null || value is null
                || string.Equals(template.OutputProvider, value.Key, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            template.OutputProvider = value.Key;
            Commit();
            OnPropertyChanged();
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
        OnPropertyChanged(nameof(SelectedOutputProvider));
        OnPropertyChanged(nameof(ShowGenericShape));
        OnPropertyChanged(nameof(ThumbstickCount));
        OnPropertyChanged(nameof(TriggerCount));
        OnPropertyChanged(nameof(ButtonCount));
        OnPropertyChanged(nameof(PovCount));
        OnPropertyChanged(nameof(ProductString));
    }
}
