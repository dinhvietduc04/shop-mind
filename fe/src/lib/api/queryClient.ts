import { QueryClient } from "@tanstack/react-query";
import { ApiError } from "./types";

/**
 * Singleton so non-component code (the chat store, after a tool-driven cart
 * mutation) can invalidate the same cache the pages read from. The retry
 * policy lives here: 4xx (except 429) is a verdict, not a hiccup.
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
