"use client";

import Link from "next/link";
import { ArrowRight, MessageSquareText } from "lucide-react";
import { BandChip } from "@/components/ui";
import { useAddToCart } from "@/lib/api/hooks";
import { priceToBands } from "@/lib/bands";
import { formatUsd, ApiError } from "@/lib/api/types";

/** The fixture this card acts on — MacBook Air M3, the only laptop under the demo budget. */
const TARGET = { id: "p-macbook-air-m3", name: "MacBook Air M3", price: 1099 };

/**
 * F0 demo of the assistant drawer. The full chat surface lands at F4;
 * the Assist invariant is already load-bearing here: the assistant can
 * add to cart and quote — payment lives only on the human's bill page.
 * Numbers on this card match the seeded catalog exactly (traceability).
 */
export function AssistantPanel() {
  const add = useAddToCart();
  const bands = priceToBands(1200);
  const error =
    add.error instanceof ApiError ? add.error.message : null;

  return (
    <section className="rounded-label border-[1.5px] border-ink bg-drawer p-5 text-drawer-ink">
      <h2 className="flex items-center gap-2 text-[13px] font-extrabold uppercase tracking-[0.26em]">
        <MessageSquareText size={15} aria-hidden />
        The assistant · turn 41
      </h2>

      <p className="mt-3 text-[14px] leading-relaxed">
        “One unit in bin laptops decodes under your budget band — search set to{" "}
        <span className="inline-flex translate-y-[3px] items-end gap-[3px]">
          {bands.map((b, i) => (
            <BandChip key={i} band={b} size="sm" title={b} />
          ))}
        </span>{" "}
        <span className="font-mono">1·2·0·0</span>: the MacBook Air M3 at{" "}
        {formatUsd(TARGET.price)}. The ThinkPad X1 Carbon is {formatUsd(1499)} —
        out of band but stronger RAM. Prices and stock came from this turn’s
        catalog calls, not from my weights.”
      </p>

      <p className="mt-3 font-mono text-[11px] uppercase tracking-[0.14em] text-drawer-ink/60">
        trail: search_products(category=laptops, ram_gb≥8, maxPrice=1200) →
        get_product → add_to_cart
      </p>

      <div className="mt-5 flex flex-wrap items-center gap-3">
        <button
          onClick={() => add.mutate(TARGET.id)}
          disabled={add.isPending || add.isSuccess}
          className="inline-flex items-center gap-2 rounded-[4px] border-[1.5px] border-drawer-ink/50 px-4 py-2 text-[12px] font-extrabold uppercase tracking-[0.14em] hover:border-drawer-ink disabled:opacity-50"
        >
          {add.isSuccess
            ? `Added ${TARGET.name} ✓`
            : `Add ${TARGET.name} — ${formatUsd(TARGET.price)}`}
        </button>
        <Link
          href="/quote"
          className="inline-flex items-center gap-2 rounded-[4px] border-[1.5px] border-[#1a1a1a] bg-band-yellow px-4 py-2 text-[12px] font-extrabold uppercase tracking-[0.14em] text-[#1a1a1a] hover:brightness-110"
        >
          Review bill
          <ArrowRight size={13} aria-hidden />
        </Link>
        <span className="w-full font-mono text-[10px] uppercase tracking-[0.12em] text-drawer-ink/50">
          payment is on the bill page · a yellow control is human-only · the
          assistant cannot press it
        </span>
        {error && (
          <span
            role="alert"
            className="w-full font-mono text-[10px] uppercase tracking-[0.12em] text-band-red"
          >
            {error}
          </span>
        )}
      </div>
    </section>
  );
}
