import * as signalR from "@microsoft/signalr";
import { tokenStorage } from "./tokenStorage";
import { detectClientKind } from "./clientKind";

let connection: signalR.HubConnection | null = null;

export function getDashboardConnection(): signalR.HubConnection {
  if (connection) return connection;

  const hubUrl = `${import.meta.env.VITE_HUB_URL}?clientKind=${detectClientKind()}`;

  connection = new signalR.HubConnectionBuilder()
    .withUrl(hubUrl, {
      accessTokenFactory: () => tokenStorage.getAccessToken() ?? "",
    })
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Warning)
    .build();

  return connection;
}

export async function stopDashboardConnection(): Promise<void> {
  if (connection) {
    await connection.stop();
    connection = null;
  }
}
