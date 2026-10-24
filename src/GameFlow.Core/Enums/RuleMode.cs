namespace GameFlow.Core.Enums;

public enum RuleMode
{
    /// <summary>The rule is skipped entirely: its inputs reach the output untouched and it produces nothing.</summary>
    Passthrough,

    /// <summary>
    /// "Ignore / block": the rule produces nothing AND its source input
    /// is kept from reaching the output. Block means the same thing for
    /// every rule type — the SOURCE is blocked — and it only ever removes
    /// the physical press or deflection; it never cancels a button or
    /// stick another rule produces. Per type, the source is:
    /// <list type="bullet">
    ///   <item>remap, autofire, multi-button autofire, combo, rule toggle: the source button;</item>
    ///   <item>SOCD cleaning: both buttons of the pair;</item>
    ///   <item>stick threshold: the stick it shapes (reads as centred);</item>
    ///   <item>stick autofire: the source stick (the target stick is left alone);</item>
    ///   <item>freeze last direction: the activation button (no freeze happens);</item>
    ///   <item>stick trim: the arm button (the trigger is left alone);</item>
    ///   <item>multi-source row: every source; the target is not written;</item>
    ///   <item>script: the button its control key names, if it names one;</item>
    ///   <item>gyro, touchpad, axis inversion: nothing — their source never reaches the output on its own, so blocking is the same as switching the rule off.</item>
    /// </list>
    /// Before this was settled, "block" meant whatever each pass happened
    /// to do: some zeroed the TARGET stick every tick, and a button block
    /// that ran after a remap erased the remap's output on that button.
    /// </summary>
    DoNothing,

    /// <summary>The rule does its job.</summary>
    Modify
}
