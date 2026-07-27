import { Navigate, Outlet } from "react-router-dom";
import { useEntitlement } from "@/hooks/useBilling";

/**
 * Gates the paid part of the app behind an active plan.
 *
 * This is a routing convenience, not the security boundary — every gated API endpoint re-checks
 * entitlement server-side, so bypassing this in the browser gets you an empty page and a string
 * of 402s rather than access to anything.
 */
export function EntitledRoute() {
  const { data, isLoading, isError } = useEntitlement();

  if (isLoading) {
    return (
      <div className="flex h-full min-h-[50vh] w-full items-center justify-center">
        <div className="h-10 w-10 animate-spin rounded-full border-2 border-blood border-t-transparent" />
      </div>
    );
  }

  // On a failed check, let them through to the page rather than bouncing them to billing on a
  // transient network blip. The API is still enforcing, so nothing is actually exposed.
  if (isError) return <Outlet />;

  if (!data?.isEntitled) return <Navigate to="/billing" replace />;

  return <Outlet />;
}
