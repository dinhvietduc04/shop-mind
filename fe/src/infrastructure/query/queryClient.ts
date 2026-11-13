import { QueryClient } from "@tanstack/react-query";
import { ApiError } from "@/infrastructure/api";

/**
 * Singleton so non-component code can invalidate the same cache the pages read.
 * This is the app-wide React Query configuration and is intentionally kept in
 * the infrastructure layer rather than the feature layer.
 */
export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: (failureCount, error) => {
        const status = error instanceof ApiError ? error.status : 0;
        if (status >= 400 && status < 500 && status !== 429) return false;
        return failureCount < 2;
      },
    },
  },
});
