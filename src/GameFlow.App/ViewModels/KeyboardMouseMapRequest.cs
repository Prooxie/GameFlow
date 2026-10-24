using GameFlow.Infrastructure.Runtime.Input;

namespace GameFlow.App.ViewModels;

public sealed record KeyboardMouseMapRequest(string? DeviceId, int Key = 0, MouseInput Mouse = MouseInput.None);
