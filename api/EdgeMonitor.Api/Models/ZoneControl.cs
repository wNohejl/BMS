namespace EdgeMonitor.Api.Models;

/// <summary>Desired control configuration per zone — what the user asked for.
/// Sent to the C++ engine over the command channel; the engine reports actual
/// state back via status snapshots (ZoneStatus).</summary>
public class ZoneControl
{
    public int Id { get; set; }
    public string TenantId { get; set; } = "";
    public string ZoneId { get; set; } = "";
    public string Mode { get; set; } = "schedule"; // "schedule" | "manual"
    public double? ManualSetpointF { get; set; }   // set when Mode == "manual"
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>Latest reported control state per zone — what the engine says is
/// actually happening (state machine state, effective setpoint, temperature).</summary>
public class ZoneStatus
{
    public int Id { get; set; }
    public string TenantId { get; set; } = "";
    public string ZoneId { get; set; } = "";
    public string Name { get; set; } = "";        // from the engine's building model
    public int Floor { get; set; }
    public string HvacState { get; set; } = ""; // "idle" | "cooling" | "heating"
    public double SetpointF { get; set; }
    public double TempF { get; set; }        // model ground truth
    public double SensorTempF { get; set; }  // reported by the (possibly drifting) sensor
    public bool Occupied { get; set; }
    public string FaultsCsv { get; set; } = "";   // active injected faults, comma-separated
    public DateTime ReportedUtc { get; set; }
}

/// <summary>Alarm history: rows stay after clearing (IsActive=false) so the
/// twin has an auditable record of every incident and recovery.</summary>
public class TwinAlarm
{
    public int Id { get; set; }
    public string TenantId { get; set; } = "";
    public string ZoneId { get; set; } = "";
    public string Type { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime RaisedUtc { get; set; }
    public bool IsActive { get; set; }
    public DateTime? ClearedUtc { get; set; }
}

/// <summary>Audit trail of every command sent to a device — who asked the
/// building to do what, and when.</summary>
public class DeviceCommand
{
    public int Id { get; set; }
    public string TenantId { get; set; } = "";
    public string ZoneId { get; set; } = "";
    public string Type { get; set; } = ""; // "setSetpoint" | "clearSetpoint"
    public double Value { get; set; }
    public DateTime SentUtc { get; set; }
}
