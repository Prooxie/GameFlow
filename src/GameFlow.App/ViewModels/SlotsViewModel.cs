using System.Collections.ObjectModel;
using System.Windows.Input;
using GameFlow.Infrastructure.Runtime;
using GameFlow.Infrastructure.Runtime.Slots;
using GameFlow.Infrastructure.Runtime.Templates;
using GameFlow.Infrastructure.Profiles;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;

namespace GameFlow.App.ViewModels;

/// <summary>Row in the slots list (display only).</summary>
public sealed class SlotRowViewModel : ViewModelBase
{
    public SlotRowViewModel(ControllerSlot slot)
    {
        Id = slot.Id;
        Apply(slot);
    }

    public string Id { get; }

    private string name = string.Empty;
    public string Name { get => name; private set => SetProperty(ref name, value); }

    private int index;
    public int Index { get => index; private set => SetProperty(ref index, value); }

    private string kindLabel = string.Empty;
    public string KindLabel { get => kindLabel; private set => SetProperty(ref kindLabel, value); }

    private bool enabled;
    public bool Enabled { get => enabled; private set => SetProperty(ref enabled, value); }

    private string deviceSummary = string.Empty;
    public string DeviceSummary { get => deviceSummary; private set => SetProperty(ref deviceSummary, value); }

    public void Apply(ControllerSlot slot)
    {
        Name = slot.Name;
        Index = slot.Index;
        KindLabel = SlotsViewModel.KindLabelFor(slot.OutputTemplate.OutputKind);
        Enabled = slot.Enabled;
        int count = slot.InputDeviceIds.Count;
        DeviceSummary = count == 0 ? "No devices assigned" : count == 1 ? "1 device" : $"{count} devices";
    }
}

/// <summary>A device that can be assigned/unassigned to the selected slot.</summary>
public sealed class AssignableDeviceRow(string id, string displayName)
{
    public string Id { get; } = id;
    public string DisplayName { get; } = displayName;
}

public sealed class SlotsViewModel : ViewModelBase, IDisposable
{
    private readonly SlotRegistry registry;
    private readonly InputDeviceCatalog catalog;
    private readonly ProfileSession profileSession;

    private bool loadingDetail;
    private bool rebuildQueued;
    private bool disposed;

    public SlotsViewModel(SlotRegistry registry, InputDeviceCatalog catalog, DeviceTemplateStore templateStore, ProfileSession profileSession, GameFlow.Infrastructure.Localization.ILocalizationService localization)
    {
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.profileSession = profileSession ?? throw new ArgumentNullException(nameof(profileSession));
        TemplateEditor = new DeviceTemplateEditorViewModel(templateStore ?? throw new ArgumentNullException(nameof(templateStore)), localization);

        OutputKindOptions =
        [
            new OutputKindOption(VirtualControllerKind.Xbox360, "Xbox 360"),
            new OutputKindOption(VirtualControllerKind.DualShock4, "DualShock 4"),
            new OutputKindOption(VirtualControllerKind.DualSense, "DualSense"),
            new OutputKindOption(VirtualControllerKind.GenericDirectInput, "Generic (DirectInput)"),
        ];
        newSlotKind = OutputKindOptions[0];

        CreateSlotCommand = new RelayCommand(CreateSlot, () => registry.CanCreate);
        DuplicateSlotCommand = new RelayCommand(DuplicateSlot, () => SelectedSlot is not null && registry.CanCreate);
        SaveSlotCommand = new RelayCommand(SaveSlot, () => SelectedSlot is not null);
        DeleteSlotCommand = new RelayCommand(DeleteSlot, () => SelectedSlot is not null);
        AssignDeviceCommand = new RelayCommand<string>(AssignDevice);
        UnassignDeviceCommand = new RelayCommand<string>(UnassignDevice);
        AddProfileCommand = new RelayCommand(AddProfile, () => SelectedSlot is not null && SelectedAvailableProfile is not null);
        RemoveProfileCommand = new RelayCommand<string>(RemoveProfile);
        LoadAvailableProfilesAsync();

        registry.SlotsChanged += OnSlotsChanged;
        catalog.Updated += OnCatalogUpdated;
        this.localization = localization;
        localization.CultureChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(VirtualControllersHeader));
            OnPropertyChanged(nameof(AddControllerLabel));
            OnPropertyChanged(nameof(SlotEnabledLabel));
            OnPropertyChanged(nameof(SlotDuplicateLabel));
            OnPropertyChanged(nameof(SlotSaveLabel));
            OnPropertyChanged(nameof(SlotDeleteLabel));
        };

        Rebuild();
    }

    private readonly GameFlow.Infrastructure.Localization.ILocalizationService localization;

    public string VirtualControllersHeader =>
        Loc("SidebarVirtualHeader", "VIRTUAL CONTROLLERS");
    public string AddControllerLabel =>
        Loc("SlotsAddControllerLabel", "Add controller");
    public string SlotEnabledLabel =>
        Loc("SlotsEnabledLabel", "Enabled");
    public string SlotDuplicateLabel =>
        Loc("SlotsDuplicateLabel", "Duplicate");
    public string SlotSaveLabel =>
        Loc("SlotsSaveLabel", "Save");
    public string SlotDeleteLabel =>
        Loc("SlotsDeleteLabel", "Delete");

    private string Loc(string key, string fallback)
    {
        var value = localization[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    public ObservableCollection<SlotRowViewModel> Slots { get; } = [];
    public ObservableCollection<AssignableDeviceRow> AssignedDevices { get; } = [];
    public ObservableCollection<AssignableDeviceRow> AvailableDevices { get; } = [];

    /// <summary>Profiles layered onto the selected slot, in order.</summary>
    public ObservableCollection<ProfileSummary> AssignedProfiles { get; } = [];

    /// <summary>All profiles available to add as a layer.</summary>
    public ObservableCollection<ProfileSummary> AvailableProfiles { get; } = [];

    public IReadOnlyList<OutputKindOption> OutputKindOptions { get; }
    public DeviceTemplateEditorViewModel TemplateEditor { get; }

    public ICommand CreateSlotCommand { get; }
    public ICommand DuplicateSlotCommand { get; }
    public ICommand SaveSlotCommand { get; }
    public ICommand DeleteSlotCommand { get; }
    public ICommand AssignDeviceCommand { get; }
    public ICommand UnassignDeviceCommand { get; }
    public ICommand AddProfileCommand { get; }
    public ICommand RemoveProfileCommand { get; }

    private ProfileSummary? selectedAvailableProfile;
    public ProfileSummary? SelectedAvailableProfile
    {
        get => selectedAvailableProfile;
        set
        {
            if (SetProperty(ref selectedAvailableProfile, value))
            {
                (AddProfileCommand as RelayCommand)?.NotifyCanExecuteChanged();
            }
        }
    }

    private OutputKindOption newSlotKind;
    public OutputKindOption NewSlotKind
    {
        get => newSlotKind;
        set => SetProperty(ref newSlotKind, value);
    }

    public bool CanCreate => registry.CanCreate;

    private SlotRowViewModel? selectedSlot;
    public SlotRowViewModel? SelectedSlot
    {
        get => selectedSlot;
        set
        {
            if (SetProperty(ref selectedSlot, value))
            {
                LoadDetail();
                OnPropertyChanged(nameof(HasSelectedSlot));
                (DeleteSlotCommand as RelayCommand)?.NotifyCanExecuteChanged();
                (DuplicateSlotCommand as RelayCommand)?.NotifyCanExecuteChanged();
                (SaveSlotCommand as RelayCommand)?.NotifyCanExecuteChanged();
                (AddProfileCommand as RelayCommand)?.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasSelectedSlot => SelectedSlot is not null;

    private string slotName = string.Empty;
    public string SlotName
    {
        get => slotName;
        set
        {
            if (SetProperty(ref slotName, value) && !loadingDetail && SelectedSlot is not null)
            {
                registry.Rename(SelectedSlot.Id, value);
            }
        }
    }

    private bool slotEnabled;
    public bool SlotEnabled
    {
        get => slotEnabled;
        set
        {
            if (SetProperty(ref slotEnabled, value) && !loadingDetail && SelectedSlot is not null)
            {
                registry.SetEnabled(SelectedSlot.Id, value);
            }
        }
    }

    public static string KindLabelFor(VirtualControllerKind kind) => kind switch
    {
        VirtualControllerKind.Xbox360 => "Xbox 360",
        VirtualControllerKind.DualShock4 => "DualShock 4",
        VirtualControllerKind.DualSense => "DualSense",
        VirtualControllerKind.GenericDirectInput => "Generic",
        _ => "Controller",
    };

    private void CreateSlot()
    {
        var created = registry.CreateSlot(NewSlotKind?.Kind ?? VirtualControllerKind.Xbox360);
        if (created is not null)
        {
            // Selection follows after the SlotsChanged-driven rebuild.
            pendingSelectId = created.Id;
        }
    }

    private void DuplicateSlot()
    {
        if (SelectedSlot is null)
        {
            return;
        }
        var created = registry.DuplicateSlot(SelectedSlot.Id);
        if (created is not null)
        {
            pendingSelectId = created.Id;
        }
    }

    /// <summary>
    /// Every field in this editor already persists the instant it changes
    /// (SlotName's setter calls registry.Rename directly, template edits
    /// commit through the same registry, etc.) — there's no pending,
    /// unsaved state to flush. This exists anyway as an explicit,
    /// deliberate confirmation: it re-persists the slot's current values,
    /// which is always safe (idempotent) and gives a concrete moment the
    /// user can point to as "I saved this," rather than trusting silent
    /// auto-save alone.
    /// </summary>
    private void SaveSlot()
    {
        if (SelectedSlot is not null)
        {
            registry.Rename(SelectedSlot.Id, SlotName);
        }
    }

    private void DeleteSlot()
    {
        if (SelectedSlot is not null)
        {
            registry.DeleteSlot(SelectedSlot.Id);
        }
    }

    private void AssignDevice(string? deviceId)
    {
        if (SelectedSlot is not null && !string.IsNullOrWhiteSpace(deviceId))
        {
            registry.AssignDevice(SelectedSlot.Id, deviceId);
        }
    }

    private void UnassignDevice(string? deviceId)
    {
        if (SelectedSlot is not null && !string.IsNullOrWhiteSpace(deviceId))
        {
            registry.UnassignDevice(SelectedSlot.Id, deviceId);
        }
    }

    private async void LoadAvailableProfilesAsync()
    {
        try
        {
            var list = await profileSession.ListProfilesAsync();
            Dispatcher.UIThread.Post(() =>
            {
                AvailableProfiles.Clear();
                foreach (var summary in list)
                {
                    AvailableProfiles.Add(summary);
                }
                // Re-resolve names now that the catalog is loaded.
                if (SelectedSlot is not null)
                {
                    LoadDetail();
                }
            });
        }
        catch
        {
            // Non-fatal: the picker just stays empty.
        }
    }

    private void AddProfile()
    {
        if (SelectedSlot is null || SelectedAvailableProfile is null)
        {
            return;
        }
        var slot = registry.GetSlot(SelectedSlot.Id);
        if (slot is null || slot.ProfileIds.Contains(SelectedAvailableProfile.Id))
        {
            return;
        }
        var ids = new List<string>(slot.ProfileIds) { SelectedAvailableProfile.Id };
        registry.SetProfiles(slot.Id, ids);
        LoadDetail();
    }

    private void RemoveProfile(string? profileId)
    {
        if (SelectedSlot is null || string.IsNullOrWhiteSpace(profileId))
        {
            return;
        }
        var slot = registry.GetSlot(SelectedSlot.Id);
        if (slot is null)
        {
            return;
        }
        var ids = slot.ProfileIds.Where(p => p != profileId).ToList();
        registry.SetProfiles(slot.Id, ids);
        LoadDetail();
    }

    private string? pendingSelectId;

    private void OnSlotsChanged(object? sender, EventArgs e) => QueueRebuild();
    private void OnCatalogUpdated(object? sender, EventArgs e) => QueueRebuild();

    private void QueueRebuild()
    {
        if (rebuildQueued || disposed)
        {
            return;
        }
        rebuildQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            rebuildQueued = false;
            if (!disposed)
            {
                Rebuild();
            }
        });
    }

    private void Rebuild()
    {
        var target = registry.GetSlots();

        // Remove rows whose slot vanished.
        for (int i = Slots.Count - 1; i >= 0; i--)
        {
            if (target.All(s => s.Id != Slots[i].Id))
            {
                Slots.RemoveAt(i);
            }
        }

        // Add new / update existing, in target order.
        for (int idx = 0; idx < target.Count; idx++)
        {
            var slot = target[idx];
            var existing = Slots.FirstOrDefault(r => r.Id == slot.Id);
            if (existing is null)
            {
                Slots.Insert(Math.Min(idx, Slots.Count), new SlotRowViewModel(slot));
            }
            else
            {
                existing.Apply(slot);
                int currentIdx = Slots.IndexOf(existing);
                if (currentIdx != idx && idx < Slots.Count)
                {
                    Slots.Move(currentIdx, idx);
                }
            }
        }

        // Honor a pending selection from CreateSlot.
        if (pendingSelectId is not null)
        {
            var match = Slots.FirstOrDefault(r => r.Id == pendingSelectId);
            pendingSelectId = null;
            if (match is not null)
            {
                SelectedSlot = match;
            }
        }
        else if (SelectedSlot is not null)
        {
            // Selected slot may have changed (device assignment etc.) — refresh detail.
            LoadDetail();
        }

        OnPropertyChanged(nameof(CanCreate));
        (CreateSlotCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private void LoadDetail()
    {
        loadingDetail = true;
        try
        {
            AssignedDevices.Clear();
            AvailableDevices.Clear();
            AssignedProfiles.Clear();

            var slot = SelectedSlot is null ? null : registry.GetSlot(SelectedSlot.Id);
            if (slot is null)
            {
                SlotName = string.Empty;
                SlotEnabled = false;
                TemplateEditor.Clear();
                return;
            }

            SlotName = slot.Name;
            SlotEnabled = slot.Enabled;

            var slotId = slot.Id;
            TemplateEditor.LoadTemplate(slot.OutputTemplate, t => registry.UpdateTemplate(slotId, t));

            // Assigned devices (in slot order), resolving names from the catalog.
            var devices = catalog.Devices;
            foreach (var id in slot.InputDeviceIds)
            {
                var info = devices.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
                AssignedDevices.Add(new AssignableDeviceRow(id, info?.DisplayName ?? id));
            }

            // Available = gamepads/joysticks not already assigned to this slot.
            foreach (var d in devices)
            {
                bool assignable = d.Category is DeviceCategory.Gamepad or DeviceCategory.Joystick or DeviceCategory.Keyboard or DeviceCategory.Mouse;
                if (assignable && !slot.InputDeviceIds.Contains(d.Id))
                {
                    AvailableDevices.Add(new AssignableDeviceRow(d.Id, d.DisplayName));
                }
            }

            // Layered profiles (in order), resolving names from the catalog.
            foreach (var pid in slot.ProfileIds)
            {
                var name = AvailableProfiles.FirstOrDefault(p => p.Id == pid)?.Name ?? pid;
                AssignedProfiles.Add(new ProfileSummary(pid, name));
            }
        }
        finally
        {
            loadingDetail = false;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        registry.SlotsChanged -= OnSlotsChanged;
        catalog.Updated -= OnCatalogUpdated;
    }
}
