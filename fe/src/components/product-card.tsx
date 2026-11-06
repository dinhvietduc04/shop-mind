"use client";

import { Check, Minus } from "lucide-react";
import { BandStrip, Button, PhotoPlate, StockDots } from "@/components/ui";
import { BAND_BG, bandForCategory, priceToBands } from "@/lib/bands";
import { useAddToCart, useCart, useRemoveCartItem } from "@/lib/api/hooks";
import { formatUsd, ApiError, type ProductDto } from "@/lib/api/types";
import { cn } from "@/lib/cn";

const LIGHT_TABS = new Set(["white", "gold", "silver", "yellow", "grey"]);

const specLabel = (key: string) => key.replace(/_/g, " ");

export function ProductCard({ product }: { product: ProductDto }) {
  const { data: cart } = useCart();
  const add = useAddToCart();
  const remove = useRemoveCartItem();
  const line = cart?.items.find((l) => l.productId === product.id);
  const soldOut = product.availableStock <= 0;
  const band = bandForCategory(product.categoryName?.toLowerCase());
  const specs = Object.entries(product.specs ?? {}).slice(0, 3);
  const photo = product.images.find((i) => i.url)?.url || undefined;

  const showError =
    add.error instanceof ApiError ? add.error.message : null;

  return (
    <article className="relative rounded-label border-[1.5px] border-ink bg-card p-5">
      <span
        aria-hidden
        className={cn(
          "absolute -top-[9px] right-4 rounded-[3px] border-[1.5px] border-[#1a1a1a] px-2 font-mono text-[10px] font-bold uppercase tracking-[0.18em]",
          BAND_BG[band],
          LIGHT_TABS.has(band) ? "text-[#1a1a1a]" : "text-[#fffdf7]"
        )}
      >
        {(product.categoryName ?? "misc").slice(0, 3).toUpperCase()} · {product.sku}
      </span>

      <PhotoPlate sku={product.sku} band={band} name={product.name} image={photo} />

      <h3 className="text-[17px] font-extrabold uppercase tracking-[0.03em]">
        {product.name}
      </h3>
      <p className="font-mono text-[11px] uppercase tracking-[0.16em] text-ink-soft">
        {product.brand} · {product.slug.split("-").slice(-2).join(" ")}
      </p>

      {specs.length > 0 && (
        <table className="mt-4 w-full border-collapse text-[12px]">
          <tbody>
            {specs.map(([key, value]) => (
              <tr key={key} className="border-y border-line/50">
                <th className="w-[86px] bg-kraft/60 px-2.5 py-1.5 text-left align-top font-mono text-[10px] font-bold uppercase tracking-[0.14em] text-ink-soft">
                  {specLabel(key)}
                </th>
                <td className="px-2.5 py-1.5">{String(value)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <div className="mt-4 flex items-end justify-between gap-3">
        <div>
          <BandStrip bands={priceToBands(product.price)} />
          <div className="mt-1.5 font-mono text-[19px] font-bold tabular-nums leading-none">
            {formatUsd(product.price)}
          </div>
        </div>
        <StockDots qty={product.availableStock} />
      </div>

      <div className="mt-4 flex flex-wrap gap-2">
        {soldOut ? (
          <Button variant="ghost" disabled title="Bin empty — restock ETA unknown">
            Bin empty
          </Button>
        ) : line ? (
          <Button
            variant="ghost"
            onClick={() => remove.mutate(product.id)}
            disabled={remove.isPending}
            aria-label={`Remove ${product.name} from cart`}
          >
            <Minus size={13} aria-hidden />
            In bin · {line.quantity} — {remove.isPending ? "removing…" : "remove"}
          </Button>
        ) : (
          <Button variant="add" onClick={() => add.mutate(product.id)} disabled={add.isPending}>
            <Check size={13} aria-hidden />
            {add.isPending ? "Adding…" : "Add to bin"}
          </Button>
        )}
      </div>

      {showError && (
        <p role="alert" className="mt-3 font-mono text-[11px] uppercase tracking-[0.1em] text-band-red">
          {showError}
        </p>
      )}
    </article>
  );
}
