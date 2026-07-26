export type RaidTier = "Tier1" | "Tier2" | "Tier3";
export type RaidStatus = "Active" | "Quiet" | "Ended";
export type ServerStatus = "Unknown" | "Online" | "Offline";
export type NotificationSeverity = "Info" | "Warning" | "Critical";

export interface NotificationSummary {
  id: string;
  type: string;
  title: string;
  body: string | null;
  severity: NotificationSeverity;
  isRead: boolean;
  relatedEntityType: string | null;
  relatedEntityId: string | null;
  createdAt: string;
}

export interface UserSettings {
  soundEnabled: boolean;
  desktopEnabled: boolean;
  browserEnabled: boolean;
  discordEnabled: boolean;
  pushEnabled: boolean;
  callEnabled: boolean;
  quietHoursStart: string | null;
  quietHoursEnd: string | null;
  quietHoursTimezone: string;
  updatedAt: string;
}

export interface Webhook {
  id: string;
  serverId: string | null;
  url: string;
  eventTypes: string[];
  isActive: boolean;
  createdAt: string;
}

export interface CurrentUser {
  id: string;
  discordUsername: string;
  discordAvatar: string | null;
  email: string | null;
  displayName: string | null;
  timezone: string;
}

export interface RustServerSummary {
  id: string;
  name: string;
  ipAddress: string;
  gamePort: number;
  queryPort: number | null;
  mapName: string | null;
  seed: number | null;
  worldSize: number | null;
  description: string | null;
  status: ServerStatus;
  tags: string[];
  isFavorite: boolean;
  wipeSchedule: string | null;
  restartSchedule: string | null;
  autoReconnect: boolean;
  createdAt: string;
  pingMs: number | null;
  playerCount: number | null;
  maxPlayers: number | null;
  queueSize: number | null;
  lastPolledAt: string | null;
}

export interface RaidEventSummary {
  id: string;
  serverId: string;
  serverName: string;
  detectedAt: string;
  grid: string | null;
  tier: RaidTier;
  raidType: string | null;
  explosionCount: number;
  estimatedSize: string | null;
  status: RaidStatus;
}

export interface EmergencyRaidAlertPayload {
  id: string;
  serverId: string;
  serverName: string;
  tier: RaidTier;
  grid: string | null;
  explosionCount: number;
  raidType: string | null;
  detectedAt: string;
}

export interface RaidAlarmSettings {
  serverId: string;
  isEnabled: boolean;
  tier1Threshold: number;
  tier2Threshold: number;
  tier3Threshold: number;
  timeWindowSeconds: number;
  clusterRadius: number;
  cooldownSeconds: number;
  updatedAt: string;
}

export interface TeamSummary {
  id: string;
  name: string;
  slug: string;
  iconUrl: string | null;
  createdAt: string;
  roleName: string;
}

export interface MessageTemplate {
  id: string;
  teamId: string;
  serverId: string | null;
  eventType: string;
  templateText: string;
  isEnabled: boolean;
  cooldownSeconds: number;
  createdAt: string;
}

export interface ChatTemplateMetadata {
  eventTypes: string[];
  placeholders: string[];
}

export interface TeamMemberSummary {
  id: string;
  userId: string;
  discordUsername: string;
  discordAvatar: string | null;
  roleName: string;
  status: string;
  joinedAt: string;
}

export interface TeamInviteSummary {
  id: string;
  token: string;
  inviteeDiscord: string | null;
  status: string;
  expiresAt: string;
  createdAt: string;
}

export type MarkerType = "Raid" | "Team" | "Player" | "Custom" | "Monument";

export interface MapInfo {
  id: string;
  serverId: string;
  imageUrl: string | null;
  width: number | null;
  height: number | null;
  updatedAt: string;
}

export interface MapMarker {
  id: string;
  mapId: string;
  createdBy: string;
  type: MarkerType;
  x: number;
  y: number;
  label: string | null;
  color: string | null;
  isShared: boolean;
  createdAt: string;
}

export interface DailyRaidCount {
  date: string;
  count: number;
}

export interface HourlyRaidCount {
  hourUtc: number;
  count: number;
}

export interface AnalyticsSummary {
  serverId: string;
  days: number;
  totalRaids: number;
  tier1Count: number;
  tier2Count: number;
  tier3Count: number;
  raidsByDay: DailyRaidCount[];
  raidsByHour: HourlyRaidCount[];
  avgPingMs: number | null;
  avgPlayerCount: number | null;
  peakPlayerCount: number | null;
}
