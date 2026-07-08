using System.Reflection;
using Microsoft.Extensions.Logging;

namespace GameFlow.Infrastructure.Runtime.HidMaestro;

/// <summary>
/// Runtime (reflection-based) bridge to HIDMaestro.Core. The compile-time
/// path (<c>HIDMAESTRO_SDK</c>) is still preferred when the project is
/// built against the SDK, but this loader means a user can simply drop
/// <c>HIDMaestro.Core.dll</c> next to the executable and the real sink
/// activates on the next start — no rebuild, no compile symbol.
///
/// <para>
/// Everything here is defensive and, as of this revision, everything is
/// also LOUD: a prior version tolerated missing members by leaving the
/// corresponding controller field permanently unbound (stick axes that
/// silently never moved) and had no guard around the per-frame submit
/// call (a reflection type mismatch there would throw on every single
/// write, forever). Neither failure mode produced a log line explaining
/// itself. Now: binding is all-or-nothing — <see cref="TryCreateController"/>
/// either returns a controller with every required member correctly
/// wired, or fails immediately with a complete list of what could and
/// couldn't be found, so the exact SDK shape can be nailed down in one
/// look rather than by guessing why input silently doesn't arrive.
/// </para>
/// </summary>
internal static class HidMaestroDynamic
{
    private static readonly object Gate = new();
    private static bool attempted;
    private static bool available;
    private static string status = "Not probed yet.";

    private static object? context;
    private static Type? controllerType;
    private static Type? stateType;
    private static Type? buttonEnumType;
    private static Type? hatEnumType;
    private static MethodInfo? getProfile;          // HMContext.GetProfile(string)
    private static MethodInfo? createController;    // HMContext.CreateController(profile) | (string)
    private static bool createTakesString;
    private static MethodInfo? submitState;         // HMController.<verb>(in HMGamepadState)

    private static readonly string[] CandidateFileNames =
    [
        "HIDMaestro.Core.dll",
        "HidMaestro.Core.dll",
        "hidmaestro.core.dll",
    ];

    // Preferred name fragments for the per-frame state-submit method, in
    // priority order. Disambiguates when HMController exposes more than
    // one single-parameter method that accepts an HMGamepadState (e.g. a
    // real "SubmitState" alongside a "ValidateState" diagnostic helper) —
    // picking arbitrarily in that case could silently bind the wrong one.
    private static readonly string[] SubmitNameHints = ["submit", "send", "write", "update", "push", "set"];

    public static string StatusDescription
    {
        get { lock (Gate) { return status; } }
    }

    public static bool IsAvailable(ILogger logger)
    {
        lock (Gate)
        {
            if (!attempted)
            {
                attempted = true;
                try
                {
                    Probe(logger);
                }
                catch (Exception exception)
                {
                    available = false;
                    status = $"Probe failed: {exception.Message}";
                    logger.LogWarning(exception, "HIDMaestro dynamic probe failed.");
                }
            }
            return available;
        }
    }

    private static void Probe(ILogger logger)
    {
        var baseDirectory = AppContext.BaseDirectory;
        string? path = CandidateFileNames
            .Select(name => Path.Combine(baseDirectory, name))
            .FirstOrDefault(File.Exists);

        if (path is null)
        {
            status = $"HIDMaestro.Core.dll not found next to the executable ({baseDirectory}). " +
                     "Place the SDK assembly there (plus its driver payload) to activate HIDMaestro output. " +
                     "Checked filenames: " + string.Join(", ", CandidateFileNames);
            logger.LogWarning("HIDMaestro dynamic: {Status}", status);
            return;
        }

        var assembly = Assembly.LoadFrom(path);
        Type? Find(string simpleName) =>
            assembly.GetTypes().FirstOrDefault(t => string.Equals(t.Name, simpleName, StringComparison.Ordinal));

        var contextType = Find("HMContext");
        controllerType  = Find("HMController");
        stateType       = Find("HMGamepadState");
        buttonEnumType  = Find("HMButton");
        hatEnumType     = Find("HMHat");

        if (contextType is null || controllerType is null || stateType is null
            || buttonEnumType is null || hatEnumType is null)
        {
            status = "HIDMaestro.Core.dll loaded but expected types are missing " +
                     $"(HMContext:{contextType is not null} HMController:{controllerType is not null} " +
                     $"HMGamepadState:{stateType is not null} HMButton:{buttonEnumType is not null} " +
                     $"HMHat:{hatEnumType is not null}). Exported types: " +
                     string.Join(", ", assembly.GetExportedTypes().Take(24).Select(t => t.Name));
            logger.LogWarning("HIDMaestro dynamic: {Status}", status);
            return;
        }

        context = Activator.CreateInstance(contextType)
            ?? throw new InvalidOperationException("HMContext could not be instantiated.");

        // Bootstrap calls. Any-arity now (a prior version only matched a
        // strictly parameterless overload — if the real InstallDriver
        // takes so much as an optional bool, that silently skipped the
        // ONE call that actually installs the driver, and every
        // subsequent CreateController would fail for a reason that never
        // got logged).
        InvokeBestEffort(contextType, "LoadDefaultProfiles", logger);
        InvokeBestEffort(contextType, "InstallDriver", logger);

        getProfile = contextType.GetMethod("GetProfile", [typeof(string)]);
        createController = contextType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == "CreateController" && m.GetParameters().Length == 1);
        createTakesString = createController?.GetParameters()[0].ParameterType == typeof(string);

        submitState = ResolveSubmitMethod(controllerType, stateType, logger);

        if (createController is null || submitState is null || (getProfile is null && !createTakesString))
        {
            status = "HIDMaestro.Core API mismatch — could not bind " +
                     $"(GetProfile:{getProfile is not null} CreateController:{createController is not null} " +
                     $"SubmitState:{submitState is not null}). HMController members: " +
                     string.Join(", ", controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                         .Select(m => m.Name).Distinct().Take(24)) +
                     " | HMContext members: " +
                     string.Join(", ", contextType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                         .Select(m => m.Name).Distinct().Take(24));
            logger.LogWarning("HIDMaestro dynamic: {Status}", status);
            return;
        }

        available = true;
        status = $"Active (dynamic) — loaded {Path.GetFileName(path)}, submit method '{submitState.Name}'.";
        logger.LogInformation("HIDMaestro dynamic bridge ready: {Path} (submit='{Submit}')", path, submitState.Name);
    }

    /// <summary>
    /// Finds HMController's per-frame state-submit method. Multiple
    /// single-parameter methods accepting HMGamepadState are possible
    /// (a real submit alongside e.g. a validation helper); name hints
    /// disambiguate, falling back to the first match with a logged
    /// warning so a wrong pick is at least visible, not silent.
    /// </summary>
    private static MethodInfo? ResolveSubmitMethod(Type controllerType, Type stateType, ILogger logger)
    {
        var candidates = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetParameters().Length == 1
                && m.GetParameters()[0].ParameterType.GetElementTypeOrSelf() == stateType)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }
        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        foreach (var hint in SubmitNameHints)
        {
            var match = candidates.FirstOrDefault(m => m.Name.Contains(hint, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        logger.LogWarning(
            "HIDMaestro dynamic: {Count} candidate submit methods found ({Names}) and none matched a known verb — " +
            "picking '{Picked}'. If input doesn't arrive, this is the first thing to check.",
            candidates.Count, string.Join(", ", candidates.Select(c => c.Name)), candidates[0].Name);
        return candidates[0];
    }

    /// <summary>
    /// Invokes a method by name if it exists, tolerating any parameter
    /// count: required parameters get a reasonable default (0/false/null)
    /// rather than causing the lookup to skip the method entirely. Logs
    /// exactly what happened — found-and-invoked, found-but-failed, or
    /// not-found — so a silently-skipped bootstrap step (the earlier bug)
    /// can't happen again without at least one log line about it.
    /// </summary>
    private static void InvokeBestEffort(Type type, string methodName, ILogger logger)
    {
        var method = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal))
            .OrderBy(m => m.GetParameters().Length)
            .FirstOrDefault();

        if (method is null)
        {
            logger.LogDebug("HIDMaestro dynamic: {Method}() not found on {Type} — skipped.", methodName, type.Name);
            return;
        }

        try
        {
            var args = method.GetParameters()
                .Select(p => p.HasDefaultValue ? p.DefaultValue : DefaultFor(p.ParameterType))
                .ToArray();
            _ = method.Invoke(context, args);
            logger.LogDebug("HIDMaestro dynamic: {Method}({Arity} args) invoked.", methodName, args.Length);
        }
        catch (Exception exception)
        {
            var inner = (exception as TargetInvocationException)?.InnerException ?? exception;
            // First run of InstallDriver commonly needs elevation; a
            // failure here is expected in that case and CreateController
            // will fail with a clearer message later if it's fatal.
            logger.LogWarning(inner, "HIDMaestro dynamic: {Method}() threw — continuing.", methodName);
        }
    }

    private static object? DefaultFor(Type type) =>
        type.IsValueType ? Activator.CreateInstance(type) : null;

    private static Type GetElementTypeOrSelf(this Type type) =>
        type.IsByRef ? type.GetElementType()! : type;

    /// <summary>Creates a virtual controller for the given HIDMaestro profile id.</summary>
    public static DynamicHidMaestroController? TryCreateController(
        string profileId, ILogger logger, out string? failure)
    {
        failure = null;
        lock (Gate)
        {
            if (!available || context is null || createController is null
                || stateType is null || buttonEnumType is null || hatEnumType is null || submitState is null)
            {
                failure = status;
                return null;
            }

            try
            {
                object? controller;
                if (createTakesString)
                {
                    controller = createController.Invoke(context, [profileId]);
                }
                else
                {
                    var profile = getProfile!.Invoke(context, [profileId])
                        ?? throw new InvalidOperationException($"Profile '{profileId}' not found.");
                    controller = createController.Invoke(context, [profile]);
                }

                if (controller is null)
                {
                    failure = $"CreateController('{profileId}') returned null.";
                    return null;
                }

                // All-or-nothing binding: DynamicHidMaestroController's
                // constructor throws (with a full member dump) if any
                // required field/method can't be found, rather than
                // constructing a partially-wired controller that looks
                // active but silently drops half its input.
                return new DynamicHidMaestroController(
                    controller, stateType, buttonEnumType, hatEnumType, submitState, logger);
            }
            catch (TargetInvocationException exception)
            {
                failure = exception.InnerException?.Message ?? exception.Message;
                logger.LogError(exception.InnerException ?? exception,
                    "HIDMaestro controller creation failed for profile {Profile}.", profileId);
                return null;
            }
            catch (Exception exception)
            {
                failure = exception.Message;
                logger.LogError(exception,
                    "HIDMaestro controller creation failed for profile {Profile}.", profileId);
                return null;
            }
        }
    }
}

/// <summary>
/// A live HIDMaestro virtual controller driven through reflection. Every
/// numeric axis, the button flags, and the hat are bound at construction
/// time and are ALL mandatory — if the real SDK uses different field
/// names than expected, construction throws immediately with the full
/// list of what bound and what didn't, instead of quietly running with
/// dead stick axes. Field values are coerced to the target field's actual
/// numeric type (float vs double) so a type mismatch there can't throw
/// on every single frame.
/// </summary>
internal sealed class DynamicHidMaestroController : IDisposable
{
    private readonly object controller;
    private readonly MethodInfo submitState;
    private readonly ILogger logger;
    private readonly object boxedState;
    private readonly object?[] submitArgs;

    private readonly Setter setLeftX, setLeftY, setRightX, setRightY, setLeftTrigger, setRightTrigger;
    private readonly Setter setButtons, setHat;
    private readonly Type buttonEnumType;
    private readonly Type hatEnumType;
    private readonly Dictionary<string, ulong> buttonValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> missingButtons = new(StringComparer.OrdinalIgnoreCase);
    private bool disposed;
    private int consecutiveSubmitFailures;
    private const int FailureGiveUpThreshold = 300; // ~1-3s at typical tick rates

    /// <summary>
    /// False once submits have failed enough consecutive times in a row
    /// that continuing to retry is pointless (a persistent reflection
    /// mismatch won't fix itself frame-to-frame). The owning sink should
    /// stop calling <see cref="Submit"/> once this goes false and treat
    /// HIDMaestro as unavailable for the rest of this configuration,
    /// rather than spending a reflection Invoke every frame forever on a
    /// call that's already proven itself broken.
    /// </summary>
    public bool IsHealthy => consecutiveSubmitFailures < FailureGiveUpThreshold;

    /// <summary>A bound field/property setter that also knows the target's real numeric type, for safe reflection coercion.</summary>
    private readonly record struct Setter(MemberInfo Member, Type TargetType, Action<object, object?> Apply);

    public DynamicHidMaestroController(
        object controller, Type stateType, Type buttonEnumType, Type hatEnumType,
        MethodInfo submitState, ILogger logger)
    {
        this.controller = controller;
        this.submitState = submitState;
        this.logger = logger;
        this.buttonEnumType = buttonEnumType;
        this.hatEnumType = hatEnumType;

        boxedState = Activator.CreateInstance(stateType)
            ?? throw new InvalidOperationException("HMGamepadState could not be instantiated.");
        submitArgs = [boxedState];

        Setter? Bind(string name)
        {
            var field = stateType.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field is not null)
            {
                return new Setter(field, field.FieldType, field.SetValue);
            }
            var property = stateType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property is not null && property.CanWrite)
            {
                return new Setter(property, property.PropertyType, property.SetValue);
            }
            return null;
        }

        var leftX  = Bind("LeftStickX");
        var leftY  = Bind("LeftStickY");
        var rightX = Bind("RightStickX");
        var rightY = Bind("RightStickY");
        var lt     = Bind("LeftTrigger");
        var rt     = Bind("RightTrigger");
        var buttons = Bind("Buttons");
        var hat     = Bind("Hat");

        var missing = new List<string>();
        if (leftX  is null) missing.Add("LeftStickX");
        if (leftY  is null) missing.Add("LeftStickY");
        if (rightX is null) missing.Add("RightStickX");
        if (rightY is null) missing.Add("RightStickY");
        if (lt     is null) missing.Add("LeftTrigger");
        if (rt     is null) missing.Add("RightTrigger");
        if (buttons is null) missing.Add("Buttons");
        if (hat     is null) missing.Add("Hat");

        if (missing.Count > 0)
        {
            var available = stateType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Select(f => f.Name)
                .Concat(stateType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanWrite).Select(p => p.Name))
                .Distinct();
            throw new InvalidOperationException(
                $"HMGamepadState is missing expected member(s): {string.Join(", ", missing)}. " +
                $"Binding is all-or-nothing so a real controller never silently runs with dead axes. " +
                $"Writable members actually found on {stateType.Name}: {string.Join(", ", available)}");
        }

        setLeftX = leftX!.Value; setLeftY = leftY!.Value;
        setRightX = rightX!.Value; setRightY = rightY!.Value;
        setLeftTrigger = lt!.Value; setRightTrigger = rt!.Value;
        setButtons = buttons!.Value; setHat = hat!.Value;

        foreach (var name in Enum.GetNames(buttonEnumType))
        {
            buttonValues[name] = Convert.ToUInt64(Enum.Parse(buttonEnumType, name));
        }
    }

    /// <summary>
    /// Submits one input frame. Sticks in [-1,1], triggers in [0,1].
    /// Returns false if the underlying reflected call failed — callers
    /// should count consecutive failures and stop calling after a few,
    /// rather than eating the same exception every frame forever.
    /// </summary>
    public bool Submit(
        float lx, float ly, float rx, float ry, float lt, float rt,
        IReadOnlyList<(string ButtonName, bool Down)> buttons, string hatName)
    {
        if (disposed)
        {
            return false;
        }

        try
        {
            SetNumeric(setLeftX, lx);
            SetNumeric(setLeftY, ly);
            SetNumeric(setRightX, rx);
            SetNumeric(setRightY, ry);
            SetNumeric(setLeftTrigger, lt);
            SetNumeric(setRightTrigger, rt);

            ulong mask = 0;
            foreach (var (name, down) in buttons)
            {
                if (!down)
                {
                    continue;
                }
                if (buttonValues.TryGetValue(name, out var value))
                {
                    mask |= value;
                }
                else if (missingButtons.Add(name))
                {
                    logger.LogWarning(
                        "HIDMaestro dynamic: HMButton has no member named '{Name}' — mapping skipped. Available: {Members}",
                        name, string.Join(", ", buttonValues.Keys));
                }
            }
            setButtons.Apply(boxedState, Enum.ToObject(buttonEnumType, mask));

            object hat;
            try { hat = Enum.Parse(hatEnumType, hatName, ignoreCase: true); }
            catch { hat = Enum.ToObject(hatEnumType, 0); }
            setHat.Apply(boxedState, hat);

            _ = submitState.Invoke(controller, submitArgs);

            if (consecutiveSubmitFailures > 0)
            {
                logger.LogInformation("HIDMaestro dynamic: submit recovered after {Count} failed frame(s).", consecutiveSubmitFailures);
                consecutiveSubmitFailures = 0;
            }
            return true;
        }
        catch (Exception exception)
        {
            consecutiveSubmitFailures++;
            var inner = (exception as TargetInvocationException)?.InnerException ?? exception;
            if (consecutiveSubmitFailures is 1 or 30 or 100)
            {
                // Rate-limited: log on the 1st/30th/100th consecutive
                // failure rather than every single frame (this loop can
                // run at 100–250 Hz).
                logger.LogError(inner,
                    "HIDMaestro dynamic: submit failed ({Count} consecutive). Method='{Method}'.",
                    consecutiveSubmitFailures, submitState.Name);
            }
            if (consecutiveSubmitFailures == FailureGiveUpThreshold)
            {
                logger.LogError(
                    "HIDMaestro dynamic: submit has failed {Count} consecutive times — giving up on this " +
                    "controller instance for good rather than retrying forever. Last error: {Error}",
                    consecutiveSubmitFailures, inner.Message);
            }
            return false;
        }
    }

    /// <summary>Coerces the value to whatever numeric type the target field/property actually declares (float vs double are both common in third-party SDKs; reflection does not implicitly widen a boxed value).</summary>
    private void SetNumeric(Setter setter, float value)
    {
        object converted = setter.TargetType == typeof(float) ? value
            : setter.TargetType == typeof(double) ? (double)value
            : setter.TargetType == typeof(decimal) ? (decimal)value
            : Convert.ChangeType(value, setter.TargetType);
        setter.Apply(boxedState, converted);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        (controller as IDisposable)?.Dispose();
    }
}
