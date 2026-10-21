using System.Runtime.CompilerServices;

// Lets GameFlow.Infrastructure.Tests unit-test internal implementation
// details directly (e.g. the evdev ioctl-number derivation in
// Runtime/Input/Linux/) without widening their real visibility just to
// make them reachable from a separate test assembly.
[assembly: InternalsVisibleTo("GameFlow.Infrastructure.Tests")]
[assembly: InternalsVisibleTo("GameFlow.HidMaestro")]
[assembly: InternalsVisibleTo("GameFlow.HidMaestro.Tests")]
