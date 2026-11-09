"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { BandChip } from "@/components/ui";
import { RequireRole } from "@/components/guard";
import { cn } from "@/lib/cn";

const TABS = [
  { href: "/admin", label: "Dashboard" },
  { href: "/admin/products", label: "Products" },
  { href: "/admin/categories", label: "Categories" },
  { href: "/admin/inventory", label: "Inventory" },
  { href: "/admin/orders", label: "Orders" },
  { href: "/admin/ai", label: "AI audit" },
];

/** Exact match for /admin itself, prefix match for the sections. */
function tabActive(pathname: string, href: string) {
  return href === "/admin" ? pathname === "/admin" : pathname.startsWith(href);
}

/** One gate for the whole console (§5.6): guests bounce to the turnstile with
 *  a return path, signed-in non-admins get the 403 card naming their identity. */
export default function AdminLayout({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  return (
    <RequireRole role="Admin">
      <div className="mx-auto max-w-[1080px] px-6 py-6">
        <div className="flex flex-wrap items-center gap-2 border-b-[1.5px] border-ink pb-3">
          <BandChip band="red" size="md" title="admin surface" />
          <span className="font-mono text-[11px] font-bold uppercase tracking-[0.22em]">
            Admin console
          </span>
          <nav aria-label="Admin sections" className="ml-auto flex flex-wrap gap-1">
            {TABS.map((t) => (
              <Tab key={t.href} href={t.href} label={t.label} active={tabActive(pathname, t.href)} />
            ))}
          </nav>
        </div>
        {children}
      </div>
    </RequireRole>
  );
}

function Tab({ href, label, active }: { href: string; label: string; active: boolean }) {
  return (
    <Link
      href={href}
      aria-current={active ? "page" : undefined}
      className={cn(
        "rounded-[4px] border-[1.5px] px-3 py-1.5 text-[11px] font-bold uppercase tracking-[0.14em]",
        active ? "border-ink bg-ink text-paper" : "border-line text-ink-soft hover:border-ink hover:text-ink"
      )}
    >
      {label}
    </Link>
  );
}
