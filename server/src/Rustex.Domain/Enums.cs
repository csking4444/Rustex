namespace Rustex.Domain;

public enum ServerStatus { Unknown, Online, Offline }

public enum TeamMemberStatus { Active, Invited, Removed }

public enum InviteStatus { Pending, Accepted, Expired, Revoked }

/// <summary>Count-based raid classification: how many qualifying explosions landed in one
/// cluster (see RaidAlarmEvaluator), not a subjective "how bad is this" judgment call.</summary>
public enum RaidTier { Tier1 = 1, Tier2 = 2, Tier3 = 3 }

public enum RaidStatus { Active, Quiet, Ended }

public enum EventSourceKind { Simulated, RustPlus, Plugin }

public enum NotificationSeverity { Info, Warning, Critical }

public enum NotificationChannel { InApp, Desktop, Browser, Discord, Push, Call }

public enum DeliveryStatus { Queued, Sent, Delivered, Failed }

public enum MarkerType { Raid, Team, Player, Custom, Monument }

public enum CallProvider { Twilio, Vonage, Plivo }

public enum CallStatus { Queued, Ringing, Answered, Missed, Failed }
