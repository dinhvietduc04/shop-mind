import { type ReactNode } from "react";
import {
  type BandName,
  bandClass,
  STOCK_BAND,
  STOCK_LABEL,
  stockState,
  type StockState,
} from "@/lib/bands";
import { cn } from "@/lib/cn";

/* ---------------------------------- Button --------------------------------- */

type ButtonVariant = "add" | "ghost" | "human";

const buttonVariants: Record<ButtonVariant, string> = {
  add: "bg-band-red text-[#fffdf7] border-band-red hover:brightness-110",
  ghost: "bg-card text-ink border-line hover:border-ink hover:bg-kraft",
  human: "bg-band-yellow text-[#1a1a1a] border-[#1a1a1a] hover:brightness-108",
};

export function Button({
  variant = "ghost",
  className,
  children,
  ...props
}: React.ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: ButtonVariant;
  children: ReactNode;
}) {
  return (
    <button
      className={cn(
        "inline-flex items-center gap-2 rounded-[4px] border-[1.5px] px-4 py-2",
        "text-[12px] font-extrabold uppercase tracking-[0.14em]",
        "transition-[filter,background,border] duration-150 ease-out",
        "disabled:pointer-events-none disabled:opacity-40",
        buttonVariants[variant],
        className
      )}
      {...props}
    >
      {children}
    </button>
  );
}

/* --------------------------------- BandChip -------------------------------- */

export function BandChip({
  band,
  size = "md",
  title,
}: {
  band: BandName;
  size?: "sm" | "md" | "lg";
  title?: string;
}) {
  return (
    <span
      title={title}
      aria-hidden
      className={cn(
        "inline-block rounded-[3px] border-[1.5px] border-[#1a1a1a] dark:border-[#8a8068]",
        bandClass(band),
        size === "sm" && "h-3 w-[7px]",
        size === "md" && "h-[22px] w-[12px]",
        size === "lg" && "h-8 w-4"
      )}
    />
  );
}

export function BandStrip({ bands }: { bands: BandName[] }) {
  return (
    <span className="inline-flex items-end gap-[3px]">
      {bands.map((b, i) => (
        <span
          key={i}
          className="band-in inline-flex"
          style={{ animationDelay: `${i * 70}ms` }}
        >
          <BandChip band={b} />
        </span>
      ))}
    </span>
  );
}

/* -------------------------------- StockDots -------------------------------- */

export function StockDots({ qty, withLabel = true }: { qty: number; withLabel?: boolean }) {
  const state: StockState = stockState(qty);
  const filled = state === "in" ? 3 : state === "low" ? 2 : 1;
  return (
    <span className="inline-flex items-center gap-2">
      <span className="flex gap-[4px]" aria-hidden>
        {[0, 1, 2].map((i) => (
          <span
            key={i}
            className={cn(
              "h-[9px] w-[9px] rounded-full border-[1.5px] border-[#1a1a1a] dark:border-[#8a8068]",
              i < filled ? bandClass(STOCK_BAND[state]) : "bg-transparent opacity-30"
            )}
          />
        ))}
      </span>
      {withLabel && (
        <span className="font-mono text-[11px] uppercase tracking-[0.1em] text-ink-soft">
          {STOCK_LABEL[state]} · {qty}
        </span>
      )}
    </span>
  );
}

/* -------------------------------- PhotoPlate ------------------------------- */

/**
 * Placeholder for product photography: an empty plate slot in catalog-sheet
 * language — registration corners, plate number, category band. Honest demo
 * state; replaced by real imagery when assets exist (PRODUCT.md: no fake claims).
 */
/**
 * Plate slot for product photography. Empty state: dashed registration frame,
 * honest "photo not on file" label. With a photo: the image sits on the plate,
 * captioned with its plate number. Local files — see public/products/PROVENANCE.md.
 */
export function PhotoPlate({
  sku,
  band,
  name,
  image,
  compact = false,
}: {
  sku: string;
  band: BandName;
  name?: string;
  image?: string;
  compact?: boolean;
}) {
  if (image) {
    return (
      <figure
        className={cn(
          "relative overflow-hidden border-[1.5px] border-line bg-card",
          compact ? "h-full w-full" : "mt-2 mb-5 h-[150px]"
        )}
      >
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img
          src={image}
          alt={name ? `${name} — product photo` : `Product ${sku}`}
          className={cn("object-contain", compact ? "h-full w-full p-0.5" : "h-full w-full p-2")}
          loading="lazy"
        />
        {!compact && (
          <figcaption className="absolute bottom-1 left-2 flex items-center gap-1.5 rounded-[3px] bg-card/90 px-1.5 py-0.5">
            <BandChip band={band} size="sm" title={band} />
            <span className="font-mono text-[9px] uppercase tracking-[0.18em] text-ink-soft">
              pl. {sku}
            </span>
          </figcaption>
        )}
      </figure>
    );
  }
  const corner = "absolute h-3 w-3 border-line";
  return (
    <div
      role="img"
      aria-label={`No photo on file for plate ${sku}`}
      className="relative mt-2 mb-5 flex h-[150px] items-center justify-center border-[1.5px] border-dashed border-line bg-kraft/50"
    >
      <span aria-hidden className={cn("left-2 top-2 border-l-[1.5px] border-t-[1.5px]", corner)} />
      <span aria-hidden className={cn("right-2 top-2 border-r-[1.5px] border-t-[1.5px]", corner)} />
      <span aria-hidden className={cn("bottom-2 left-2 border-b-[1.5px] border-l-[1.5px]", corner)} />
      <span aria-hidden className={cn("bottom-2 right-2 border-b-[1.5px] border-r-[1.5px]", corner)} />
      <span className="flex items-center gap-2.5">
        <BandChip band={band} size="sm" title={band} />
        <span className="font-mono text-[10px] uppercase tracking-[0.22em] text-ink-soft">
          pl. {sku} · photo not on file
        </span>
      </span>
    </div>
  );
}

/* ---------------------------------- Field ---------------------------------- */

export function Field({
  label,
  error,
  id,
  ...props
}: React.InputHTMLAttributes<HTMLInputElement> & { label: string; error?: string }) {
  return (
    <label htmlFor={id} className="block">
      <span className="mb-1.5 block text-[11px] font-bold uppercase tracking-[0.22em] text-ink-soft">
        {label}
      </span>
      <input
        id={id}
        // Browser extensions inject attributes (e.g. caret-color) into inputs
        // before hydration; suppress the attribute diff on this element only.
        suppressHydrationWarning
        className={cn(
          "w-full rounded-[4px] border-[1.5px] bg-card px-3 py-2 text-[14px] text-ink",
          "placeholder:text-ink-soft/70 disabled:opacity-40",
          error ? "border-band-red" : "border-line focus:border-ink"
        )}
        {...props}
      />
      {error && (
        <span role="alert" className="mt-1 block text-[12px] font-semibold text-band-red">
          {error}
        </span>
      )}
    </label>
  );
}

/* ----------------------------- States: skeleton, empty, error ---------------------------- */

export function Skeleton({ className }: { className?: string }) {
  return (
    <div
      aria-hidden
      className={cn(
        "animate-pulse rounded-[4px] border-[1.5px] border-line/40 bg-kraft-deep/40",
        className
      )}
    />
  );
}

export function EmptyState({ title, children }: { title: string; children?: ReactNode }) {
  return (
    <div className="flex flex-col items-start gap-3 rounded-label border-[1.5px] border-dashed border-line bg-card p-8">
      <h3 className="text-lg font-extrabold uppercase tracking-[0.06em]">{title}</h3>
      {children && <div className="text-sm text-ink-soft">{children}</div>}
    </div>
  );
}

export function ErrorNote({ children }: { children: ReactNode }) {
  return (
    <div
      role="alert"
      className="flex items-start gap-3 rounded-[4px] border-[1.5px] border-band-red bg-card p-4 text-sm"
    >
      <span aria-hidden className="mt-0.5 inline-block h-3 w-3 rounded-full bg-band-red" />
      <div className="text-ink">{children}</div>
    </div>
  );
}
