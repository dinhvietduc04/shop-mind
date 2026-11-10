"use client";

import { Check, Minus } from "lucide-react";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { Button, MagnitudeTick, PhotoPlate, StockDots } from "@/components/ui";
import { BAND_BG, bandForCategory } from "@/lib/bands";
import { useAddToCart, useCart, useRemoveCartItem } from "@/lib/api/hooks";
import { formatUsd, ApiError, type ProductDto } from "@/lib/api/types";
import { asMember } from "@/lib/session";
import { cn } from "@/lib/cn";

const LIGHT_TABS = new Set(["white", "gold", "silver", "yellow", "grey"]);

import { specLabel } from "@/lib/attributes/registry";

export function ProductCard({ product }: { product: ProductDto }) {
  const { data: cart } = useCart();
  const add = useAddToCart();
  const remove = useRemoveCartItem();
  const router = useRouter();
  const pathname = usePathname();
  /* Guests can browse everything the real API leaves public — but a cart is
     per-user, so filling one goes through the turnstile first. asMember()
     hides the settle-ordering protocol: a restoring member is never bounced. */
  const onAdd = async () => {
    if (!(await asMember())) {
      router.push(`/login?next=${encodeURIComponent(`${pathname}${window.location.search}`)}`);
      return;
    }
    add.mutate({ productId: product.id });
  };
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
          "absolute -top-[9px] right-4 rounded-[3px] border-[1.5px] border-[#1a1a1a] px-2 font-mono text-[11px] font-bold uppercase tracking-[0.18em]",
          BAND_BG[band],
          LIGHT_TABS.has(band) ? "text-[#1a1a1a]" : "text-[#fffdf7]"
        )}
      >
        {(product.categoryName ?? "misc").slice(0, 3).toUpperCase()} · {product.sku}
      </span>

      <PhotoPlate sku={product.sku} band={band} name={product.name} image={photo} />

      {/* fixed 2-line height + clamp → brand/spec rows start level across cards */}
      <h2 className="min-h-[2.4em] text-[17px] font-extrabold uppercase leading-[1.2] tracking-[0.03em] line-clamp-2">
        <Link
          href={`/product/${product.slug}`}
          className="hover:underline decoration-[1.5px] underline-offset-4"
        >
          {product.name}
        </Link>
      </h2>
      <p className="font-mono text-[11px] uppercase tracking-[0.16em] text-ink-soft">
        {product.brand} · {product.slug.split("-").slice(-2).join(" ")}
      </p>

      {/* specs always occupy three rows' height (invisible spacers) so the
          price/stock line below sits at the same y in every card */}
      <table className="mt-4 w-full border-collapse text-[12px]">
        <tbody>
          {specs.map(([key, value]) => (
            <tr key={key} className="border-y border-line/50">
              <th className="w-[86px] bg-kraft/60 px-2.5 py-1.5 text-left align-top font-mono text-[11px] font-bold uppercase tracking-[0.14em] text-ink-soft">
                {specLabel(key)}
              </th>
              <td className="px-2.5 py-1.5">{String(value)}</td>
            </tr>
          ))}
          {Array.from({ length: Math.max(0, 3 - specs.length) }).map((_, i) => (
            <tr key={`pad-${i}`} aria-hidden className="border-y border-transparent">
              <td colSpan={2} className="invisible px-2.5 py-1.5">—</td>
            </tr>
          ))}
        </tbody>
      </table>

      <div className="mt-4 flex items-end justify-between gap-3">
        <div>
          <div className="flex items-center gap-2">
            <MagnitudeTick value={product.price} />
            <span className="font-mono text-[19px] font-bold tabular-nums leading-none">
              {formatUsd(product.price)}
            </span>
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
            In cart · {line.quantity} — {remove.isPending ? "removing…" : "remove"}
          </Button>
        ) : (
          <Button variant="add" onClick={onAdd} disabled={add.isPending}>
            <Check size={13} aria-hidden />
            {add.isPending ? "Adding…" : "Add to cart"}
          </Button>
        )}
        <Link
          href={`/product/${product.slug}`}
          className="self-center font-mono text-[11px] uppercase tracking-[0.14em] text-ink-soft underline decoration-line underline-offset-4 hover:text-ink"
        >
          Spec sheet →
        </Link>
      </div>

      {showError && (
        <p role="alert" className="mt-3 font-mono text-[11px] uppercase tracking-[0.1em] text-band-red">
          {showError}
        </p>
      )}
    </article>
  );
}
