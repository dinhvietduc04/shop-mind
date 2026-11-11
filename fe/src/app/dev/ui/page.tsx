"use client";

import Link from "next/link";
import { ArrowLeft, RotateCcw } from "lucide-react";
import {
  BandChip,
  Button,
  EmptyState,
  ErrorNote,
  Field,
  MagnitudeTick,
  PhotoPlate,
  Skeleton,
  StockDots,
} from "@/components/ui";
import { BAND_DIGIT, MAGNITUDE_LABEL, magnitudeBand } from "@/lib/bands";
import { formatUsd } from "@/lib/api/types";

const LADDER = Object.entries(BAND_DIGIT) as [keyof typeof BAND_DIGIT, number | null][];

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="border-b-[1.5px] border-dashed border-line py-8 first:pt-0">
      <h2 className="text-lg font-extrabold uppercase tracking-[0.08em]">{title}</h2>
      <div className="mt-5 flex flex-wrap items-start gap-6">{children}</div>
    </section>
  );
}

export default function UiKitPage() {
  return (
    <div className="mx-auto max-w-[1000px] px-6 py-8">
      <h1 className="text-2xl font-extrabold uppercase tracking-[0.04em]">UI kit</h1>
      <p className="mt-2 max-w-[62ch] text-sm leading-relaxed text-ink-soft">
        Every component in the Resistor Code system, in every state it can hold.
        Hover and focus each control. The dashed outline is the focus ring.
      </p>

      <div className="mt-8">
        <Section title="The whole band ladder">
          <div className="grid grid-cols-6 gap-2">
            {LADDER.map(([band, digit]) => (
              <div key={band} className="flex flex-col items-center gap-1.5">
                <BandChip band={band} size="lg" title={band} />
                <span className="font-mono text-[11px] uppercase text-ink-soft">
                  {band}
                  {digit !== null ? ` · ${digit}` : ""}
                </span>
              </div>
            ))}
          </div>
        </Section>

        <Section title="Add, ghost, and human-only yellow buttons">
          <Button variant="add">Add to cart</Button>
          <Button variant="ghost">Ghost</Button>
          <Button variant="human">Review bill &amp; pay</Button>
          <Button variant="add" disabled>
            Add (disabled)
          </Button>
          <Button variant="ghost" disabled>
            Ghost (disabled)
          </Button>
        </Section>

        <Section title="Price magnitude band and stock dots">
          <p className="-mt-1 mb-3 max-w-[62ch] text-[12px] leading-relaxed text-ink-soft">
            One stripe per price is the resistor multiplier, encoding magnitude only
            (×1 … ×100k). The digits beside it are the actual value. The stripe
            shows magnitude at a glance, and you never need to decode it.
          </p>
          <div className="flex flex-col gap-2">
            {[49.99, 329, 1099].map((v) => (
              <div key={v} className="flex items-center gap-3">
                <MagnitudeTick value={v} />
                <span className="font-mono text-sm font-bold tabular-nums">{formatUsd(v)}</span>
                <span className="font-mono text-[11px] uppercase tracking-[0.14em] text-ink-soft">
                  {MAGNITUDE_LABEL[magnitudeBand(v)]}
                </span>
              </div>
            ))}
          </div>
          <div className="mt-4 flex flex-col gap-2">
            <StockDots qty={7} />
            <StockDots qty={2} />
            <StockDots qty={0} />
          </div>
        </Section>

        <Section title="Plate slot with a photo on file and the empty fallback">
          <div className="grid w-full gap-4 md:grid-cols-2">
            <PhotoPlate sku="A1-01" band="brown" name="ThinkPad X1 Carbon" image="/products/thinkpad-x1c.jpg" />
            <PhotoPlate sku="A1-02" band="brown" />
          </div>
        </Section>

        <Section title="Fields">
          <div className="grid w-full gap-5 md:grid-cols-3">
            <Field id="f1" label="Search" placeholder="16 GB laptop" />
            <Field id="f2" label="Budget" defaultValue="1200" />
            <Field id="f3" label="Quantity" error="Bin holds 2, not 5" defaultValue="5" />
          </div>
        </Section>

        <Section title="Loading, empty, and error states">
          <div className="flex w-full flex-col gap-4">
            <div className="grid grid-cols-3 gap-4">
              <Skeleton className="h-[150px]" />
              <Skeleton className="h-[150px]" />
              <Skeleton className="h-[150px]" />
            </div>
            <EmptyState title="No units match this filter">
              <p>Clear a band and try again.</p>
            </EmptyState>
            <ErrorNote>
              <strong>Redis cache offline.</strong> Serving from Postgres. Reads
              are slower. Prices stay live. No action needed.
            </ErrorNote>
          </div>
        </Section>

        <div className="flex gap-4 py-6">
          <Link
            href="/"
            className="inline-flex items-center gap-2 text-sm font-bold uppercase tracking-[0.14em] underline underline-offset-4"
          >
            <ArrowLeft size={14} aria-hidden /> Back to the catalog
          </Link>
          <button
            onClick={async () => {
              await fetch("/api/_mock/reset", { method: "POST" });
              location.reload();
            }}
            className="inline-flex items-center gap-2 text-sm font-bold uppercase tracking-[0.14em] text-ink-soft underline underline-offset-4 hover:text-ink"
          >
            <RotateCcw size={14} aria-hidden /> Reset demo data
          </button>
        </div>
      </div>
    </div>
  );
}
