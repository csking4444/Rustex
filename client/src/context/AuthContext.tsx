import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { apiClient } from "@/lib/apiClient";
import { tokenStorage } from "@/lib/tokenStorage";
import type { CurrentUser } from "@/types";

interface TokenPair {
  accessToken: string;
  refreshToken: string;
}

interface AuthContextValue {
  user: CurrentUser | null;
  isLoading: boolean;
  isAuthenticated: boolean;
  loginWithGoogle: () => void;
  loginWithSteam: () => void;
  registerWithPassword: (email: string, password: string, username: string) => Promise<void>;
  loginWithPassword: (email: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  refetchUser: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<CurrentUser | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const fetchUser = useCallback(async () => {
    if (!tokenStorage.getAccessToken()) {
      setUser(null);
      setIsLoading(false);
      return;
    }

    try {
      const response = await apiClient.get<CurrentUser>("/users/me");
      setUser(response.data);
    } catch {
      setUser(null);
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    void fetchUser();
  }, [fetchUser]);

  const loginWithGoogle = useCallback(() => {
    window.location.href = `${import.meta.env.VITE_API_BASE_URL}/auth/google/login`;
  }, []);

  const loginWithSteam = useCallback(() => {
    window.location.href = `${import.meta.env.VITE_API_BASE_URL}/auth/steam/login`;
  }, []);

  const registerWithPassword = useCallback(
    async (email: string, password: string, username: string) => {
      const { data } = await apiClient.post<TokenPair>("/auth/register", { email, password, username });
      tokenStorage.setTokens(data.accessToken, data.refreshToken);
      await fetchUser();
    },
    [fetchUser],
  );

  const loginWithPassword = useCallback(
    async (email: string, password: string) => {
      const { data } = await apiClient.post<TokenPair>("/auth/login", { email, password });
      tokenStorage.setTokens(data.accessToken, data.refreshToken);
      await fetchUser();
    },
    [fetchUser],
  );

  const logout = useCallback(async () => {
    const refreshToken = tokenStorage.getRefreshToken();
    if (refreshToken) {
      try {
        await apiClient.post("/auth/logout", { refreshToken });
      } catch {
        // best-effort — clear local state regardless
      }
    }
    tokenStorage.clear();
    setUser(null);
  }, []);

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      isLoading,
      isAuthenticated: user !== null,
      loginWithGoogle,
      loginWithSteam,
      registerWithPassword,
      loginWithPassword,
      logout,
      refetchUser: fetchUser,
    }),
    [user, isLoading, loginWithGoogle, loginWithSteam, registerWithPassword, loginWithPassword, logout, fetchUser],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within an AuthProvider");
  return ctx;
}
