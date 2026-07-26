using Microsoft.Extensions.Logging;

namespace GameFlow.Infrastructure.Runtime.Input.Linux;

/// <summary>
/// Linux counterpart to Win32MouseOutputWriter — creates one virtual
/// relative-motion mouse via uinput at construction and injects
/// EV_REL/REL_X/REL_Y events (each batch closed with a SYN_REPORT, the
/// same framing every evdev event stream needs) instead of calling
/// SendInput.
///
/// <para>
/// <b>Requires /dev/uinput access</b> — typically root, membership in an
/// "input" or "uinput" group depending on distro udev rules, or
/// CAP_SYS_ADMIN. If the device can't be created, this writer logs once
/// and every <see cref="MoveRelative"/> call becomes a silent no-op —
/// an operational/permissions gap on the machine it's running on, not
/// something to crash over, matching how the rest of this codebase
/// treats a missing capability as a status to report rather than a
/// fatal error.
/// </para>
///
/// <para>
/// Not verified against a live device — see UinputInterop.cs's own
/// note on why (no working uinput driver in this build environment).
/// </para>
/// </summary>
public sealed class LinuxMouseOutputWriter : IMouseOutputWriter, IDisposable
{
    private readonly ILogger<LinuxMouseOutputWriter> logger;
    private readonly int fd;
    private readonly bool available;

    public LinuxMouseOutputWriter(ILogger<LinuxMouseOutputWriter> logger)
    {
        this.logger = logger;
        fd = TryCreateDevice();
        available = fd >= 0;

        if (available)
        {
            logger.LogInformation("uinput: virtual mouse created for touchpad-mouse output.");
        }
        else
        {
            logger.LogWarning(
                "uinput: could not create a virtual mouse (needs /dev/uinput access — often the " +
                "\"input\"/\"uinput\" group, or CAP_SYS_ADMIN). Touchpad mouse mapping will have no effect.");
        }
    }

    private int TryCreateDevice()
    {
        var candidateFd = UinputInterop.OpenWriteOnlyNonBlocking("/dev/uinput");
        if (candidateFd < 0)
        {
            return -1;
        }

        if (UinputInterop.ioctl_intarg(candidateFd, UinputInterop.UI_SET_EVBIT, EvdevInterop.EV_REL) < 0
            || UinputInterop.ioctl_intarg(candidateFd, UinputInterop.UI_SET_RELBIT, EvdevInterop.REL_X) < 0
            || UinputInterop.ioctl_intarg(candidateFd, UinputInterop.UI_SET_RELBIT, EvdevInterop.REL_Y) < 0)
        {
            return Fail(candidateFd, "enabling EV_REL/REL_X/REL_Y capabilities");
        }

        var setup = new UinputInterop.UinputSetup
        {
            Id = new UinputInterop.InputId { BusType = UinputInterop.BusVirtual, Vendor = 0, Product = 0, Version = 1 },
            Name = UinputInterop.BuildFixedName("GameFlow Virtual Mouse"),
            FfEffectsMax = 0
        };

        if (UinputInterop.ioctl_uinputsetup(candidateFd, UinputInterop.UI_DEV_SETUP, ref setup) < 0)
        {
            return Fail(candidateFd, "UI_DEV_SETUP");
        }
        if (UinputInterop.ioctl_noarg(candidateFd, UinputInterop.UI_DEV_CREATE) < 0)
        {
            return Fail(candidateFd, "UI_DEV_CREATE");
        }

        return candidateFd;
    }

    private int Fail(int fdToClose, string step)
    {
        logger.LogDebug("uinput: {Step} failed while creating the virtual mouse.", step);
        _ = EvdevInterop.close(fdToClose);
        return -1;
    }

    public void MoveRelative(float dx, float dy)
    {
        if (!available)
        {
            return;
        }

        var roundedDx = (int)MathF.Round(dx);
        var roundedDy = (int)MathF.Round(dy);
        if (roundedDx == 0 && roundedDy == 0)
        {
            return;
        }

        WriteEvent(EvdevInterop.EV_REL, EvdevInterop.REL_X, roundedDx);
        WriteEvent(EvdevInterop.EV_REL, EvdevInterop.REL_Y, roundedDy);
        // SYN_REPORT closes the batch — without it, consumers reading
        // this device may never see the X/Y moves as a completed frame.
        WriteEvent(EvdevInterop.EV_SYN, (ushort)UinputInterop.SYN_REPORT, 0);
    }

    private void WriteEvent(ushort type, ushort code, int value)
    {
        var ev = new EvdevInterop.InputEvent { Type = type, Code = code, Value = value };
        _ = UinputInterop.write(fd, ref ev, (nuint)EvdevInterop.InputEventSize);
        // Return value deliberately not checked — same reasoning as
        // Win32MouseOutputWriter: a dropped mouse-move frame at 60-1000 Hz
        // is forgettable, not worth failing the pipeline tick over.
    }

    public void Dispose()
    {
        if (!available)
        {
            return;
        }
        _ = UinputInterop.ioctl_noarg(fd, UinputInterop.UI_DEV_DESTROY);
        _ = EvdevInterop.close(fd);
    }
}
