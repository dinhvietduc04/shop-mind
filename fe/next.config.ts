import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Keep the dev-mode "N issues" badge out of demo captures and screenshots.
  devIndicators: false,
  // cacheComponents (Next 16 default) validates every navigated segment for
  // instant UI. The browser-only mock boot (msw cannot enter the SSR graph)
  // intentionally defers first render, which the validation flags. There is no
  // server data fetching to cache yet. The real API and RSC pass can re-enable
  // it.
  cacheComponents: false,
  partialPrefetching: false,
  turbopack: {
    rules: {
      "*.css": {
        loaders: ["@tailwindcss/turbopack"],
        as: "*.css",
      },
    },
  },
};

export default nextConfig;
