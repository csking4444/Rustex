import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "@/lib/apiClient";
import type {
  BillingInterval,
  Entitlement,
  Invoice,
  PaymentMethod,
  Plan,
  Subscription,
} from "@/types";

/** Anonymous — the pricing page renders before sign-in. */
export function usePlans() {
  return useQuery({
    queryKey: ["billing", "plans"],
    queryFn: async () => (await apiClient.get<Plan[]>("/billing/plans")).data,
    // The catalog only changes on deploy, so re-fetching it on every mount is wasted traffic.
    staleTime: 60 * 60 * 1000,
  });
}

export function useSubscription() {
  return useQuery({
    queryKey: ["billing", "subscription"],
    queryFn: async () => (await apiClient.get<Subscription>("/billing/subscription")).data,
  });
}

/**
 * Resolved entitlement, re-checked periodically. The server is the authority — this is only used
 * to decide what to render, never to decide what is allowed, so a stale value here can at worst
 * show a feature whose API call then returns 402/403.
 */
export function useEntitlement() {
  return useQuery({
    queryKey: ["billing", "entitlement"],
    queryFn: async () => (await apiClient.get<Entitlement>("/billing/entitlement")).data,
    staleTime: 30 * 1000,
  });
}

export function useInvoices() {
  return useQuery({
    queryKey: ["billing", "invoices"],
    queryFn: async () => (await apiClient.get<Invoice[]>("/billing/invoices")).data,
  });
}

export function usePaymentMethod() {
  return useQuery({
    queryKey: ["billing", "payment-method"],
    queryFn: async () => (await apiClient.get<PaymentMethod | null>("/billing/payment-method")).data,
  });
}

/** Everything billing-related, refreshed together after any change. */
function invalidateBilling(queryClient: ReturnType<typeof useQueryClient>) {
  void queryClient.invalidateQueries({ queryKey: ["billing"] });
  // Server limits come from the plan, so the servers list may now allow (or disallow) an add.
  void queryClient.invalidateQueries({ queryKey: ["servers"] });
}

/** Sends the browser to the provider's hosted checkout. */
export function useStartCheckout() {
  return useMutation({
    mutationFn: async (input: { tier: string; interval: BillingInterval }) =>
      (await apiClient.post<{ url: string }>("/billing/checkout", input)).data,
    onSuccess: (data) => {
      window.location.href = data.url;
    },
  });
}

export function useChangePlan() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: { tier: string; interval: BillingInterval }) =>
      (await apiClient.post<Subscription>("/billing/change-plan", input)).data,
    onSuccess: () => invalidateBilling(queryClient),
  });
}

export function useCancelSubscription() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (immediately: boolean) =>
      (await apiClient.post<Subscription>("/billing/cancel", { immediately })).data,
    onSuccess: () => invalidateBilling(queryClient),
  });
}

export function useResumeSubscription() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => (await apiClient.post<Subscription>("/billing/resume")).data,
    onSuccess: () => invalidateBilling(queryClient),
  });
}

/** Redirects to the provider's hosted portal — we never collect card details ourselves. */
export function useUpdatePaymentMethod() {
  return useMutation({
    mutationFn: async () => (await apiClient.post<{ url: string }>("/billing/payment-method")).data,
    onSuccess: (data) => {
      window.location.href = data.url;
    },
  });
}
