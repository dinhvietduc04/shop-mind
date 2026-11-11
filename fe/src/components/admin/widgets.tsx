"use client";

import { type ReactNode } from "react";
import { BandChip } from "@/components/ui";
import type { BandName } from "@/lib/bands";
import { ORDER_BAND } from "@/lib/orders/lifecycle";
import type { OrderStatus } from "@/lib/api/types";
import { cn } from "@/lib/cn";

/** Shared layout for the admin screens. Same flat-outline world as the
 *  storefront, with denser grids. */

export function Panel({
  title,
  aside,
  children,
  className,
}: {
  title: string;
  aside?: ReactNode;
  children: ReactNode;
  className?: string;
}) {
  return (
    <section className={cn("rounded-label border-[1.5px] border-ink bg-card", className)}>
      <header className="flex flex-wrap items-center gap-3 border-b-[1.5px] border-line px-4 py-2.5">
        <h2 className="font-mono text-[11px] font-bold uppercase tracking-[0.22em]">{title}</h2>
        {aside && <div className="ml-auto flex flex-wrap items-center justify-end gap-2">{aside}</div>}
      </header>
      <div className="p-4">{children}</div>
    </section>
  );
}

export function Kpi({
  label,
  value,
  sub,
  band,
}: {
  label: string;
  value: ReactNode;
  sub?: string;
  band?: BandName;
}) {
  return (
    <div className="rounded-label border-[1.5px] border-ink bg-card px-4 py-3">
      <p className="flex items-center gap-2 font-mono text-[11px] font-bold uppercase tracking-[0.18em] text-ink-soft">
        {band && <BandChip band={band} size="sm" />}
        {label}
      </p>
      <p className="mt-1 font-mono text-[22px] font-bold leading-none">{value}</p>
      {sub && <p className="mt-1.5 text-[11px] leading-snug text-ink-soft">{sub}</p>}
    </div>
  );
}

/** Band-styled switch. It uses role="switch" for the a11y tree and a mono
 *  ON/OFF readout, so state is never color-only. */
export function Toggle({
  label,
  hint,
  checked,
  onChange,
  disabled,
  danger,
}: {
  label: string;
  hint?: string;
  checked: boolean;
  onChange: (next: boolean) => void;
  disabled?: boolean;
  danger?: boolean; // off means the tool is switched off and gets a red band; healthy is green
}) {
  return (
    <div className={cn("flex items-start gap-3 py-1.5", disabled && "opacity-50")}>
      <button
        type="button"
        role="switch"
        aria-checked={checked}
        aria-label={label}
        aria-describedby={hint ? `hint-${label.replace(/[^\w]/g, "")}` : undefined}
        disabled={disabled}
        onClick={() => onChange(!checked)}
        className={cn(
          "relative mt-0.5 h-5 w-10 shrink-0 rounded-[3px] border-[1.5px] border-[#1a1a1a] transition-colors",
          checked ? "bg-band-green" : danger ? "bg-band-red" : "bg-band-grey"
        )}
      >
        <span
          aria-hidden
          className={cn(
            "absolute top-[1.5px] h-3 w-3 rounded-[2px] border border-[#1a1a1a] bg-card transition-[left]",
            checked ? "left-[21px]" : "left-[2px]"
          )}
        />
      </button>
      <span className="min-w-0">
        <span className="block font-mono text-[11px] font-bold uppercase tracking-[0.1em]">
          {label}
          <span className={cn("ml-2 font-normal", checked ? "text-band-green" : danger ? "text-band-red" : "text-ink-soft")}>
            {checked ? "ON" : "OFF"}
          </span>
        </span>
        {hint && <span id={`hint-${label.replace(/[^\w]/g, "")}`} className="block text-[11px] leading-snug text-ink-soft">{hint}</span>}
      </span>
    </div>
  );
}

export function Pill({ band, label }: { band: BandName; label: string }) {
  return (
    <span className="inline-flex items-center gap-1.5 rounded-[3px] border-[1.5px] border-ink px-1.5 py-0.5 font-mono text-[11px] font-bold uppercase tracking-[0.08em]">
      <BandChip band={band} size="sm" />
      {label}
    </span>
  );
}

/** Maps status to a band for admin tables. Order statuses delegate to the
 *  lifecycle's one ladder. Product status and ledger status live here. */
export function statusBand(status: string): BandName {
  if (status in ORDER_BAND) return ORDER_BAND[status as OrderStatus];
  switch (status) {
    case "Active": case "ok": return "green";
    case "Draft": case "limited": return "yellow";
    case "Inactive": case "error": return "red";
    default: return "grey";
  }
}

export const th = "px-2 py-1.5 text-left font-mono text-[11px] font-bold uppercase tracking-[0.16em] text-ink-soft";
export const td = "px-2 py-2 align-middle text-[13px]";

export function AdminTable({ head, children }: { head: ReactNode; children: ReactNode }) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full border-collapse">
        <thead>
          <tr className="border-b-[1.5px] border-ink">{head}</tr>
        </thead>
        <tbody>{children}</tbody>
      </table>
    </div>
  );
}
