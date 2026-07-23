using PalOps.Web.AdvancedOperations;

VerifyValidation();
VerifyIncidentTransitions();
VerifyTokenSecurity();
VerifyPlaybookSafety();
VerifyRiskScoring();
Console.WriteLine("PalOps advanced operations verifier passed.");

static void VerifyValidation()
{
    var validator = new AdvancedOperationsValidator();
    Assert(validator.NormalizeSeverity("HIGH") == IncidentSeverity.High, "severity normalization failed");
    Assert(validator.NormalizeTargetType("S3") == DisasterRecoveryTargetType.S3Compatible, "target type alias failed");
    ExpectArgument(() => validator.NormalizeTargetType("ftp"), "unsupported target type must fail");
    Assert(validator.ValidateName(new string('x', 160), "name", 160).Length == 160, "caller-provided name limit must be honored");
    ExpectArgument(() => validator.ValidateName(new string('x', 161), "name", 160), "name above caller-provided limit must fail");
    var steps = validator.ValidatePlaybookSteps([
        new OperationsPlaybookStep(3, "health-refresh", new Dictionary<string, string>
        {
            [""] = "ignored",
            ["Mode"] = "first",
            ["mode"] = "second"
        }, false)
    ]);
    Assert(steps[0].Parameters.Count == 1 && steps[0].Parameters["mode"] == "second", "playbook parameters must skip empty keys and normalize case-insensitive duplicates");
    AdvancedOperationsValidator.RequireConfirmation("RUN UPDATE PLAN", "RUN UPDATE PLAN");
    ExpectArgument(() => AdvancedOperationsValidator.RequireConfirmation("run update plan", "RUN UPDATE PLAN"), "confirmation must be exact");
}

static void VerifyIncidentTransitions()
{
    var now = DateTimeOffset.Parse("2026-07-22T00:00:00Z");
    var incident = IncidentTransitions.Create("Disk low", IncidentSeverity.High, "diagnostic", "disk.free", now);
    incident = IncidentTransitions.Acknowledge(incident, "operator", "investigating", now.AddMinutes(1));
    Assert(incident.Status == IncidentStatus.Acknowledged, "acknowledge transition failed");
    incident = IncidentTransitions.Assign(incident, "owner", "admin", now.AddMinutes(2));
    Assert(incident.Assignee == "admin", "assign transition failed");
    incident = IncidentTransitions.Resolve(incident, "admin", "space reclaimed", now.AddMinutes(3));
    Assert(incident.Status == IncidentStatus.Resolved, "resolve transition failed");
    incident = IncidentTransitions.Reopen(incident, "owner", "alert returned", now.AddMinutes(4));
    Assert(incident.Status == IncidentStatus.Open, "reopen transition failed");
    Assert(incident.Timeline.Count == 5, "incident timeline must retain all transitions");
}

static void VerifyTokenSecurity()
{
    var generated = ApiTokenSecret.Create("status.read", DateTimeOffset.UtcNow.AddHours(1));
    Assert(generated.PlainText.StartsWith("palops_", StringComparison.Ordinal), "token prefix missing");
    Assert(generated.Record.Verify(generated.PlainText, DateTimeOffset.UtcNow, "status.read"), "generated token must verify");
    Assert(!generated.Record.TokenHash.Equals(generated.PlainText, StringComparison.Ordinal), "plaintext token must never be persisted as its hash");
    Assert(!generated.Record.Verify(generated.PlainText + "x", DateTimeOffset.UtcNow, "status.read"), "modified token must fail");
    Assert(!generated.Record.Verify(generated.PlainText, DateTimeOffset.UtcNow, "events.write"), "scope mismatch must fail");
    Assert(!(generated.Record with { RevokedAt = DateTimeOffset.UtcNow }).Verify(generated.PlainText, DateTimeOffset.UtcNow, "status.read"), "revoked token must fail");
}

static void VerifyPlaybookSafety()
{
    foreach (var action in OperationsPlaybookCatalog.AllowedActions)
        Assert(OperationsPlaybookCatalog.IsAllowed(action), $"allowed action rejected: {action}");
    Assert(!OperationsPlaybookCatalog.IsAllowed("shell"), "shell action must be rejected");
    Assert(!OperationsPlaybookCatalog.IsAllowed("process-start"), "process action must be rejected");
}

static void VerifyRiskScoring()
{
    Assert(PlayerRiskScorer.Score(0, false, false, false) == 0, "clean player score must be zero");
    Assert(PlayerRiskScorer.Score(100, true, true, true) == 100, "risk score must clamp to 100");
    Assert(PlayerRiskScorer.Score(2, true, false, false) > PlayerRiskScorer.Score(1, false, false, false), "discipline signals must increase score");
}

static void ExpectArgument(Action action, string message)
{
    try { action(); }
    catch (ArgumentException) { return; }
    throw new InvalidOperationException(message);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
