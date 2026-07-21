using PalOps.Web.Maintenance;

VerifyCrashGuardThreshold();
VerifyCrashGuardResetWindow();
VerifyCrashGuardDisabledStatus();
VerifyPlanValidation();
VerifySafetyConfirmations();
VerifyActivityGate();
Console.WriteLine("PalOps maintenance verifier passed.");

static void VerifyCrashGuardThreshold()
{
    var now = DateTimeOffset.Parse("2026-07-20T10:00:00Z");
    var configuration = Configuration(enabled: true, maximumCrashes: 3, windowMinutes: 10);
    var events = new[]
    {
        Crash(now.AddMinutes(-8)),
        Crash(now.AddMinutes(-3)),
        Crash(now.AddSeconds(-15)),
    };
    var result = new CrashGuardEvaluator().Evaluate(configuration, CrashGuardState.Default(), events, now);
    Assert(result.CrashesInWindow == 3, "three recent crashes must be counted");
    Assert(result.ThresholdReached, "the third crash in the window must open the circuit");
    Assert(result.Status == "circuit-open", "threshold state must be reported as circuit-open");
}

static void VerifyCrashGuardResetWindow()
{
    var now = DateTimeOffset.Parse("2026-07-20T10:00:00Z");
    var state = CrashGuardState.Default() with { LastResetAt = now.AddMinutes(-2) };
    var events = new[]
    {
        Crash(now.AddMinutes(-8)),
        Crash(now.AddMinutes(-1)),
    };
    var result = new CrashGuardEvaluator().Evaluate(Configuration(true, 2, 10), state, events, now);
    Assert(result.CrashesInWindow == 1, "crashes before a manual reset must not count toward the new window");
    Assert(!result.ThresholdReached, "reset must clear the effective crash count");
}

static void VerifyCrashGuardDisabledStatus()
{
    var now = DateTimeOffset.Parse("2026-07-20T10:00:00Z");
    var result = new CrashGuardEvaluator().Evaluate(
        Configuration(false, 2, 10),
        CrashGuardState.Default(),
        new[] { Crash(now.AddMinutes(-1)), Crash(now) },
        now);
    Assert(result.Status == "disabled", "disabled Crash Guard must remain disabled even when historical crashes reach the threshold");
}

static void VerifyPlanValidation()
{
    var validator = new MaintenanceValidator();
    var now = DateTimeOffset.Parse("2026-07-20T10:00:00Z");
    var valid = validator.Create(Plan(), null, null, "admin", now);
    Assert(valid.Name == "Routine maintenance", "valid plan must be created");
    Assert(valid.NextRunAt is null, "manual plans must not receive a scheduled run time");

    ExpectArgument(
        () => validator.Create(Plan() with { AnnouncementMessage = "Maintenance soon" }, null, null, "admin", now),
        "announcement without {seconds} must be rejected");
    ExpectArgument(
        () => validator.Create(Plan() with { StopServer = false, StartServer = true }, null, null, "admin", now),
        "starting a server without the controlled stop step must be rejected");
    ExpectArgument(
        () => validator.Create(Plan() with { ScriptEnabled = true, ScriptPath = @"\\server\share\maintenance.cmd" }, null, null, "admin", now),
        "network-hosted maintenance scripts must be rejected");
    ExpectArgument(
        () => validator.Create(Plan() with { ScheduleType = "interval", ScheduleExpression = "00:15:00" }, null, null, "admin", now),
        "maintenance schedules must reject hidden interval mode");
}

static void VerifySafetyConfirmations()
{
    MaintenanceValidator.RequireExecutionConfirmation("RUN MAINTENANCE");
    MaintenanceValidator.RequireResetConfirmation("RESET CRASH GUARD");
    ExpectArgument(() => MaintenanceValidator.RequireExecutionConfirmation("run maintenance"), "maintenance confirmation is case-sensitive");
    ExpectArgument(() => MaintenanceValidator.RequireResetConfirmation("RESET"), "circuit reset requires the full acknowledgement");
    Assert(MaintenanceValidator.ValidateReason("root cause checked") == "root cause checked", "valid audit reason must round-trip");
}

static void VerifyActivityGate()
{
    var gate = new MaintenanceActivityGate();
    using var first = gate.TryAcquire("maintenance");
    Assert(first is not null && gate.IsBusy, "first maintenance activity must acquire the gate");
    Assert(gate.TryAcquire("crash-guard") is null, "a second activity must not overlap");
    first!.Dispose();
    using var second = gate.TryAcquire("crash-guard");
    Assert(second is not null, "the gate must become available after the first activity completes");
}

static CrashGuardConfiguration Configuration(bool enabled, int maximumCrashes, int windowMinutes) => new(
    enabled,
    maximumCrashes,
    windowMinutes,
    15,
    180,
    true,
    DateTimeOffset.Parse("2026-07-20T00:00:00Z"),
    "test");

static MaintenanceCrashEvent Crash(DateTimeOffset at) => new(
    Guid.NewGuid().ToString("N"),
    "unexpected-exit",
    at,
    1234,
    "detected",
    "test crash",
    null,
    "PALSERVER_EXITED_UNEXPECTEDLY");

static MaintenancePlanWriteRequest Plan() => new(
    "Routine maintenance",
    false,
    "manual",
    string.Empty,
    true,
    60,
    "Maintenance begins in {seconds} seconds.",
    true,
    true,
    "pre-maintenance backup",
    true,
    false,
    string.Empty,
    string.Empty,
    1800,
    true,
    true,
    true,
    true,
    180,
    5);

static void ExpectArgument(Action action, string message)
{
    try
    {
        action();
    }
    catch (ArgumentException)
    {
        return;
    }
    throw new InvalidOperationException(message);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
