"use client";

import { Suspense, useState, useSyncExternalStore } from "react";
import Link from "next/link";
import dynamic from "next/dynamic";
import { usePathname } from "next/navigation";
import { ThemeProvider, useTheme } from "next-themes";
import { QueryClientProvider } from "@tanstack/react-query";
import { Moon, Radio, Search, ShoppingCart, Sun } from "lucide-react";
import { cn } from "@/lib/cn";
import { BandChip } from "@/components/ui";
import { useCart, useCategories, useProducts } from "@/lib/api/hooks";
import { useSession } from "@/lib/session";
import { queryClient } from "@/lib/api/queryClient";
import { beginHold } from "@/lib/api/readyGate";
import { formatUsd } from "@/lib/api/types";
import { useShopUI } from "@/lib/ui";
import { BAND_DIGIT, bandClass, bandForCategory } from "@/lib/bands";

/* -------------------------------- Providers -------------------------------- */

// ssr:false keeps msw out of the server bundle graph (its browser export
// refuses Node resolution in msw v3). The provider arms the request gate, so
// pages paint while api() parks requests until the worker answers.
const MockBootstrapper = dynamic(() => import("@/mocks/Bootstrapper"), {
  ssr: false,
});

export function Providers({ children }: { children: React.ReactNode }) {
  const mockMode = (process.env.NEXT_PUBLIC_API_MODE ?? "mock") === "mock";
  /* Arm the request gate during this render, before any child effect can
     schedule a fetch. Idempotent; never armed against the real API. */
  useState(() => {
    if (mockMode && typeof window !== "undefined") beginHold();
    return true;
  });

  return (
    <QueryClientProvider client={queryClient}>
      <ThemeProvider attribute="class" defaultTheme="light" enableSystem>
        {/* cacheComponents: browser-only content (next/dynamic ssr:false loading
            browser()) must suspend to a Suspense boundary during prerender. */}
        {mockMode && (
          <Suspense fallback={null}>
            <MockBootstrapper />
          </Suspense>
        )}
        {children}
      </ThemeProvider>
    </QueryClientProvider>
  );
}

/* ------------------------------- Theme toggle ------------------------------ */

/* Mounted-boolean for SSR-safe client-only rendering: the canonical
   useSyncExternalStore pattern (server snapshot = false, client = true). */
const emptySubscribe = () => () => {};
const useMounted = () =>
  useSyncExternalStore(
    emptySubscribe,
    () => true,
    () => false
  );

function ThemeToggle() {
  const { resolvedTheme, setTheme } = useTheme();
  const mounted = useMounted();
  // Before mount we cannot know the stored theme. Render the neutral Moon
  // so server and first client paint agree (next-themes hydration rule).
  const dark = mounted && resolvedTheme === "dark";
  return (
    <button
      aria-label={dark ? "Switch to paper (light) theme" : "Switch to night (dark) theme"}
      onClick={() => setTheme(dark ? "light" : "dark")}
      className="rounded-[4px] border-[1.5px] border-ink/60 p-2 hover:border-ink"
    >
      {dark ? <Sun size={16} /> : <Moon size={16} />}
    </button>
  );
}

/* --------------------------------- Masthead -------------------------------- */

export function Masthead() {
  const { query, setQuery } = useShopUI();
  const pathname = usePathname();
  const user = useSession((s) => s.user);
  const { data: cart } = useCart();
  const count = cart?.totalQuantity ?? 0;
  const total = cart?.subtotal ?? 0;
  const navItems = [
    { href: "/", label: "Catalog" },
    { href: "/quote", label: "Bill" },
    { href: "/orders", label: "Orders" },
    { href: "/assistant", label: "Assistant" },
    ...(user?.role === "Admin" ? [{ href: "/admin", label: "Admin" }] : []),
    { href: "/dev/ui", label: "UI kit" },
  ];
  const isActive = (href: string) =>
    href === "/" ? pathname === "/" : pathname.startsWith(href);
  return (
    <header className="sticky top-0 z-20 border-b-[1.5px] border-ink bg-kraft">
      <div className="mx-auto flex max-w-[1600px] flex-wrap items-center gap-x-4 gap-y-2 px-4 py-3 md:flex-nowrap md:gap-6 md:px-6">
        <Link href="/" className="flex shrink-0 items-baseline gap-3">
          <span className="text-lg font-extrabold uppercase tracking-[0.2em] md:text-xl md:tracking-[0.3em]">
            ShopMind
          </span>
          <span className="hidden font-mono text-[11px] uppercase tracking-[0.24em] text-ink-soft md:inline">
            cat. elec-026
          </span>
        </Link>
        <span
          className="hidden items-center gap-[3px] md:flex"
          title="Today's code: the five category bands, in bin order"
          aria-label="Category bands: laptops brown, phones orange, audio green, displays blue, components violet"
        >
          {(["brown", "orange", "green", "blue", "violet"] as const).map((b) => (
            <BandChip key={b} band={b} size="sm" title={b} />
          ))}
        </span>
        <nav className="hidden items-center gap-1 text-[12px] font-bold uppercase tracking-[0.16em] lg:flex">
          {navItems.map((n) => (
            <Link
              key={n.href}
              href={n.href}
              className={cn(
                "rounded-[4px] px-3 py-1.5",
                isActive(n.href) ? "bg-ink text-paper" : "hover:bg-ink/10"
              )}
            >
              {n.label}
            </Link>
          ))}
        </nav>
        <div className="relative order-last min-w-0 grow basis-full md:order-none md:ml-auto md:w-40 md:max-w-[380px] md:basis-auto">
          <Search
            size={15}
            aria-hidden
            className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-ink-soft"
          />
          <input
            type="search"
            aria-label="Search the catalog"
            suppressHydrationWarning
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder='search bins, for example "16 GB laptop under $1,200"'
            className="w-full rounded-[4px] border-[1.5px] border-line bg-card py-2 pl-9 pr-3 font-mono text-[13px] placeholder:text-ink-soft focus:border-ink"
          />
        </div>
        <div className="ml-auto flex shrink-0 items-center gap-2 md:ml-0">
          <Link
            href="/quote"
            className={cn(
              "flex items-center gap-2 rounded-[4px] border-[1.5px] px-3 py-1.5",
              count > 0
                ? "border-ink bg-band-yellow font-extrabold text-[#1a1a1a] hover:brightness-105"
                : "border-line text-ink-soft hover:border-ink"
            )}
            aria-label={`Cart: ${count} items, subtotal ${formatUsd(total)}. Open the bill.`}
          >
            <ShoppingCart size={15} aria-hidden />
            <span className="font-mono text-[13px] tabular-nums">
              {count > 0 ? formatUsd(total) : "empty"}
            </span>
          </Link>
          <Link
            href={user ? "/account" : `/login?next=${encodeURIComponent(pathname)}`}
            className={cn(
              "flex items-center gap-2 rounded-[4px] border-[1.5px] px-3 py-1.5 text-[11px] font-bold uppercase tracking-[0.14em]",
              user
                ? "border-ink hover:bg-ink/10"
                : "border-dashed border-line text-ink-soft hover:border-ink hover:text-ink"
            )}
            aria-label={user ? `Account: ${user.email}, role ${user.role}` : "Sign in"}
            title={user ? `${user.firstName} ${user.lastName} · ${user.role}` : "Guest, no session"}
          >
            {user ? (
              <>
                <span className="grid h-6 w-6 place-items-center rounded-full bg-ink font-mono text-[11px] text-paper">
                  {user.firstName?.[0]}
                  {user.lastName?.[0]}
                </span>
                <span className="hidden md:inline">{user.role === "Admin" ? "Admin" : "Account"}</span>
              </>
            ) : (
              "Sign in"
            )}
          </Link>
          <ThemeToggle />
        </div>
      </div>
      <nav
        aria-label="Primary"
        className="flex gap-2 overflow-x-auto border-t-[1.5px] border-transparent px-4 pb-2 md:px-6 lg:hidden"
      >
        {navItems.map((n) => (
          <Link
            key={n.href}
            href={n.href}
            className={cn(
              "shrink-0 rounded-[4px] border-[1.5px] px-3 py-1 text-[11px] font-bold uppercase tracking-[0.16em]",
              isActive(n.href)
                ? "border-ink bg-ink text-paper"
                : "border-line text-ink hover:border-ink"
            )}
          >
            {n.label}
          </Link>
        ))}
      </nav>
    </header>
  );
}

/* --------------------------------- Bin rail -------------------------------- */

export function BinRail() {
  const { category: active, setCategory: onSelect } = useShopUI();
  const { data: categories } = useCategories();
  const { data: all } = useProducts({ pageSize: 100 });
  const counts = new Map<string, number>();
  const binOf = new Map<string, string>();
  (all?.items ?? []).forEach((p) => {
    if (p.categoryName) counts.set(p.categoryName, (counts.get(p.categoryName) ?? 0) + 1);
    binOf.set(p.categoryName ?? "", p.sku.slice(0, 3).toUpperCase());
  });
  return (
    <aside className="w-full shrink-0 border-r-[1.5px] border-line bg-kraft px-4 py-5 lg:w-[248px]">
      <h2 className="mb-3 text-[11px] font-extrabold uppercase tracking-[0.26em]">
        Bins
      </h2>
      <div className="flex gap-2 overflow-x-auto lg:flex-col lg:overflow-visible">
        <button
          onClick={() => onSelect(null)}
          className={cn(
            "flex min-w-[150px] items-center gap-2.5 rounded-[6px] border-[1.5px] px-3 py-2 text-left",
            active === null
              ? "border-ink bg-card font-extrabold"
              : "border-line/70 hover:border-ink"
          )}
        >
          <span className="font-mono text-[11px] text-ink-soft">ALL</span>
          <span className="text-[13px] uppercase tracking-[0.08em]">Every bin</span>
          <span className="ml-auto font-mono text-[11px] text-ink-soft">
            {all?.totalCount ?? "··"}
          </span>
        </button>
        {(categories ?? []).map((c) => {
          const code = binOf.get(c.name) ?? c.slug.slice(0, 3).toUpperCase();
          return (
          <button
            key={c.id}
            onClick={() => onSelect(active === c.slug ? null : c.slug)}
            aria-pressed={active === c.slug}
            className={cn(
              "flex min-w-[150px] items-center gap-2.5 rounded-[6px] border-[1.5px] px-3 py-2 text-left",
              active === c.slug
                ? "border-ink bg-card font-extrabold"
                : "border-line/70 hover:border-ink"
            )}
          >
            <span
              aria-hidden
              className={cn(
                "h-[22px] w-3 shrink-0 rounded-[3px] border-[1.5px] border-[#1a1a1a] dark:border-[#8a8068]",
                bandClass(bandForCategory(c.slug))
              )}
            />
            <span className="text-[13px] uppercase tracking-[0.08em]">{c.name}</span>
            <span className="ml-auto font-mono text-[11px] text-ink-soft">
              {code}·{counts.get(c.name) ?? 0}
            </span>
          </button>
          );
        })}
      </div>

      <div className="mt-6 hidden border-t-[1.5px] border-dashed border-line pt-5 lg:block">
        <RailLegend />
      </div>
      <details className="mt-4 lg:hidden">
        <summary className="cursor-pointer select-none rounded-[6px] border-[1.5px] border-line bg-card px-3 py-2 text-[11px] font-extrabold uppercase tracking-[0.26em]">
          Legend · code &amp; colors
        </summary>
        <div className="mt-2 rounded-[6px] border-[1.5px] border-line bg-card px-3 pb-3 pt-1">
          <RailLegend />
        </div>
      </details>
    </aside>
  );
}

function RailLegend() {
  return (
    <div className="space-y-5 text-[12px]">
      <div>
        <h3 className="mb-2 text-[11px] font-extrabold uppercase tracking-[0.26em]">
          Stock dots
        </h3>
        <ul className="space-y-1.5 text-ink-soft">
          <li className="flex items-center gap-2">
            <span aria-hidden className="h-3 w-3 rounded-full border border-ink bg-band-green" />
            5+ in bin
          </li>
          <li className="flex items-center gap-2">
            <span aria-hidden className="h-3 w-3 rounded-full border border-ink bg-band-yellow" />
            1–4 low
          </li>
          <li className="flex items-center gap-2">
            <span aria-hidden className="h-3 w-3 rounded-full border border-ink bg-band-red" />
            0 empty
          </li>
        </ul>
      </div>
      <div>
        <h3 className="mb-2 flex items-center gap-1.5 text-[11px] font-extrabold uppercase tracking-[0.26em]">
          <Radio size={12} aria-hidden /> Value ladder
        </h3>
        <div className="flex h-4 overflow-hidden rounded-[4px] border-[1.5px] border-ink">
          {(
            ["black", "brown", "red", "orange", "yellow", "green", "blue", "violet", "grey", "white"] as const
          ).map((b) => (
            <span
              key={b}
              title={`${BAND_DIGIT[b]} = ${b}`}
              className={cn("flex-1", bandClass(b))}
            />
          ))}
        </div>
        <p className="mt-2 leading-relaxed">
          The stripe beside every price is the resistor{" "}
          <strong className="font-semibold text-ink">multiplier</strong> band, one
          colour per order of magnitude: brown ×10, red ×100, orange ×1k. The exact
          number is always printed beside it. The band only shows the order of
          magnitude.
        </p>
      </div>
      <p className="border-t-[1.5px] border-dashed border-line pt-4 text-ink-soft">
        <span
          aria-hidden
          className="mr-1.5 inline-block h-3 w-3 translate-y-0.5 rounded-[3px] border border-ink bg-band-yellow"
        />
        <strong className="text-ink">Yellow is human-only.</strong> The assistant
        never presses a yellow control.
      </p>
    </div>
  );
}
